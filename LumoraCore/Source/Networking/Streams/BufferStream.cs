// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Stream that carries a FIFO buffer of primitive values rather than one value per tick. The owner
/// Writes elements; they're sent in batches when enough has accumulated, and receivers drain them with
/// Read. Element bytes go on the wire raw (via a span reinterpret), so it's allocation-light and needs
/// no per-type coder. Sends explicitly (Period 0) once at least MinimumLengthToTransmit is buffered. -xlinka
/// </summary>
/// <typeparam name="T">An unmanaged primitive element type (byte, int, float, ...).</typeparam>
public abstract class BufferStream<T> : Stream where T : unmanaged
{
    /// <summary>Hard safety cap on a single buffer's byte size (10 MiB).</summary>
    public const int MaxBufferBytes = 10 * 1024 * 1024;

    protected readonly Sync<int> _maxBufferSize = new();
    protected readonly Sync<int> _minimumLengthToTransmit = new();
    protected readonly Sync<int> _transmitGranularity = new();

    private readonly Queue<T> _buffer = new();

    /// <summary>Number of elements currently buffered.</summary>
    public int Size => _buffer.Count;

    public override bool HasValidData => _buffer.Count > 0;

    // Buffer streams transmit on demand (when enough is queued), not on a fixed period.
    public override uint Period => 0u;
    public override uint Phase => 0u;

    /// <summary>Maximum buffered bytes before the oldest elements are dropped; 0 = the safety cap.</summary>
    public int MaxBufferSize
    {
        get => _maxBufferSize.Value;
        set { CheckOwnership(); _maxBufferSize.Value = value; }
    }

    /// <summary>How many elements must be buffered before a transmit is triggered.</summary>
    public int MinimumLengthToTransmit
    {
        get => _minimumLengthToTransmit.Value;
        set { CheckOwnership(); _minimumLengthToTransmit.Value = value; }
    }

    /// <summary>Only whole multiples of this many elements are sent per tick (1 = no granularity).</summary>
    public int TransmitGranularity
    {
        get => _transmitGranularity.Value;
        set { CheckOwnership(); _transmitGranularity.Value = value; }
    }

    protected override void OnInit()
    {
        base.OnInit();
        _maxBufferSize.Value = 0; // 0 -> safety cap
        _minimumLengthToTransmit.Value = 1;
        _transmitGranularity.Value = 1;
    }

    /// <summary>Append elements for transmission. Oldest elements are dropped past the capacity.</summary>
    public void Write(T[] data)
    {
        CheckOwnership();
        if (data == null)
            return;

        int cap = MaxElements();
        foreach (var value in data)
        {
            _buffer.Enqueue(value);
            while (_buffer.Count > cap)
                _buffer.Dequeue();
        }
    }

    /// <summary>Drain up to <paramref name="count"/> elements into <paramref name="destination"/>. Returns how many.</summary>
    public int Read(T[] destination, int count)
    {
        int n = System.Math.Min(count, _buffer.Count);
        for (int i = 0; i < n; i++)
            destination[i] = _buffer.Dequeue();
        return n;
    }

    /// <summary>Drop all buffered elements.</summary>
    public void Clear() => _buffer.Clear();

    public override bool IsExplicitUpdatePoint(ulong timePoint)
    {
        return _buffer.Count >= System.Math.Max(1, MinimumLengthToTransmit);
    }

    public override void Encode(BinaryWriter writer)
    {
        int granularity = System.Math.Max(1, TransmitGranularity);
        int count = _buffer.Count - (_buffer.Count % granularity);

        var elements = new T[count];
        for (int i = 0; i < count; i++)
            elements[i] = _buffer.Dequeue();

        // Raw element bytes, platform-endian. Peers share endianness in practice. -xlinka
        var bytes = MemoryMarshal.AsBytes<T>(elements);
        writer.Write7BitEncodedInt(bytes.Length);
        writer.Write(bytes);
    }

    public override void Decode(BinaryReader reader, StreamMessage message)
    {
        int byteCount = reader.Read7BitEncodedInt();
        if (byteCount <= 0 || byteCount > MaxBufferBytes)
            return;

        var bytes = reader.ReadBytes(byteCount);
        var elements = MemoryMarshal.Cast<byte, T>(bytes);

        int cap = MaxElements();
        foreach (var value in elements)
        {
            _buffer.Enqueue(value);
            while (_buffer.Count > cap)
                _buffer.Dequeue();
        }
    }

    private int MaxElements()
    {
        int maxBytes = MaxBufferSize <= 0 ? MaxBufferBytes : System.Math.Min(MaxBufferSize, MaxBufferBytes);
        return System.Math.Max(1, maxBytes / Unsafe.SizeOf<T>());
    }
}

/// <summary>Byte buffer stream (raw data).</summary>
public class ByteBufferStream : BufferStream<byte> { }

/// <summary>Signed byte buffer stream.</summary>
public class SbyteBufferStream : BufferStream<sbyte> { }

/// <summary>Short buffer stream.</summary>
public class ShortBufferStream : BufferStream<short> { }

/// <summary>Unsigned short buffer stream.</summary>
public class UshortBufferStream : BufferStream<ushort> { }

/// <summary>Int buffer stream.</summary>
public class IntBufferStream : BufferStream<int> { }

/// <summary>Unsigned int buffer stream.</summary>
public class UintBufferStream : BufferStream<uint> { }

/// <summary>Long buffer stream.</summary>
public class LongBufferStream : BufferStream<long> { }

/// <summary>Unsigned long buffer stream.</summary>
public class UlongBufferStream : BufferStream<ulong> { }

/// <summary>Float buffer stream (e.g. raw audio samples).</summary>
public class FloatBufferStream : BufferStream<float> { }

/// <summary>Double buffer stream.</summary>
public class DoubleBufferStream : BufferStream<double> { }

/// <summary>Char buffer stream.</summary>
public class CharBufferStream : BufferStream<char> { }

/// <summary>Bool buffer stream.</summary>
public class BoolBufferStream : BufferStream<bool> { }

/// <summary>Decimal buffer stream.</summary>
public class DecimalBufferStream : BufferStream<decimal> { }
