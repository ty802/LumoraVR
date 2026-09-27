// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Lumora.Wasm;

// A cursor over one region of the module bytes. It never reads past its own end, and every length it is
// asked for is checked against what is left before anything is copied or allocated.
internal sealed class WasmReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _data;
    private int _position;
    private readonly int _end;

    public WasmReader(byte[] data, int start, int end)
    {
        _data = data;
        _position = start;
        _end = end;
    }

    public int Position => _position;
    public int End => _end;
    public int Remaining => _end - _position;
    public bool AtEnd => _position >= _end;
    public byte[] Data => _data;

    public static WasmFormatException Malformed(string message) => new(message);

    public byte ReadByte()
    {
        if (_position >= _end)
            throw Malformed("unexpected end");
        return _data[_position++];
    }

    public byte PeekByte()
    {
        if (_position >= _end)
            throw Malformed("unexpected end");
        return _data[_position];
    }

    public void Skip(int count)
    {
        if (count < 0 || count > Remaining)
            throw Malformed("unexpected end");
        _position += count;
    }

    public uint ReadFixedU32()
    {
        if (Remaining < 4)
            throw Malformed("unexpected end");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position, 4));
        _position += 4;
        return value;
    }

    public ulong ReadFixedU64()
    {
        if (Remaining < 8)
            throw Malformed("unexpected end");
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_position, 8));
        _position += 8;
        return value;
    }

    public uint ReadU32()
    {
        uint result = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte b = ReadByte();
            if (shift == 28)
            {
                if ((b & 0x80) != 0)
                    throw Malformed("integer representation too long");
                if ((b & 0x70) != 0)
                    throw Malformed("integer too large");
                return result | ((uint)b << 28);
            }
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
        }
    }

    public int ReadS32()
    {
        int result = 0;
        int shift = 0;
        byte b;
        while (true)
        {
            b = ReadByte();
            if (shift == 28)
            {
                if ((b & 0x80) != 0)
                    throw Malformed("integer representation too long");
                int high = b & 0x70;
                bool negative = (b & 0x08) != 0;
                if (negative ? high != 0x70 : high != 0)
                    throw Malformed("integer too large");
                return result | (b << 28);
            }
            result |= (b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                break;
        }
        if ((b & 0x40) != 0)
            result |= -1 << shift;
        return result;
    }

    public long ReadS64()
    {
        long result = 0;
        int shift = 0;
        byte b;
        while (true)
        {
            b = ReadByte();
            if (shift == 63)
            {
                if ((b & 0x80) != 0)
                    throw Malformed("integer representation too long");
                int payload = b & 0x7F;
                if (payload != 0 && payload != 0x7F)
                    throw Malformed("integer too large");
                return result | ((long)b << 63);
            }
            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                break;
        }
        if ((b & 0x40) != 0)
            result |= -1L << shift;
        return result;
    }

    // Block types are a signed 33-bit number: negative values are the single-byte type forms, and
    // non-negative values are indexes into the type section.
    public long ReadS33()
    {
        long result = 0;
        int shift = 0;
        byte b;
        while (true)
        {
            b = ReadByte();
            if (shift == 28)
            {
                if ((b & 0x80) != 0)
                    throw Malformed("integer representation too long");
                int high = b & 0x60;
                bool negative = (b & 0x10) != 0;
                if (negative ? high != 0x60 : high != 0)
                    throw Malformed("integer too large");
                result |= (long)(b & 0x1F) << 28;
                if (negative)
                    result |= -1L << 33;
                return result;
            }
            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                break;
        }
        if ((b & 0x40) != 0)
            result |= -1L << shift;
        return result;
    }

    public int ReadCount(int max, string what)
    {
        uint count = ReadU32();
        // Every element of every vector takes at least one byte, so a count larger than what is left
        // can only be a lie; failing here keeps a forged count from sizing an allocation.
        if (count > (uint)Remaining)
            throw Malformed("unexpected end");
        if (count > (uint)max)
            throw new WasmValidationException($"too many {what}");
        return (int)count;
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
            throw Malformed("unexpected end of section or function");
        var bytes = new byte[count];
        Buffer.BlockCopy(_data, _position, bytes, 0, count);
        _position += count;
        return bytes;
    }

    public string ReadName()
    {
        uint length = ReadU32();
        if (length > (uint)Remaining)
            throw Malformed("unexpected end");
        try
        {
            string name = StrictUtf8.GetString(_data, _position, (int)length);
            _position += (int)length;
            return name;
        }
        catch (ArgumentException)
        {
            throw Malformed("malformed UTF-8 encoding");
        }
    }

    public WasmValueType ReadValueType()
    {
        byte b = ReadByte();
        return b switch
        {
            0x7F => WasmValueType.I32,
            0x7E => WasmValueType.I64,
            0x7D => WasmValueType.F32,
            0x7C => WasmValueType.F64,
            0x7B => throw new WasmValidationException("SIMD is not supported"),
            0x70 => WasmValueType.FuncRef,
            0x6F => WasmValueType.ExternRef,
            _ => throw Malformed("malformed value type"),
        };
    }

    public WasmValueType ReadReferenceType()
    {
        byte b = ReadByte();
        return b switch
        {
            0x70 => WasmValueType.FuncRef,
            0x6F => WasmValueType.ExternRef,
            _ => throw Malformed("malformed reference type"),
        };
    }

    public WasmSizeRange ReadLimits()
    {
        byte flags = ReadByte();
        switch (flags)
        {
            case 0x00:
                return new WasmSizeRange(ReadU32(), null);
            case 0x01:
            {
                uint min = ReadU32();
                uint max = ReadU32();
                return new WasmSizeRange(min, max);
            }
            default:
                throw Malformed("integer too large");
        }
    }
}
