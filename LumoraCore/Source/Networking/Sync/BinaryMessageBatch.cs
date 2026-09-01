// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core;
using Lumora.Core.Networking;
using Lumora.Nexus.Transport;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Sync;

public abstract class BinaryMessageBatch : SyncMessage
{
    private MemoryStream _stream;
    private BinaryWriter _writer;
    private BinaryReader? _reader;
    private List<DataRecord> _dataRecords = new();
    private int _currentRecordIndex = -1;

    // Encode() output sizing: a RefID plus a 7-bit length per record, and the fixed batch header. Slack on
    // purpose - overshooting the capacity costs nothing, undershooting costs a grow-and-copy.
    private const int RecordHeaderAllowance = 16;
    private const int BatchHeaderAllowance = 48;

    public int DataRecordCount => _dataRecords.Count;

    public override abstract MessageType MessageType { get; }

    public override bool Reliable => true;
    public override bool Background => false;

    // Per-link delta sequence. On a Delta this is the batch's own position in the sender's stream and
    // the receiver applies it only when it is exactly next. On a FULL-WORLD batch it is a tag meaning
    // "this supersedes every delta up to and including N". A PARTIAL full batch - drive corrections, a
    // single-element resync - leaves it at 0 so it never moves the receiver's cursor past deltas that
    // are still in flight for other elements. A Confirmation carries the sequence of the relay the
    // authority deliberately did not echo back to this peer, so the peer's run stays unbroken. -xlinka
    public ulong LinkSequence { get; set; }

    // Used to account for queued backlog by memory instead of by batch count, since batch count says nothing
    // about what a backlog actually costs.
    public int PayloadByteSize => _stream != null ? (int)_stream.Length : 0;

    // Batched state (Delta/Full, and large Confirmations) compresses well and isn't on the
    // tightest latency path; 150 bytes is the floor below which the codec rarely pays off and
    // a single-field delta would just expand. Small confirmations stay under this and ship
    // raw. - xlinka
    public override int CompressionThreshold => 150;

    public BinaryMessageBatch(ulong stateVersion, ulong syncTick, IConnection sender = null!)
        : base(stateVersion, syncTick, sender)
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream);
    }

    // DATA RECORD MANAGEMENT

	public BinaryWriter BeginNewDataRecord(RefID targetID)
	{
		if (_currentRecordIndex >= 0)
			throw new InvalidOperationException("Previous data record not finished!");

        var record = new DataRecord
        {
            TargetID = targetID,
            StartOffset = (int)_stream.Position,
            Validity = MessageValidity.Valid
        };

        _dataRecords.Add(record);
        _currentRecordIndex = _dataRecords.Count - 1;

		// Just track the start - we'll write the header in FinishDataRecord
        return _writer;
    }

	public void FinishDataRecord(RefID targetID)
	{
		if (_currentRecordIndex < 0)
			throw new InvalidOperationException("No data record in progress!");

        var record = _dataRecords[_currentRecordIndex];
        if (record.TargetID != targetID)
            throw new InvalidOperationException("TargetID mismatch!");

        record.EndOffset = (int)_stream.Position;
        record.IsProcessed = false;
        _dataRecords[_currentRecordIndex] = record;
        _currentRecordIndex = -1;
    }

	// Used when encoding fails: removes the record and resets the stream position.
	public void CancelDataRecord()
	{
		if (_currentRecordIndex < 0)
			return; // Nothing to cancel

		var record = _dataRecords[_currentRecordIndex];
		_stream.Position = record.StartOffset; // Reset stream to before this record
		_dataRecords.RemoveAt(_currentRecordIndex);
		_currentRecordIndex = -1;
	}

    public DataRecord GetDataRecord(int index)
    {
        return _dataRecords[index];
    }

	public int FindDataRecordIndex(RefID targetID)
	{
		for (int i = 0; i < _dataRecords.Count; i++)
		{
			if (_dataRecords[i].TargetID == targetID)
				return i;
		}
		return -1;
	}

    public BinaryReader SeekDataRecord(int index)
    {
        if (_reader == null)
        {
            _stream.Position = 0;
            _reader = new BinaryReader(_stream);
        }

        var record = _dataRecords[index];
        _stream.Position = record.StartOffset;

        // Just seek, don't skip anything
        // The stored data doesn't include RefID/length headers - they were parsed during Decode()
        return _reader;
    }

    public void MarkDataRecordAsProcessed(int index)
    {
        var record = _dataRecords[index];
        record.IsProcessed = true;
        _dataRecords[index] = record;
    }

    public bool IsProcessed(int index)
    {
        return _dataRecords[index].IsProcessed;
    }

    public void InvalidateDataRecord(int index, bool conflict)
    {
        var record = _dataRecords[index];
        record.Validity = conflict ? MessageValidity.Conflict : MessageValidity.Invalid;
        _dataRecords[index] = record;
    }

    public void RemoveInvalidRecords()
    {
        _dataRecords.RemoveAll(r => r.Validity != MessageValidity.Valid);
    }

	public void GetConflictingDataRecords(List<RefID> output)
	{
		foreach (var record in _dataRecords)
		{
			if (record.Validity == MessageValidity.Conflict)
				output.Add(record.TargetID);
		}
	}

    // ENCODING/DECODING

    // Encode this batch to bytes for transmission.
    //
    // Header: [type][stateVersion:7bit][syncTick:7bit][senderTime:f64][linkSequence:7bit]
    // [confirmTime:7bit, Confirmation only][recordCount:7bit], then [RefID][len:7bit][data] per record.
    // The link sequence is part of the header for every batch type - a build that does not write it
    // misreads the record count as a sequence and produces garbage, so ProtocolCompatibility's
    // protocol version is bumped alongside this and the join handshake refuses the mismatch outright
    // rather than letting two builds trade unreadable frames. -xlinka
    public override byte[] Encode()
    {
        // Sized up front from the record payload plus a per-record header allowance, so a big batch does
        // not grow-and-copy its way through the output stream; record bytes are then written straight out
        // of this batch's own buffer instead of through a byte[] allocated per record. -xlinka
        int payloadSize = 0;
        for (int i = 0; i < _dataRecords.Count; i++)
        {
            payloadSize += _dataRecords[i].EndOffset - _dataRecords[i].StartOffset;
        }

        using var output = new MemoryStream(payloadSize + _dataRecords.Count * RecordHeaderAllowance + BatchHeaderAllowance);
        using var writer = new BinaryWriter(output);

        writer.Write((byte)MessageType);
        writer.Write7BitEncoded(SenderStateVersion);
        writer.Write7BitEncoded(SenderSyncTick);
        writer.Write(SenderTime);
        writer.Write7BitEncoded(LinkSequence);
        if (this is ConfirmationMessage confirmation)
        {
            writer.Write7BitEncoded(confirmation.ConfirmTime);
        }

        writer.Write7BitEncoded((ulong)_dataRecords.Count);

        // Write each record: [RefID][DataLength][Data]
        if (!_stream.TryGetBuffer(out var source))
            source = new ArraySegment<byte>(_stream.ToArray());

        for (int i = 0; i < _dataRecords.Count; i++)
        {
            var record = _dataRecords[i];
            var dataSize = record.EndOffset - record.StartOffset;

            writer.WriteRefID(record.TargetID);
            writer.Write7BitEncoded((ulong)dataSize);
            writer.Write(source.Array!, source.Offset + record.StartOffset, dataSize);
        }

        return output.ToArray();
    }

    public static BinaryMessageBatch Decode(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var reader = new BinaryReader(input);

        // Mask the compression flag defensively; callers hand us an already-unwrapped frame
        // (high bit cleared), but never let a stray 0x80 corrupt the enum value. - xlinka
        var messageType = (MessageType)(reader.ReadByte() & 0x7F);
        var stateVersion = reader.Read7BitEncoded();
        var syncTick = reader.Read7BitEncoded();
        var senderTime = reader.ReadDouble();
        var linkSequence = reader.Read7BitEncoded();
        var confirmTime = 0UL;
        if (messageType == MessageType.Confirmation)
        {
            confirmTime = reader.Read7BitEncoded();
        }
        var recordCount = (int)reader.Read7BitEncoded();

        BinaryMessageBatch batch = messageType switch
        {
            MessageType.Delta => new DeltaBatch(stateVersion, syncTick),
            MessageType.Full => new FullBatch(stateVersion, syncTick),
            MessageType.Confirmation => new ConfirmationMessage(confirmTime, stateVersion, syncTick),
            _ => throw new InvalidOperationException($"Unknown message type: {messageType}")
        };

        batch.SenderTime = senderTime;
        batch.LinkSequence = linkSequence;

        batch._stream = new MemoryStream();
        batch._writer = new BinaryWriter(batch._stream);

        for (int i = 0; i < recordCount; i++)
        {
            var targetID = reader.ReadRefID();
            var dataLength = (int)reader.Read7BitEncoded();
            var recordData = reader.ReadBytes(dataLength);

            var record = new DataRecord
            {
                TargetID = targetID,
                StartOffset = (int)batch._stream.Position,
                EndOffset = (int)batch._stream.Position + dataLength,
                Validity = MessageValidity.Valid,
                IsProcessed = false
            };

            batch._dataRecords.Add(record);
            batch._stream.Write(recordData, 0, dataLength);
        }

        batch._reader = new BinaryReader(batch._stream);
        return batch;
    }

    protected virtual void ParseDataRecords(int expectedCount)
    {
    }

    public override void Dispose()
    {
        base.Dispose();
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _dataRecords.Clear();
    }
}

public struct DataRecord
{
	public RefID TargetID;
	public int StartOffset;
	public int EndOffset;
	public MessageValidity Validity;
	public bool IsProcessed;
}

public enum MessageType : byte
{
    Delta = 1,
    Full = 2,
    Confirmation = 3,
    Control = 4,
    Stream = 5,
    AsyncStream = 6,
    Ping = 7,
    Disconnect = 8,
    RawFrame = 9
}

