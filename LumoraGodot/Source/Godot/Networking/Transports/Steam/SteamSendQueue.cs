// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
#if GODOT_PC

using System.Collections.Generic;
using System.Threading;

namespace Lumora.Godot.Networking.Transports.Steam;

// What one channel could not hand to Steam yet, in order, kept apart from every Steamworks type so it can be
// checked on its own. A full send buffer means the link is behind, not gone: the host streaming a busy world to
// a peer on a thin link filled it in seconds and closed the connection, every few seconds, all evening. Reliable
// data now waits here and goes on as room opens, and nothing reliable on the channel may pass it or the stream
// would arrive out of order. -xlinka
internal sealed class SteamSendQueue
{
    public enum Outcome { Sent, Full, Dead }

    public delegate Outcome Transmit(byte[] data, int offset, int count);

    // A peer this far behind is not catching up; holding more only costs this machine memory.
    public const long MaxBytes = 256L * 1024 * 1024;

    private readonly Queue<byte[]> _pieces = new();
    private long _bytes;

    public long Bytes => Interlocked.Read(ref _bytes);
    public int Count => _pieces.Count;
    public long PeakBytes { get; private set; }

    // One reliable piece, behind whatever already waits. What does not go is copied and kept.
    public Outcome Send(byte[] data, int offset, int count, Transmit transmit)
    {
        if (_pieces.Count > 0)
        {
            var flushed = Flush(transmit);
            if (flushed == Outcome.Dead)
                return Outcome.Dead;
            if (_pieces.Count > 0)
                return Hold(data, offset, count);
        }
        var result = transmit(data, offset, count);
        return result == Outcome.Full ? Hold(data, offset, count) : result;
    }

    // Hands over what waits, oldest first, until Steam has no room.
    public Outcome Flush(Transmit transmit)
    {
        while (_pieces.Count > 0)
        {
            var piece = _pieces.Peek();
            var result = transmit(piece, 0, piece.Length);
            if (result != Outcome.Sent)
                return result;
            _pieces.Dequeue();
            Interlocked.Add(ref _bytes, -piece.Length);
        }
        return Outcome.Sent;
    }

    public void Clear()
    {
        _pieces.Clear();
        Interlocked.Exchange(ref _bytes, 0);
    }

    private Outcome Hold(byte[] data, int offset, int count)
    {
        var copy = new byte[count];
        System.Buffer.BlockCopy(data, offset, copy, 0, count);
        _pieces.Enqueue(copy);
        long now = Interlocked.Add(ref _bytes, count);
        if (now > PeakBytes)
            PeakBytes = now;
        return now > MaxBytes ? Outcome.Dead : Outcome.Full;
    }
}
#endif
