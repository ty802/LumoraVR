// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Writes unsigned values bit by bit (MSB first), packing them into bytes. Quantized stream encoding
/// uses this so a value quantized to N bits costs ~N bits on the wire, not a whole int. -xlinka
/// </summary>
internal sealed class BitWriter
{
    private readonly List<byte> _bytes = new();
    private byte _current;
    private int _bitInByte;

    public void WriteBits(uint value, int bits)
    {
        for (int i = bits - 1; i >= 0; i--)
        {
            if (((value >> i) & 1u) != 0)
                _current |= (byte)(1 << (7 - _bitInByte));

            if (++_bitInByte == 8)
            {
                _bytes.Add(_current);
                _current = 0;
                _bitInByte = 0;
            }
        }
    }

    /// <summary>Flush any partial byte and return the packed bytes.</summary>
    public byte[] ToArray()
    {
        if (_bitInByte > 0)
        {
            _bytes.Add(_current);
            _current = 0;
            _bitInByte = 0;
        }
        return _bytes.ToArray();
    }
}

/// <summary>
/// Reads unsigned values bit by bit (MSB first) from a byte buffer. Pair of <see cref="BitWriter"/>. -xlinka
/// </summary>
internal sealed class BitReader
{
    private readonly byte[] _bytes;
    private int _bitPos;

    public BitReader(byte[] bytes)
    {
        _bytes = bytes;
    }

    public uint ReadBits(int bits)
    {
        uint value = 0;
        for (int i = 0; i < bits; i++)
        {
            int byteIndex = _bitPos >> 3;
            int bitIndex = 7 - (_bitPos & 7);
            uint bit = byteIndex < _bytes.Length ? (uint)((_bytes[byteIndex] >> bitIndex) & 1) : 0u;
            value = (value << 1) | bit;
            _bitPos++;
        }
        return value;
    }
}

/// <summary>
/// Scalar quantization: map a float in [min, max] to an N-bit unsigned integer and back. The byte cost
/// is implied by the bit count (which is synced), so the wire carries no length. -xlinka
/// </summary>
internal static class Quantization
{
    public static uint Quantize(float value, float min, float max, int bits)
    {
        if (bits <= 0 || max <= min)
            return 0;

        uint maxQ = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
        float t = (value - min) / (max - min);
        if (t < 0f) t = 0f;
        else if (t > 1f) t = 1f;
        return (uint)(t * maxQ + 0.5f);
    }

    public static float Dequantize(uint q, float min, float max, int bits)
    {
        if (bits <= 0)
            return min;

        uint maxQ = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
        if (maxQ == 0)
            return min;

        return min + ((float)q / maxQ) * (max - min);
    }
}
