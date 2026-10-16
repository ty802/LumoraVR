// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lumora.Core.Assets;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using Lumora.Nexus.Transport;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Session;

// Handles peer-to-peer asset transfer over the control message channel.
//
// Protocol:
//   Client -> AssetRequest(uri) -> Host/Owner
//   Host -> AssetTransmissionStart(id, uri, totalBytes, format, totalBytes64) -> Client
//   Client -> AssetNextChunkRequest(id, -1) -> Host   (ready for chunks)
//   Host -> AssetChunk(id, offset, data, offset64) as many as the link's window holds -> Client
//   Client -> AssetNextChunkRequest(id, offset64) for every chunk that lands -> Host
//   ...until done...
//   Host -> AssetNotAvailable(uri) -> Client  (if it can't serve the file)
//
// Host relay: a client always asks the host for an asset, but the host might not own it - another
// peer may have imported it (its URI carries that peer's machine id). When the host can't serve a
// local:// asset from its own store, instead of giving up it forwards the request to the owning peer,
// receives the bytes, and streams them on to the original requester. Several requesters waiting on the
// same asset are coalesced onto one fetch. This is what lets "B imports content, C joins" work when
// the host is neither B nor C.
//
// Nothing here waits on an answer forever. A receiver that goes quiet holds only its own slots, and
// the sender starts its jobs over until it answers again; a requester that hears nothing asks again,
// and the sender folds a repeated ask into the job it already has. The first three-player session
// over Steam sat at four uploads of 0 B for good: one guest stopped answering, the pool of four
// slots was shared by everyone, and the least-recently-served pick kept handing that guest every
// slot that came free until it held all four and nobody else got a byte. -xlinka
//
// Files go from disk to the wire and from the wire to disk a chunk at a time, so neither end holds a
// whole file, and sizes and offsets are 64-bit. Both travel twice: the 32-bit fields a build without
// them reads, then the 64-bit ones after everything such a build looks at, so either build reads the
// other's messages. A file over 2 GB is only ever sent to a build that reads the wide fields, because
// an older one refuses anything over its own cap well before that. -xlinka
//
// A receiver on this build names the chunk each ack answers and takes chunks in any order, so the sender
// spreads its chunks over the transport's bulk lanes and paces each peer by its round trip (LinkPace). A
// build before that asks once per chunk with no offset, one ask fewer than it gets chunks, and needs them
// in order: it gets them in order on the background channel, paced the same way. -xlinka
public class SessionAssetTransferer : IDisposable
{

    private readonly struct JobID : IEquatable<JobID>
    {
        public readonly IConnection Connection;
        public readonly int ID;

        public JobID(IConnection connection, int id) { Connection = connection; ID = id; }
        public bool Equals(JobID other) => Connection == other.Connection && ID == other.ID;
        public override bool Equals(object? obj) => obj is JobID j && Equals(j);
        public override int GetHashCode() => HashCode.Combine(Connection, ID);
    }

    private const int ChunkSize = 64 * 1024;
    private const int MaxReceiveRuns = 4096;
    // What a job counts against its peer's start budget.
    private const int AdmitChunks = 16;

    // Pacing; see LinkPace.
    private const long InitialWindow = 4L * ChunkSize;
    private const long MinWindow = 2L * ChunkSize;
    private const long MaxWindow = 16L * 1024 * 1024;
    private const double MinRoundSeconds = 0.02;
    private const double QueueMarginSeconds = 0.005;
    private const double QueueMarginShare = 0.05;
    private const double FloorEpochSeconds = 10;
    private const double IdleResetSeconds = 1;
    private const int RateRounds = 8;
    // How long a round's middle chunk may sit at its receiver before it is acked. Past this the receiver is
    // not keeping up: chunks queue in front of its world changes and streams, in the one line every
    // message waits in. A frame at the slowest rate anything renders at, and then some.
    private const double MaxHeldSeconds = 0.15;

    // Outbound (we are sending)

    private sealed class FileTransmitJob : IDisposable
    {
        public IConnection Target { get; }
        public int ID { get; }
        public Uri AssetUri { get; }

        private readonly string _filePath;
        private readonly string _format;
        private Stream? _source;
        private long _offset;
        private byte[]? _scratch;

        public bool AllSent => _source != null && _offset >= TotalBytes;

        public long TotalBytes { get; private set; }
        public long SentBytes => _offset;

        // What this job costs against its peer's start budget: a first window, or the whole file if smaller.
        public long Window => System.Math.Min(System.Math.Max(TotalBytes, 1), (long)AdmitChunks * ChunkSize);

        // When the receiver last asked for a chunk, or the start last went out, and how many starts
        // have gone. Read by the stall sweep.
        public double LastHeard;
        public int Starts;

        // The receiver asked for this start's first chunk, and whether it acks each chunk by offset and
        // takes them in any order.
        public bool Ready;
        public bool Lanes;

        // Chunks out and not acked yet, with when each went: by offset for a receiver that names them, in
        // order for one that does not. The last chunk to an older receiver is not here, as it never acks it.
        private readonly Dictionary<long, (double At, int Size)> _byOffset = new();
        private readonly Queue<(double At, int Size)> _inOrder = new();
        public long OutBytes { get; private set; }
        public bool Drained => _byOffset.Count == 0 && _inOrder.Count == 0;

        public void Track(long offset, double at, int size)
        {
            if (Lanes)
                _byOffset[offset] = (at, size);
            else
                _inOrder.Enqueue((at, size));
            OutBytes += size;
        }

        // The chunk an ack answers; a size of zero when it answers nothing still out.
        public (double At, int Size) Acked(long? offset)
        {
            (double At, int Size) hit;
            if (Lanes)
            {
                if (offset is not long at || !_byOffset.Remove(at, out hit))
                    return default;
            }
            else if (!_inOrder.TryDequeue(out hit))
            {
                return default;
            }
            OutBytes -= hit.Size;
            return hit;
        }

        public long ClearFlight()
        {
            long had = OutBytes;
            _byOffset.Clear();
            _inOrder.Clear();
            OutBytes = 0;
            return had;
        }

        public FileTransmitJob(string filePath, Uri assetUri, IConnection target, int id)
        {
            Target = target;
            ID = id;
            AssetUri = assetUri;
            _filePath = filePath;
            // A local:// URI is a content hash and carries no format. The only machine that still knows
            // what these bytes are is the one holding the file, and it knows because its cache file kept
            // the extension, so send it. Without it the receiver has a nameless blob and every decoder
            // that dispatches on extension - the mesh decoder above all - has nothing to dispatch on.
            // -xlinka
            _format = Path.GetExtension(filePath) ?? string.Empty;
            try { TotalBytes = new FileInfo(filePath).Length; }
            catch { TotalBytes = 0; }
        }

        // A queued job holds the path, not the bytes, and a started one reads its file as it goes. Reading
        // whole files at queue time put every file an avatar has into memory at once, read on the sync
        // thread under the data model lock in a single cycle, for jobs that would wait minutes for a slot.
        // A record stored encrypted is the one exception: its plaintext only exists in memory. -xlinka
        public bool Load(LocalDB? db)
        {
            if (_source != null)
                return true;
            try
            {
                var uriStr = AssetUri.OriginalString;
                if (db != null && db.IsStoredEncrypted(uriStr))
                {
                    var plain = db.ReadAssetBytesAsync(uriStr).GetAwaiter().GetResult();
                    if (plain == null)
                        return false;
                    _source = new MemoryStream(plain, writable: false);
                }
                else
                {
                    _source = new FileStream(_filePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, ChunkSize, FileOptions.SequentialScan);
                }
                TotalBytes = _source.Length;
                return true;
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"AssetTransferer: cannot read '{_filePath}' for {AssetUri.OriginalString}: {ex.Message}");
                Dispose();
                return false;
            }
        }

        // Also the restart: a start sent again begins the file over, and the receiver drops whatever it
        // had for this job and begins over with it.
        public ControlMessage Initialize()
        {
            _offset = 0;
            Ready = false;
            var msg = new ControlMessage(ControlMessage.Message.AssetTransmissionStart);
            msg.Targets.Add(Target);
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(ID);
            w.Write(AssetUri.OriginalString);
            w.Write((int)System.Math.Min(TotalBytes, int.MaxValue));
            // Appended after the fields a build without it wrote, so its reader stops cleanly at the
            // length and simply gets no format.
            w.Write(_format);
            w.Write(TotalBytes);
            msg.Payload = ms.ToArray();
            return msg;
        }

        public ControlMessage GetChunk()
        {
            int size = (int)System.Math.Min(ChunkSize, TotalBytes - _offset);
            _scratch ??= new byte[ChunkSize];
            int read = 0;
            if (size > 0)
            {
                _source!.Position = _offset;
                while (read < size)
                {
                    int n = _source.Read(_scratch, read, size - read);
                    if (n <= 0)
                        throw new EndOfStreamException($"{_filePath} ended at {_offset + read} of {TotalBytes} bytes");
                    read += n;
                }
            }

            var msg = new ControlMessage(ControlMessage.Message.AssetChunk);
            msg.Targets.Add(Target);
            using var ms = new MemoryStream(size + 32);
            using var w = new BinaryWriter(ms);
            w.Write(ID);
            w.Write((int)System.Math.Min(_offset, int.MaxValue));
            w.Write(size);
            w.Write(_scratch, 0, size);
            w.Write(_offset);
            msg.Payload = ms.ToArray();
            _offset += size;
            return msg;
        }

        public void Dispose()
        {
            _source?.Dispose();
            _source = null;
        }
    }

    // Inbound (we are receiving)

    private sealed class FileReceiveJob : IDisposable
    {
        public int ID { get; }
        public Uri AssetUri { get; }
        public IConnection Source { get; }

        private readonly long _totalSize;
        private long _received;
        // What has landed, as sorted runs of [start, end). Chunks come in any order: a sender on this build
        // spreads them over lanes, and a start sent again can have chunks of the one before still on the
        // way. The address is the content, so the bytes at an offset are the same whichever pass sent them.
        private readonly List<(long Start, long End)> _runs = new();
        private readonly string _tempPath;
        private FileStream? _sink;
        private bool _finished;

        public bool IsDone => _received >= _totalSize;

        public long TotalBytes => _totalSize;
        public long ReceivedBytes => _received;

        public double LastProgress;

        // tempPathFor is handed the sender's declared format so the file we land on disk keeps the
        // extension the asset actually has. The temp name is what the local database then adopts, and
        // the extension on that cache file is the only thing an extension-dispatching decoder has to go
        // on later, so losing it here quietly breaks every load of that asset from then on. -xlinka
        public FileReceiveJob(IConnection source, BinaryReader reader, Func<string, string> tempPathFor)
        {
            Source = source;
            ID = reader.ReadInt32();
            // Bound the URI string before constructing Uri (which itself has work to do).
            AssetUri = new Uri(reader.ReadBoundedString(NetworkLimits.MaxAssetUriBytes));
            long total = reader.ReadInt32();

            // Absent on a build that predates the format field, which leaves the stream exactly here.
            string? declaredFormat = null;
            if (HasMore(reader))
                declaredFormat = reader.ReadBoundedString(NetworkLimits.MaxAssetFormatBytes);
            if (reader.BaseStream.CanSeek && reader.BaseStream.Length - reader.BaseStream.Position >= sizeof(long))
                total = reader.ReadInt64();
            Format = SanitizeFormat(declaredFormat);

            // The size is the peer's word, and the file it names is made on our disk as the bytes arrive,
            // so it is held to a ceiling before anything is opened.
            if (total < 0 || total > NetworkLimits.MaxAssetTransferTotalBytes)
                throw new InvalidDataException($"Asset transfer total size {total} out of bounds (cap {NetworkLimits.MaxAssetTransferTotalBytes}).");
            _totalSize = total;

            _tempPath = tempPathFor(Format);
            _sink = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize);
        }

        public string Format { get; }

        private static bool HasMore(BinaryReader reader)
        {
            var stream = reader.BaseStream;
            return stream.CanSeek && stream.Position < stream.Length;
        }

        // Writes the chunk where it goes and returns its offset, for the ack. One wholly here already is a
        // repeat and passed over.
        public long ApplyChunk(BinaryReader reader)
        {
            long offset = reader.ReadInt32();
            int size = reader.ReadInt32();

            // Bound the chunk size, reject negative offsets, and ensure the chunk lands
            // inside the pre-declared length.
            if (size < 0 || size > NetworkLimits.MaxAssetChunkBytes)
                throw new InvalidDataException($"Asset chunk size {size} out of bounds (cap {NetworkLimits.MaxAssetChunkBytes}).");

            var data = reader.ReadBoundedBytes(size, NetworkLimits.MaxAssetChunkBytes);
            if (HasMore(reader))
                offset = reader.ReadInt64();
            if (offset < 0 || offset > _totalSize - size)
                throw new InvalidDataException($"Asset chunk offset {offset} + size {size} exceeds length {_totalSize}.");

            if (size == 0 || Covers(offset, offset + size))
                return offset;
            // A sender scatters no further than its window, a few hundred runs at most. A peer laying down
            // slivers to grow the list without end is not sending a file.
            if (_runs.Count >= MaxReceiveRuns)
                throw new InvalidDataException($"Asset chunks scattered over more than {MaxReceiveRuns} runs.");
            _sink!.Position = offset;
            _sink.Write(data, 0, size);
            Cover(offset, offset + size);
            return offset;
        }

        private bool Covers(long start, long end)
        {
            int i = RunAtOrBefore(start);
            return i >= 0 && _runs[i].End >= end;
        }

        // Index of the last run starting at or before `at`, or -1.
        private int RunAtOrBefore(long at)
        {
            int lo = 0, hi = _runs.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_runs[mid].Start <= at) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        private void Cover(long start, long end)
        {
            int i = RunAtOrBefore(start);
            if (i < 0 || _runs[i].End < start)
                i++;
            long had = 0;
            int j = i;
            while (j < _runs.Count && _runs[j].Start <= end)
            {
                start = System.Math.Min(start, _runs[j].Start);
                end = System.Math.Max(end, _runs[j].End);
                had += _runs[j].End - _runs[j].Start;
                j++;
            }
            _runs.RemoveRange(i, j - i);
            _runs.Insert(i, (start, end));
            _received += end - start - had;
        }

        // An ack: the offset of the chunk that landed, or -1 for ready, then how long in microseconds the
        // chunk sat here between coming off the wire and this going out. A build that predates offsets
        // reads the id and stops.
        public ControlMessage NextChunkRequest(IConnection target, long offset, int heldMicros = 0)
        {
            var msg = new ControlMessage(ControlMessage.Message.AssetNextChunkRequest);
            msg.Targets.Add(target);
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(ID);
            w.Write(offset);
            w.Write(heldMicros);
            msg.Payload = ms.ToArray();
            return msg;
        }

        public string FinalizeAndGetFile()
        {
            _sink!.Flush();
            _sink.Dispose();
            _sink = null;
            _finished = true;
            return _tempPath;
        }

        // A job let go of before it finished takes its partial file with it.
        public void Dispose()
        {
            if (_sink != null)
            {
                try { _sink.Dispose(); } catch { }
                _sink = null;
            }
            if (!_finished)
            {
                try { File.Delete(_tempPath); } catch { }
            }
        }
    }

    // Uploads in flight per peer, and in all. Per peer so a peer that stops answering ties up its own
    // transfers and nobody else's. Within its count a peer is also held to a byte budget, each job costing
    // what it can have on the wire at once: a thousand small files go many at a time, big ones a few. -xlinka
    private const int MaxTransmitJobsPerPeer = 16;
    private const int MaxTransmitJobs = 64;
    private const long MaxInFlightBytesPerPeer = 4L * 1024 * 1024;

    // How much one peer gets on the wire at once, learned from its own chunks: the trip from a chunk going
    // out to its ack coming back, and the bytes acked each round. The least trip seen lately is the link
    // with nothing queued on it, and the most it delivered a second over the last few rounds is what it
    // carries. Each round, about one least trip long:
    //   a middle trip more than a margin over that floor means data is sitting in a queue somewhere, holding
    //   up whatever else crosses the link, hands and voice with it, for as long as the window keeps it
    //   there. The window drops to what the link carries in one floor trip and half the margin, held
    //   between half and three quarters of what it was. The middle trip and not the best, since the queue
    //   anything else meets is the one most of the round saw;
    //   otherwise, if the window was in use, it grows: doubling until delay first starts to build, then a
    //   sixteenth a round.
    // Trips are the wire's alone: the receiver says how long it held each chunk before acking, and the ack is
    // timed from when it came off the wire, so neither end's frame shows up as a queue. What noise is left, a
    // transport that hands messages over once a frame above all, is measured as the usual gap between a
    // round's middle and least trips and allowed for. The hold is a queue of its own and is judged on its
    // own: a receiver too slow to keep up holds chunks in front of its world changes and streams, so a round
    // whose middle hold is past MaxHeldSeconds backs off the same way, whatever the wire looks like. On a
    // receiver drawing ten frames a second, allowing for noise and counting the hold as wire both let its
    // backlog grow to seconds of chunks with everything else waiting behind them.
    // A lost packet alone moves nothing, since the transport sends it again; if it means anything it shows
    // up as delay. A window left idle goes back to slow start from a quarter of itself, so what a busy link
    // learned does not land on it all at once later. -xlinka
    private sealed class LinkPace
    {
        public readonly List<FileTransmitJob> Jobs = new();
        public int Next;
        public long Window = InitialWindow;
        public long InFlight;
        private bool _slowStart = true;
        private double _floor = double.MaxValue;
        private double _epochBest = double.MaxValue, _lastEpochBest = double.MaxValue, _epochStart = -1;
        private double _epochMiddle = double.MaxValue, _lastEpochMiddle = double.MaxValue, _lastMiddle = -1, _middleJitter;
        private double _roundStart = -1;
        private readonly List<double> _roundTrips = new();
        private readonly List<double> _roundHolds = new();
        private long _roundPeak, _roundAcked;
        private readonly double[] _rates = new double[RateRounds];
        private int _rateAt;
        private readonly double[] _recent = new double[3];
        private readonly double[] _recentHolds = new double[3];
        private int _recentAt;
        private double _lastActive = -1;
        private double _noise = -1;

        public bool HasRoom => InFlight + ChunkSize <= Window;

        private double Carries()
        {
            double most = 0;
            foreach (var rate in _rates)
                most = System.Math.Max(most, rate);
            return most;
        }

        public void Sent(int size, double now)
        {
            if (InFlight == 0 && _lastActive >= 0 && now - _lastActive > IdleResetSeconds)
            {
                Window = System.Math.Max(InitialWindow, Window / 4);
                _slowStart = true;
                _roundStart = -1;
            }
            InFlight += size;
            _lastActive = now;
            if (InFlight > _roundPeak)
                _roundPeak = InFlight;
        }

        public void Released(long size) => InFlight = System.Math.Max(0, InFlight - size);

        public void Acked(int size, double trip, double held, double now)
        {
            Released(size);
            _lastActive = now;
            if (trip <= 0)
                return;
            if (_epochStart < 0 || now - _epochStart > FloorEpochSeconds)
            {
                _lastEpochBest = _epochStart < 0 ? double.MaxValue : _epochBest;
                _epochBest = double.MaxValue;
                _lastEpochMiddle = _epochStart < 0 ? double.MaxValue : _epochMiddle;
                _epochMiddle = double.MaxValue;
                _epochStart = now;
            }
            _epochBest = System.Math.Min(_epochBest, trip);
            _floor = System.Math.Min(_epochBest, _lastEpochBest);

            if (_roundStart < 0)
            {
                _roundStart = now;
                _roundTrips.Clear();
                _roundHolds.Clear();
                _roundPeak = InFlight;
                _roundAcked = 0;
            }
            _roundTrips.Add(trip);
            _roundHolds.Add(held);
            _roundAcked += size;
            double span = now - _roundStart;
            double margin = QueueMarginSeconds + _floor * QueueMarginShare;

            // Slow start doubles a round, so a queue it starts is a round deep before any round ends; it
            // stops on the least of the last three trips, at once. It allows an eighth of the floor more:
            // each doubling goes out as one burst, and the tail of a burst waits behind its head on its way
            // out, which on a long link would end slow start long before the link is full.
            _recentHolds[_recentAt % _recent.Length] = held;
            _recent[_recentAt++ % _recent.Length] = trip;
            if (_slowStart && _recentAt >= _recent.Length)
            {
                double recent = double.MaxValue, recentHold = double.MaxValue;
                foreach (var t in _recent)
                    recent = System.Math.Min(recent, t);
                foreach (var h in _recentHolds)
                    recentHold = System.Math.Min(recentHold, h);
                if (recent - _floor > margin + _floor / 8 + System.Math.Max(0, _noise) || recentHold > MaxHeldSeconds)
                {
                    _slowStart = false;
                    double rate = span > 0 ? _roundAcked / span : 0;
                    long fits = (long)(System.Math.Max(rate, Carries()) * (_floor + margin / 2));
                    Window = System.Math.Max(MinWindow, System.Math.Max(Window / 2, System.Math.Min(Window * 3 / 4, fits)));
                    _roundStart = -1;
                    return;
                }
            }

            if (span < System.Math.Max(_floor, MinRoundSeconds))
                return;

            _rates[_rateAt++ % RateRounds] = _roundAcked / span;
            _roundTrips.Sort();
            double middle = _roundTrips[_roundTrips.Count / 2];
            if (_roundTrips.Count >= 4)
            {
                double spread = middle - _roundTrips[0];
                _noise = _noise < 0 ? spread : _noise + (spread - _noise) / 8;
            }
            // The middle trip held against the best middle trip of lately, not the best single trip: the two
            // carry the same scatter, so a transport that hands messages over a frame at a time does not
            // look like a queue, and anything standing in a queue lifts every trip and so the middle. What
            // the middle itself wanders round to round is allowed for.
            _epochMiddle = System.Math.Min(_epochMiddle, middle);
            double middleFloor = System.Math.Min(_epochMiddle, _lastEpochMiddle);
            if (_lastMiddle >= 0)
                _middleJitter += (System.Math.Abs(middle - _lastMiddle) - _middleJitter) / 8;
            _lastMiddle = middle;
            double queued = System.Math.Min(middle - _floor - System.Math.Max(0, _noise), middle - middleFloor - _middleJitter);
            _roundHolds.Sort();
            bool backlogged = _roundHolds[_roundHolds.Count / 2] > MaxHeldSeconds;
            if (queued > margin || backlogged)
            {
                long fits = (long)(Carries() * (_floor + margin / 2));
                Window = System.Math.Max(MinWindow, System.Math.Max(Window / 2, System.Math.Min(Window * 3 / 4, fits)));
                _slowStart = false;
            }
            else if (_slowStart && queued > margin / 2)
            {
                _slowStart = false;
            }
            else if (_roundPeak >= Window * 3 / 4)
            {
                Window = System.Math.Min(MaxWindow, _slowStart ? Window * 2 : Window + System.Math.Max(ChunkSize, Window / 16));
            }
            _roundStart = now;
            _roundTrips.Clear();
            _roundHolds.Clear();
            _roundPeak = InFlight;
            _roundAcked = 0;
        }
    }

    private readonly Dictionary<IConnection, LinkPace> _paces = new();

    private LinkPace PaceFor(IConnection target)
    {
        if (!_paces.TryGetValue(target, out var pace))
            _paces[target] = pace = new LinkPace();
        return pace;
    }

    // A started upload that hears nothing from its receiver for this long (times its start, up to four)
    // sends its start again, from the top, for as long as the link stays up. The wait grows so a receiver
    // whose round trip is longer than one window still gets a run long enough to finish. Fields, not
    // constants, so a test can shorten them. -xlinka
    internal static double StallSeconds = 15;
    // A request that has heard nothing back for this long (times its attempt, up to four) is asked
    // again. A request still waiting its turn at the sender costs one small message per ask.
    internal static double RequestRetrySeconds = 30;
    // A receive buffer nothing has touched for this long is let go. Anything still wanted is asked for
    // again by its request and starts over.
    internal static double ReceiveIdleSeconds = 120;
    private const double SweepIntervalSeconds = 0.5;

    // What an incoming asset is called when the sender declared no format, or declared one we refuse.
    private const string UnknownFormat = ".asset";

    // The declared format ends up in a filename, so it is a peer-controlled path fragment and gets no
    // benefit of the doubt: a leading dot and a short run of ASCII alphanumerics, nothing else. That
    // rules out separators, "..", drive letters and every unicode lookalike in one pass, and a refusal
    // costs only the neutral extension plus the decoder's content sniff. -xlinka
    private static string SanitizeFormat(string? format)
    {
        if (string.IsNullOrEmpty(format) || format![0] != '.' || format.Length < 2 || format.Length > 12)
            return UnknownFormat;

        for (int i = 1; i < format.Length; i++)
        {
            char c = format[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (!ok)
                return UnknownFormat;
        }

        return format;
    }

    // How many distinct assets the host fetches from owning peers at once to relay. Past that they wait
    // their turn rather than being refused: refusing them is what left most of a joiner's avatar grey for
    // everyone else, one request in eight answered and the rest told the asset did not exist. The queue
    // behind them is bounded too, against a peer asking for a flood of addresses. -xlinka
    private const int MaxConcurrentRelays = 16;
    private const int MaxQueuedRelays = 8192;

    // Uri.ToString() LOWERCASES the authority, and our machine ids are case-sensitive, so keying a
    // request on it makes a client-imported asset unfindable while a host-imported one works. That is
    // the same class of bug that once stopped peer meshes transferring at all; OriginalString is the
    // spelling the sender actually replicated. -xlinka
    private static string UriKey(Uri uri) => uri.OriginalString;

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    // How long a message sat here since it came off the wire, in microseconds.
    private static int HeldMicros(SyncMessage message, double now)
    {
        if (message.Arrived <= 0)
            return 0;
        double held = now - message.Arrived / (double)Stopwatch.Frequency;
        return (int)System.Math.Clamp(held * 1e6, 0, int.MaxValue);
    }

    // A derived address (a texture's sidecar or a smaller rung) is asked for on the chance its owner made
    // it, so a no is routine and not worth a warning.
    private static bool IsDerived(string uriStr) => uriStr.IndexOf('~') >= 0;

    // Holds the completion callback plus an optional per-chunk progress callback for an in-flight fetch. -xlinka
    private readonly struct GatherCallbacks
    {
        public readonly Action<Uri, string> OnGathered;
        public readonly Action<Uri, long, long>? OnProgress; // (uri, totalBytes, receivedBytes)
        public GatherCallbacks(Action<Uri, string> onGathered, Action<Uri, long, long>? onProgress)
        {
            OnGathered = onGathered;
            OnProgress = onProgress;
        }
    }

    // One asset we asked a peer for: who we asked, everyone here waiting on it (none when the host only
    // relays it), and when we last heard anything about it.
    private sealed class PendingFetch
    {
        // A LIST of waiters, not one. Nine meshes of one avatar routinely resolve to the same asset, and
        // dropping the second requester's callback left its gather task awaiting a completion that could
        // never arrive - a permanent hang, not a slow load. Every waiter is answered from the one transfer.
        public readonly List<GatherCallbacks> Waiters = new();
        public readonly IConnection Target;
        public double LastProgress;
        public int Asks = 1;

        public PendingFetch(IConnection target)
        {
            Target = target;
            LastProgress = Now;
        }
    }

    private readonly object _lock = new();
    private readonly Dictionary<JobID, FileTransmitJob> _transmitJobs = new();
    private readonly Dictionary<JobID, FileReceiveJob> _receiveJobs = new();
    private readonly Dictionary<string, PendingFetch> _fetches = new();

    // Pending outbound transfers awaiting an initialize slot. Not a FIFO: RefreshJobs picks the job whose
    // target connection was served least recently, so one peer flooding requests can't starve another. -xlinka
    private readonly List<FileTransmitJob> _transmitJobsToInitialize = new();
    // Last-served ordinal per connection; lower = waited longer (absent = never served = -1). -xlinka
    private readonly Dictionary<IConnection, int> _connectionIndexes = new();
    private int _globalConnectionIndex;

    // uriStr -> the connections waiting for the host to relay that asset to them. The host fetches the
    // bytes from the owning peer once and fans them out to everyone in the list when they arrive. -xlinka
    private readonly Dictionary<string, List<IConnection>> _pendingRelays = new();
    // Relays being fetched from their owners now, and the ones waiting for one of those to finish.
    private readonly HashSet<string> _relaysStarted = new();
    private readonly Queue<string> _relayBacklog = new();

    private int _outboundIdPool;
    private double _nextSweep;

    public Session Session { get; }

    // Live transfer counts for diagnostics (the Network debug tab), guarded by the jobs' own lock.
    public int UploadJobCount { get { lock (_lock) { return _transmitJobs.Count + _transmitJobsToInitialize.Count; } } }
    public int DownloadJobCount { get { lock (_lock) { return _receiveJobs.Count; } } }
    public int PendingAssetRequestCount { get { lock (_lock) { return _fetches.Count; } } }
    public int PendingRelayCount { get { lock (_lock) { return _pendingRelays.Count; } } }

    public readonly struct AssetTransfer
    {
        public readonly Uri Uri;
        public readonly bool IsUpload;
        public readonly long Transferred;
        public readonly long Total;

        public AssetTransfer(Uri uri, bool isUpload, long transferred, long total)
        {
            Uri = uri;
            IsUpload = isUpload;
            Transferred = transferred;
            Total = total;
        }

        public float Fraction => Total > 0 ? (float)Transferred / Total : 0f;
    }

    // Safe to call from the render/UI thread.
    public List<AssetTransfer> GetActiveTransfers()
    {
        var list = new List<AssetTransfer>();
        lock (_lock)
        {
            foreach (var job in _transmitJobs.Values)
                list.Add(new AssetTransfer(job.AssetUri, true, job.SentBytes, job.TotalBytes));
            foreach (var job in _receiveJobs.Values)
                list.Add(new AssetTransfer(job.AssetUri, false, job.ReceivedBytes, job.TotalBytes));
        }
        return list;
    }

    // What this machine is waiting to receive, by address as it was asked for: bytes in so far and the
    // size, or both zero for one asked for whose first chunk has not come. Into the caller's list, so a
    // readout asking a few times a second allocates nothing.
    public void CollectDownloads(List<(string Address, long Received, long Total)> into)
    {
        lock (_lock)
        {
            foreach (var job in _receiveJobs.Values)
                into.Add((UriKey(job.AssetUri), job.ReceivedBytes, job.TotalBytes));
            foreach (var pair in _fetches)
                into.Add((pair.Key, 0, 0));
        }
    }

    // Every transferer of a session this machine is in, newest last. A machine can be in several at once
    // (its own home is hosted too), and a fetch has to go to the session whose people can answer it.
    private static readonly List<SessionAssetTransferer> _live = new();

    // Every machine id seen among this session's people, kept after they leave: an asset whose owner was
    // here and went is waited on, one whose owner never was is not.
    private readonly HashSet<string> _seenMachines = new();

    private void NoteMachines()
    {
        var users = Session?.World?.GetAllUsers();
        if (users == null)
            return;
        lock (_seenMachines)
        {
            foreach (var u in users)
            {
                var id = u.MachineID?.Value;
                if (!string.IsNullOrEmpty(id))
                    _seenMachines.Add(id!);
            }
        }
    }

    private static List<SessionAssetTransferer> Live()
    {
        lock (_live)
            return _live.Where(t => t.Session?.World is { IsDisposed: false } && !t.Session.IsDisposed).ToList();
    }

    // Whether a machine owning this local:// address is in any session this machine is in now.
    public static bool IsOwnerPresent(string uri)
    {
        if (!uri.StartsWith("local://", StringComparison.Ordinal))
            return false;
        var machineId = ExtractMachineId(uri);
        foreach (var t in Live())
        {
            var users = t.Session.World.GetAllUsers();
            if (users != null && users.Any(u => !u.IsLocal && u.MachineID?.Value == machineId))
                return true;
        }
        return false;
    }



    public SessionAssetTransferer(Session session)
    {
        Session = session;
        lock (_live)
            _live.Add(this);
    }

    // The session to ask for a local:// asset this machine does not hold. A single "active" transferer used
    // to answer this, whichever session started last, and it was cleared when that session closed even with
    // others still open: a guest who had joined a world and then opened another asked the wrong host for
    // everything in the first, and one who left the second asked nobody. In order: a session the owning
    // machine is in; a session we joined (its host may hold a copy); the focused world's; the newest. -xlinka
    public static SessionAssetTransferer? ForLocalAsset(string uri)
    {
        var live = Live();
        if (live.Count == 0)
            return null;

        var machineId = uri.StartsWith("local://", StringComparison.Ordinal) ? ExtractMachineId(uri) : null;
        if (!string.IsNullOrEmpty(machineId))
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var world = live[i].Session.World;
                var users = world.GetAllUsers();
                if (users != null && users.Any(u => !u.IsLocal && u.MachineID?.Value == machineId))
                    return live[i];
            }
        }

        SessionAssetTransferer? joined = null, focused = null;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var world = live[i].Session.World;
            if (!world.IsAuthority && live[i].Session.Connections?.HostConnection != null)
            {
                if (world.IsFocused)
                    return live[i];
                joined ??= live[i];
            }
            if (world.IsFocused)
                focused ??= live[i];
        }
        return joined ?? focused ?? live[^1];
    }

    // Request an asset by URI. Sends AssetRequest to the host (clients) or to the asset owner's connection (if
    // we are the authority).
    public void RequestAsset(Uri assetUri, Action<Uri, string> onGathered, Action<Uri, long, long>? onProgress = null)
    {
        lock (_lock)
        {
            var uriStr = UriKey(assetUri);
            if (_fetches.TryGetValue(uriStr, out var waiting))
            {
                // Already in flight: join the queue rather than being forgotten.
                waiting.Waiters.Add(new GatherCallbacks(onGathered, onProgress));
                return;
            }

            IConnection target;
            if (Session.World.IsAuthority)
            {
                // Authority -> request from the machine that owns this local:// asset
                var machineId = ExtractMachineId(assetUri);
                var owner = Session.World.GetAllUsers()
                    ?.FirstOrDefault(u => u.MachineID?.Value == machineId);
                if (owner == null || !Session.Connections.TryGetConnection(owner, out target))
                {
                    LumoraLogger.Warn($"AssetTransferer: no peer for machine '{machineId}', cannot fetch {uriStr}");
                    onGathered(assetUri, null!);
                    return;
                }
            }
            else
            {
                target = Session.Connections.HostConnection;
            }

            if (target == null)
            {
                onGathered(assetUri, null!);
                return;
            }

            var fetch = new PendingFetch(target);
            fetch.Waiters.Add(new GatherCallbacks(onGathered, onProgress));
            _fetches[uriStr] = fetch;
            SendAssetRequest(assetUri, target);
            LumoraLogger.Debug($"AssetTransferer: requested {uriStr}");
        }
    }

    // Send a bare AssetRequest control message to target. Does NOT register a
    // completion callback - callers that want one register in _fetches themselves
    // (a client fetch) or track their own pending list (the host relay). -xlinka
    private void SendAssetRequest(Uri assetUri, IConnection target)
    {
        var msg = new ControlMessage(ControlMessage.Message.AssetRequest);
        msg.Targets.Add(target);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(UriKey(assetUri));
        msg.Payload = ms.ToArray();
        Session.Sync.EnqueueForTransmission(msg);
    }

    // Why an asset could not be had. Sent after the address, where a build that does not read it stops.
    private const byte NotHeld = 0;
    private const byte OwnerGone = 1;

    private void SendNotAvailable(string uriStr, IConnection target, bool ownerGone = false)
    {
        if (target == null)
            return;

        var msg = new ControlMessage(ControlMessage.Message.AssetNotAvailable);
        msg.Targets.Add(target);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(uriStr);
        w.Write(ownerGone ? OwnerGone : NotHeld);
        msg.Payload = ms.ToArray();
        Session.Sync.EnqueueForTransmission(msg);
    }

    // Addresses that just failed because their owner left, not because the owner said no. Read once by
    // whoever was waiting on the fetch, to tell "wait for them to come back" from "it does not exist".
    // Lock-free: it is read from inside another transferer's callback, and taking this one's lock there
    // would order two sessions' locks against each other. -xlinka
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _ownerGone = new();

    // True when this machine just lost a fetch of this address to its owner leaving, and the owner is one
    // this machine saw in the session: worth waiting for. Consumes the note.
    public static bool OwnerLeft(string uri)
    {
        if (!uri.StartsWith("local://", StringComparison.Ordinal))
            return false;
        var machineId = ExtractMachineId(uri);
        double now = Now;
        foreach (var t in Live())
        {
            if (!t._ownerGone.TryRemove(uri, out var at) || now - at >= 120)
                continue;
            t.NoteMachines();
            lock (t._seenMachines)
            {
                if (t._seenMachines.Contains(machineId))
                    return true;
            }
        }
        return false;
    }

    public void ProcessMessage(ControlMessage message)
    {
        lock (_lock)
        {
            // Per-message try/catch so a malformed payload from one peer doesn't take
            // down the asset transfer system for everyone. Bounds-check failures from
            // the decoders surface here as InvalidDataException.
            try
            {
                switch (message.ControlMessageType)
                {
                    case ControlMessage.Message.AssetRequest:       HandleAssetRequest(message);       break;
                    case ControlMessage.Message.AssetTransmissionStart: HandleTransmissionStart(message); break;
                    case ControlMessage.Message.AssetChunk:         HandleChunk(message);              break;
                    case ControlMessage.Message.AssetNextChunkRequest: HandleNextChunkRequest(message); break;
                    case ControlMessage.Message.AssetNotAvailable:  HandleNotAvailable(message);       break;
                }
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"AssetTransferer: dropped malformed {message.ControlMessageType} from {message.Sender?.Identifier}: {ex.Message}");
            }
            RefreshJobs();
        }
    }

    // Runs once per sync cycle. Restarts uploads whose receiver has gone quiet, asks again for downloads
    // nothing has come back for, lets go of receive buffers nobody is filling, and fills free slots.
    public void Update()
    {
        lock (_lock)
        {
            double now = Now;
            if (now < _nextSweep)
                return;
            _nextSweep = now + SweepIntervalSeconds;
            NoteMachines();

            try
            {
                foreach (var job in _transmitJobs.Values)
                {
                    if (job.Ready && job.OutBytes == 0)
                    {
                        job.LastHeard = now;
                        continue;
                    }
                    if (now - job.LastHeard < StallSeconds * System.Math.Min(job.Starts, 4))
                        continue;
                    Restart(job, now, $"no answer for {now - job.LastHeard:0}s");
                }

                // A long queue at the sender asks again for every asset in it at once, so one line per sweep.
                int asked = 0;
                string? firstAsked = null;
                foreach (var (uriStr, fetch) in _fetches)
                {
                    double wait = RequestRetrySeconds * System.Math.Min(fetch.Asks, 4);
                    if (now - fetch.LastProgress < wait || fetch.Target == null || !fetch.Target.IsOpen)
                        continue;
                    fetch.Asks++;
                    fetch.LastProgress = now;
                    asked++;
                    firstAsked ??= $"{uriStr} from {fetch.Target.Identifier} (ask {fetch.Asks})";
                    SendAssetRequest(new Uri(uriStr), fetch.Target);
                }
                if (asked > 0)
                    LumoraLogger.Warn($"AssetTransferer: nothing back yet on {asked} request(s), asked again; first {firstAsked}");

                List<JobID>? idle = null;
                foreach (var (key, job) in _receiveJobs)
                {
                    if (now - job.LastProgress >= ReceiveIdleSeconds)
                        (idle ??= new List<JobID>()).Add(key);
                }
                if (idle != null)
                {
                    foreach (var key in idle)
                    {
                        LumoraLogger.Debug($"AssetTransferer: letting go of an idle receive of {_receiveJobs[key].AssetUri} from {key.Connection?.Identifier}");
                        DropReceive(key);
                    }
                }
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"AssetTransferer: sweep failed: {ex.Message}");
            }

            RefreshJobs();
        }
    }

    // Begin an upload over: the receiver gets a fresh start under the same id, drops whatever it held for
    // it, and pulls from the top. A receiver that never saw the first start, or that lost its job, comes
    // back this way; one that is merely slow answers the first start and never gets here. -xlinka
    private void Restart(FileTransmitJob job, double now, string why)
    {
        job.Starts++;
        job.LastHeard = now;
        var log = job.Starts == 2 ? (Action<string>)LumoraLogger.Warn : LumoraLogger.Debug;
        log($"AssetTransferer: {why} from {job.Target.Identifier} on {UriKey(job.AssetUri)} (job {job.ID}), sending its start again (start {job.Starts})");
        PaceFor(job.Target).Released(job.ClearFlight());
        Session.Sync.EnqueueForTransmission(job.Initialize());
    }

    private void DropTransmit(JobID key)
    {
        if (!_transmitJobs.Remove(key, out var job))
            return;
        if (_paces.TryGetValue(job.Target, out var pace))
        {
            pace.Released(job.ClearFlight());
            int at = pace.Jobs.IndexOf(job);
            if (at >= 0)
            {
                pace.Jobs.RemoveAt(at);
                if (pace.Next > at)
                    pace.Next--;
            }
        }
        job.Dispose();
    }

    private void DropReceive(JobID key)
    {
        if (_receiveJobs.Remove(key, out var job))
            job.Dispose();
    }

    public void ConnectionClosed(IConnection connection)
    {
        lock (_lock)
        {
            foreach (var key in _transmitJobs.Keys.Where(k => k.Connection == connection).ToList())
                DropTransmit(key);
            _paces.Remove(connection);

            foreach (var key in _receiveJobs.Keys.Where(k => k.Connection == connection).ToList())
                DropReceive(key);

            // Everything we were fetching FROM the peer that just dropped fails, waiters and relays alike.
            // Keyed on who we asked rather than on receive jobs, which also catches a request the peer
            // never got round to starting. -xlinka
            // On the authority those are owners, and an owner leaving is not the asset ceasing to exist. A
            // joiner's only target is its host, and losing that is losing the session.
            var asked = _fetches.Where(kv => kv.Value.Target == connection).Select(kv => kv.Key).ToList();
            foreach (var uriStr in asked)
                FailFetch(uriStr, ownerGone: Session.World?.IsAuthority == true);

            // A downstream requester dropped - stop tracking it so we don't relay into a dead connection.
            // If that leaves a relay with nobody waiting, drop the whole entry; a fetch already under way
            // for it still finishes into this machine's store. -xlinka
            var emptiedRelays = new List<string>();
            foreach (var kvp in _pendingRelays)
            {
                kvp.Value.RemoveAll(c => c == connection);
                if (kvp.Value.Count == 0)
                    emptiedRelays.Add(kvp.Key);
            }
            foreach (var uriStr in emptiedRelays)
                _pendingRelays.Remove(uriStr);

            // Drop any pending outbound transfers aimed at the dead peer + forget its served-index. -xlinka
            _transmitJobsToInitialize.RemoveAll(j => j.Target == connection);
            _connectionIndexes.Remove(connection);

            PumpRelayBacklog();
            RefreshJobs();
        }
    }

    private void HandleAssetRequest(ControlMessage message)
    {
        string uriStr;
        using (var ms = new MemoryStream(message.Payload))
        using (var r = new BinaryReader(ms))
            uriStr = r.ReadBoundedString(NetworkLimits.MaxAssetUriBytes);

        var assetUri = new Uri(uriStr);

        // The same peer asking again for what we already have for it: it heard nothing for a while. A job
        // still waiting for a slot stays in line; one already started is started over, since the asker
        // has just said it holds nothing of it. -xlinka
        var existing = FindTransmitJob(message.Sender, uriStr);
        if (existing != null)
        {
            if (_transmitJobs.ContainsKey(new JobID(existing.Target, existing.ID)))
                Restart(existing, Now, "asked again");
            return;
        }

        var localDB = Engine.Current?.LocalDB;
        string localPath = null!;
        if (assetUri.Scheme == "local" && localDB != null)
            localPath = localDB.GetFilePath(uriStr);

        if (localPath != null && File.Exists(localPath))
        {
            var id = _outboundIdPool++;
            _transmitJobsToInitialize.Add(new FileTransmitJob(localPath, assetUri, message.Sender, id));
            LumoraLogger.Debug($"AssetTransferer: queued outbound transfer for {uriStr} (job {id})");
            return;
        }

        // We don't have it ourselves. If we're the authority and this is a peer-owned local:// asset,
        // we can still get it: forward the request to the owning peer and relay the bytes back. That's
        // how an asset one user imported reaches another user who joined later, even when neither of
        // them is us. -xlinka
        var ourMachineId = localDB?.MachineId;
        var assetMachineId = ExtractMachineId(assetUri);
        bool canRelay = Session.World.IsAuthority
            && assetUri.Scheme == "local"
            && !string.IsNullOrEmpty(assetMachineId)
            && assetMachineId != ourMachineId;

        if (canRelay)
        {
            RelayFromOwner(assetUri, message.Sender);
            return;
        }

        if (IsDerived(uriStr))
            LumoraLogger.Debug($"AssetTransferer: no {uriStr} here");
        else
            LumoraLogger.Warn($"AssetTransferer: cannot serve {uriStr} - not found locally");
        SendNotAvailable(uriStr, message.Sender);
    }

    private FileTransmitJob? FindTransmitJob(IConnection target, string uriStr)
    {
        foreach (var job in _transmitJobs.Values)
        {
            if (job.Target == target && UriKey(job.AssetUri) == uriStr)
                return job;
        }
        foreach (var job in _transmitJobsToInitialize)
        {
            if (job.Target == target && UriKey(job.AssetUri) == uriStr)
                return job;
        }
        return null;
    }

    // Host-as-relay: fetch a peer-owned asset from its owner and forward it to requester.
    // Coalesces multiple requesters of the same asset onto a single fetch; the bytes are handed to all of
    // them when they arrive (see ServePendingRelays). -xlinka
    private void RelayFromOwner(Uri assetUri, IConnection requester)
    {
        var uriStr = UriKey(assetUri);

        // Already gathering this exact asset - either another requester's relay, one waiting its turn, or
        // our own in-flight client fetch. Pile this requester on and let the one completion serve everyone.
        // This is also what stops a second request from opening a duplicate receive job. -xlinka
        bool alreadyInFlight = _pendingRelays.ContainsKey(uriStr) || _fetches.ContainsKey(uriStr);
        AddPendingRelay(uriStr, requester);
        if (alreadyInFlight)
            return;

        if (ResolveOwner(uriStr) == null)
        {
            LumoraLogger.Warn($"AssetTransferer: no connected peer owns {uriStr}, cannot relay");
            FailPendingRelays(uriStr, ownerGone: true);
            return;
        }

        if (_relaysStarted.Count >= MaxConcurrentRelays)
        {
            if (_relayBacklog.Count >= MaxQueuedRelays)
            {
                LumoraLogger.Warn($"AssetTransferer: {MaxQueuedRelays} relays already waiting, refusing {uriStr}");
                FailPendingRelays(uriStr);
                return;
            }
            _relayBacklog.Enqueue(uriStr);
            return;
        }

        StartRelay(uriStr);
    }

    private IConnection? ResolveOwner(string uriStr)
    {
        var machineId = ExtractMachineId(uriStr);
        var owner = Session.World.GetAllUsers()?.FirstOrDefault(u => u.MachineID?.Value == machineId);
        if (owner == null || !Session.Connections.TryGetConnection(owner, out var ownerConn))
            return null;
        return ownerConn;
    }

    private void StartRelay(string uriStr)
    {
        var ownerConn = ResolveOwner(uriStr);
        if (ownerConn == null)
        {
            FailPendingRelays(uriStr, ownerGone: true);
            return;
        }
        _relaysStarted.Add(uriStr);
        // Tracked like any request of ours, waiters or not, so a quiet owner is asked again and a
        // departed one fails everyone waiting instead of leaving the relay open for good.
        _fetches[uriStr] = new PendingFetch(ownerConn);
        SendAssetRequest(new Uri(uriStr), ownerConn);
        LumoraLogger.Debug($"AssetTransferer: relaying {uriStr} from {ownerConn.Identifier}");
    }

    // Starts the relays that were waiting, as far as the cap allows. One whose requesters all left, or
    // that something else is already fetching, is passed over.
    private void PumpRelayBacklog()
    {
        while (_relaysStarted.Count < MaxConcurrentRelays && _relayBacklog.Count > 0)
        {
            var uriStr = _relayBacklog.Dequeue();
            if (!_pendingRelays.ContainsKey(uriStr) || _fetches.ContainsKey(uriStr))
                continue;
            StartRelay(uriStr);
        }
    }

    private void AddPendingRelay(string uriStr, IConnection requester)
    {
        if (requester == null)
            return;

        if (_pendingRelays.TryGetValue(uriStr, out var list))
        {
            if (!list.Contains(requester))
                list.Add(requester);
        }
        else
        {
            _pendingRelays[uriStr] = new List<IConnection> { requester };
        }
    }

    private void ServePendingRelays(Uri assetUri, string localPath)
    {
        var uriStr = UriKey(assetUri);
        _relaysStarted.Remove(uriStr);
        if (_pendingRelays.TryGetValue(uriStr, out var requesters))
        {
            _pendingRelays.Remove(uriStr);

            // Each requester gets its own job (its own send cursor); the job reads the file as it sends.
            // -xlinka
            foreach (var requester in requesters)
            {
                if (requester == null || FindTransmitJob(requester, uriStr) != null)
                    continue;
                var id = _outboundIdPool++;
                _transmitJobsToInitialize.Add(new FileTransmitJob(localPath, assetUri, requester, id));
            }
            LumoraLogger.Debug($"AssetTransferer: relayed {uriStr} to {requesters.Count} requester(s)");
        }
        PumpRelayBacklog();
    }

    private void FailPendingRelays(string uriStr, bool ownerGone = false)
    {
        _relaysStarted.Remove(uriStr);
        if (_pendingRelays.TryGetValue(uriStr, out var requesters))
        {
            _pendingRelays.Remove(uriStr);
            foreach (var requester in requesters)
                SendNotAvailable(uriStr, requester, ownerGone);
        }
        PumpRelayBacklog();
    }

    // A fetch that cannot finish: everyone here waiting on it hears so, and so does everyone the host was
    // going to relay it to.
    private void FailFetch(string uriStr, bool ownerGone = false)
    {
        if (ownerGone)
            _ownerGone[uriStr] = Now;
        if (_fetches.Remove(uriStr, out var fetch))
        {
            var uri = new Uri(uriStr);
            foreach (var cb in fetch.Waiters)
                cb.OnGathered(uri, null!);
        }
        foreach (var key in _receiveJobs.Where(kv => UriKey(kv.Value.AssetUri) == uriStr).Select(kv => kv.Key).ToList())
            DropReceive(key);
        FailPendingRelays(uriStr, ownerGone);
    }

    private void NoteProgress(string uriStr, double now)
    {
        if (_fetches.TryGetValue(uriStr, out var fetch))
            fetch.LastProgress = now;
    }

    private void HandleTransmissionStart(ControlMessage message)
    {
        // The temp name is chosen from the format the sender declares, which is only known once the
        // header is read, so the path is produced by callback rather than up front.
        var localDB = Engine.Current?.LocalDB;

        using var ms = new MemoryStream(message.Payload);
        using var r = new BinaryReader(ms);
        var job = new FileReceiveJob(message.Sender, r, format =>
            localDB?.GetTempFilePath(format) ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + format));
        double now = Now;
        job.LastProgress = now;
        // A start for a job we already hold is the sender beginning it over: the new one replaces it.
        var key = new JobID(message.Sender, job.ID);
        DropReceive(key);
        _receiveJobs[key] = job;
        NoteProgress(UriKey(job.AssetUri), now);

        if (job.IsDone)
        {
            // Nothing to pull: an empty file is whole the moment it starts.
            Complete(key, job);
            return;
        }
        Session.Sync.EnqueueForTransmission(job.NextChunkRequest(message.Sender, -1));
        LumoraLogger.Debug($"AssetTransferer: receiving {job.AssetUri} as '{job.Format}' ({job.ID}, {job.TotalBytes} bytes)");
    }

    private void HandleChunk(ControlMessage message)
    {
        using var ms = new MemoryStream(message.Payload);
        using var r = new BinaryReader(ms);

        int id = r.ReadInt32();
        var key = new JobID(message.Sender, id);
        if (!_receiveJobs.TryGetValue(key, out var job))
            return;

        long offset;
        try
        {
            offset = job.ApplyChunk(r);
        }
        catch
        {
            // A chunk that does not fit what its start announced leaves the job with nothing it can trust.
            DropReceive(key);
            throw;
        }

        double now = Now;
        job.LastProgress = now;
        var uriStr = UriKey(job.AssetUri);
        NoteProgress(uriStr, now);

        // Every chunk is acked, the last too: the sender paces on these.
        var ack = job.NextChunkRequest(message.Sender, offset, HeldMicros(message, now));
        if (job.IsDone)
        {
            Complete(key, job);
        }
        else
        {
            // Report progress per chunk so a requester can drive a download bar / detect a stall. -xlinka
            if (_fetches.TryGetValue(uriStr, out var progressFetch))
            {
                foreach (var pcb in progressFetch.Waiters)
                    pcb.OnProgress?.Invoke(job.AssetUri, job.TotalBytes, job.ReceivedBytes);
            }
        }
        Session.Sync.EnqueueForTransmission(ack);
    }

    private void Complete(JobID key, FileReceiveJob job)
    {
        _receiveJobs.Remove(key);
        string path = job.FinalizeAndGetFile();
        var uriStr = UriKey(job.AssetUri);

        // Adopt before the path goes anywhere. The file is already addressed - the hash is in the URI
        // we asked for - so taking that address costs nothing, where re-deriving it means a second
        // full read of a file we are about to decode anyway.
        //
        // It also has to happen here rather than in each consumer. Both of the two below can be live
        // at once (the host wanted this asset AND is relaying it), and whichever adopted first would
        // move the temp file out from under the other, which then reads a path that no longer exists.
        //
        // The side effect is deliberate: a host that only relayed this now holds it, so the next
        // joiner is served from the cache instead of the owner being asked all over again. -xlinka
        var localDB = Engine.Current?.LocalDB;
        string adopted = localDB?.AdoptGatheredAsset(job.AssetUri.OriginalString, path, job.Format) ?? path;
        LumoraLogger.Debug($"AssetTransferer: finished receiving {job.AssetUri} -> {adopted}");

        if (_fetches.Remove(uriStr, out var fetch))
        {
            foreach (var cb in fetch.Waiters)
                cb.OnGathered(job.AssetUri, adopted);
        }
        // Hand the freshly gathered bytes to anyone the host was relaying this asset to. -xlinka
        ServePendingRelays(job.AssetUri, adopted);
    }

    private void HandleNextChunkRequest(ControlMessage message)
    {
        using var ms = new MemoryStream(message.Payload);
        using var r = new BinaryReader(ms);
        int id = r.ReadInt32();
        long? offset = ms.Length - ms.Position >= sizeof(long) ? r.ReadInt64() : null;
        double held = ms.Length - ms.Position >= sizeof(int) ? System.Math.Max(0, r.ReadInt32() / 1e6) : 0;

        var key = new JobID(message.Sender, id);
        if (!_transmitJobs.TryGetValue(key, out var job))
            return;

        double now = Now;
        double heard = message.Arrived > 0 ? message.Arrived / (double)Stopwatch.Frequency : now;
        job.LastHeard = now;
        var pace = PaceFor(job.Target);

        if (!job.Ready)
        {
            // The first ask of this start. A receiver that names chunks says ready with -1, so an ack it
            // sent for the start this one replaced is not taken for it.
            if (offset is long named && named != -1)
                return;
            job.Ready = true;
            job.Lanes = offset == -1;
        }
        else
        {
            var (at, size) = job.Acked(offset);
            if (size > 0)
                pace.Acked(size, heard - at - held, held, now);
        }

        if (job.AllSent && job.Drained)
        {
            DropTransmit(key);
            LumoraLogger.Debug($"AssetTransferer: finished sending job {id}");
        }
        Pump(pace);
    }

    // Sends to one peer while its window has room, a chunk from each job with one to send in turn.
    private void Pump(LinkPace pace)
    {
        var jobs = pace.Jobs;
        int passed = 0;
        while (pace.HasRoom && jobs.Count > 0 && passed < jobs.Count)
        {
            if (pace.Next >= jobs.Count)
                pace.Next = 0;
            var job = jobs[pace.Next++];
            if (!job.Ready || job.AllSent)
            {
                passed++;
                continue;
            }
            passed = 0;

            long offset = job.SentBytes;
            ControlMessage chunk;
            try
            {
                chunk = job.GetChunk();
            }
            catch (Exception ex)
            {
                // The file went away or changed under us; the receiver hears no and asks another way.
                LumoraLogger.Warn($"AssetTransferer: reading {UriKey(job.AssetUri)} for {job.Target?.Identifier} failed: {ex.Message}");
                DropTransmit(new JobID(job.Target!, job.ID));
                SendNotAvailable(UriKey(job.AssetUri), job.Target!);
                continue;
            }
            int size = (int)(job.SentBytes - offset);
            chunk.Bulk = job.Lanes;
            if (job.Lanes || !job.AllSent)
            {
                double now = Now;
                job.Track(offset, now, size);
                pace.Sent(size, now);
            }
            Session.Sync.EnqueueForTransmission(chunk);
            if (job.AllSent && job.Drained)
            {
                DropTransmit(new JobID(job.Target!, job.ID));
                LumoraLogger.Debug($"AssetTransferer: finished sending job {job.ID}");
            }
        }
    }

    private void HandleNotAvailable(ControlMessage message)
    {
        string uriStr;
        bool ownerGone = false;
        using (var ms = new MemoryStream(message.Payload))
        using (var r = new BinaryReader(ms))
        {
            uriStr = r.ReadBoundedString(NetworkLimits.MaxAssetUriBytes);
            if (ms.Position < ms.Length)
                ownerGone = r.ReadByte() == OwnerGone;
        }

        // Only the peer we asked can turn us down. Anyone else saying it would fail a fetch that is going
        // fine, and on the host every relay waiting on it.
        if (!_fetches.TryGetValue(uriStr, out var failed) || (failed.Target != null && failed.Target != message.Sender))
            return;

        if (IsDerived(uriStr))
            LumoraLogger.Debug($"AssetTransferer: {message.Sender?.Identifier} has no {uriStr}");
        else
            LumoraLogger.Warn($"AssetTransferer: peer reported AssetNotAvailable for {uriStr}{(ownerGone ? " (its owner is not in the session)" : "")}");
        FailFetch(uriStr, ownerGone);
    }

    // Pull the owner machine id out of a local://{machineId}/{hash} URI. Read from the ORIGINAL string,
    // not Uri.Host - Uri.Host lowercases the authority, which would mangle the case-sensitive id stamped on
    // User.MachineID and break owner resolution. -xlinka
    private static string ExtractMachineId(Uri assetUri)
    {
        var s = assetUri.OriginalString;
        const string prefix = "local://";
        if (!s.StartsWith(prefix, StringComparison.Ordinal))
            return assetUri.Host;
        return ExtractMachineId(s);
    }

    private static string ExtractMachineId(string s)
    {
        const string prefix = "local://";
        int start = prefix.Length;
        int slash = s.IndexOf('/', start);
        return slash > start ? s.Substring(start, slash - start) : s.Substring(start);
    }

    private void RefreshJobs()
    {
        // Least-recently-served selection among the peers with room: of all pending transfers, start the
        // one whose target connection has waited longest (a never-served connection sorts first). This
        // stops a single peer that floods requests from monopolizing the outbound slots, and the per-peer
        // caps stop a peer that has gone quiet from collecting them. -xlinka
        Dictionary<IConnection, (int Jobs, long Bytes)>? active = null;
        while (_transmitJobs.Count < MaxTransmitJobs && _transmitJobsToInitialize.Count > 0)
        {
            if (active == null)
            {
                active = new Dictionary<IConnection, (int, long)>();
                foreach (var running in _transmitJobs.Values)
                {
                    active.TryGetValue(running.Target, out var load);
                    active[running.Target] = (load.Jobs + 1, load.Bytes + running.Window);
                }
            }

            int index = -1;
            int lowest = int.MaxValue;
            for (int i = 0; i < _transmitJobsToInitialize.Count; i++)
            {
                var candidate = _transmitJobsToInitialize[i];
                if (active.TryGetValue(candidate.Target, out var busy)
                    && (busy.Jobs >= MaxTransmitJobsPerPeer || busy.Bytes + candidate.Window > MaxInFlightBytesPerPeer))
                    continue;
                if (!_connectionIndexes.TryGetValue(candidate.Target, out var served))
                    served = -1;
                if (served < lowest)
                {
                    index = i;
                    lowest = served;
                }
            }

            if (index < 0)
                break;

            var job = _transmitJobsToInitialize[index];
            _transmitJobsToInitialize.RemoveAt(index);

            _connectionIndexes.Remove(job.Target);
            _connectionIndexes[job.Target] = _globalConnectionIndex++;
            if (_globalConnectionIndex == int.MaxValue)
                _globalConnectionIndex = 0;

            if (!job.Load(Engine.Current?.LocalDB))
            {
                SendNotAvailable(UriKey(job.AssetUri), job.Target);
                continue;
            }

            job.LastHeard = Now;
            job.Starts = 1;
            var key = new JobID(job.Target, job.ID);
            _transmitJobs[key] = job;
            PaceFor(job.Target).Jobs.Add(job);
            active.TryGetValue(job.Target, out var had);
            active[job.Target] = (had.Jobs + 1, had.Bytes + job.Window);
            Session.Sync.EnqueueForTransmission(job.Initialize());
            // An empty file is whole at its start and never asked for a chunk; kept, it would only be
            // started over by the stall sweep, again and again.
            if (job.AllSent)
                DropTransmit(key);
        }
    }

    // Leaving a session answers everything still asked of it. Clearing the asks without a word left every
    // gather waiting on them waiting for good: an asset half way over when its world closed never failed,
    // so nothing that wanted it ever fell back. The answers go out after the lock, since a waiter may
    // turn straight round and ask another session. -xlinka
    public void Dispose()
    {
        lock (_live)
            _live.Remove(this);
        List<(Uri Uri, List<GatherCallbacks> Waiters)> unanswered;
        lock (_lock)
        {
            unanswered = _fetches.Select(kv => (new Uri(kv.Key), kv.Value.Waiters.ToList())).ToList();
            foreach (var job in _transmitJobs.Values)
                job.Dispose();
            foreach (var job in _receiveJobs.Values)
                job.Dispose();
            _transmitJobs.Clear();
            _receiveJobs.Clear();
            _fetches.Clear();
            _pendingRelays.Clear();
            _relaysStarted.Clear();
            _relayBacklog.Clear();
            _transmitJobsToInitialize.Clear();
            _connectionIndexes.Clear();
            _paces.Clear();
        }

        foreach (var (uri, waiters) in unanswered)
        {
            foreach (var waiter in waiters)
            {
                try
                {
                    waiter.OnGathered(uri, null!);
                }
                catch (Exception ex)
                {
                    LumoraLogger.Warn($"AssetTransferer: answering a waiter for {uri.OriginalString} on leaving threw: {ex.Message}");
                }
            }
        }
    }
}
