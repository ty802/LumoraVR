// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Lumora.Core.Assets.Interop;

// LZMA the way a package wraps it:
//
//   5 bytes   properties   lc/lp/pb packed into one byte, then the dictionary size
//   int64     decodedLength
//   int64     encodedLength
//   bytes     one raw LZMA stream, end marker optional
//
// The decoder is the published LZMA algorithm (public domain) written out from its specification: a
// binary range coder, adaptive 11-bit probabilities, and the literal/match/rep state machine. The
// decoded length is known before the first byte, so the output buffer IS the dictionary and there is
// no window to wrap. Every distance is checked against what has been written, so a corrupt stream
// fails with a reason instead of reading outside the buffer. -xlinka
public static class PackageLzma
{
    public const int HeaderSize = 21;

    private const int BitModelBits = 11;
    private const uint BitModelTotal = 1u << BitModelBits;
    private const int MoveBits = 5;
    private const uint TopValue = 1u << 24;
    private const ushort ProbabilityStart = (ushort)(BitModelTotal / 2);

    private const int States = 12;
    private const int PosBitsMax = 4;
    private const int PosStatesMax = 1 << PosBitsMax;
    private const int MatchMinLen = 2;
    private const int LenToPosStates = 4;
    private const int AlignBits = 4;
    private const int EndPosModelIndex = 14;
    private const int FullDistances = 1 << (EndPosModelIndex >> 1);
    private const int PosSlotBits = 6;

    public static bool TryReadHeader(ReadOnlySpan<byte> source, out long decodedLength, out long encodedLength)
    {
        decodedLength = 0;
        encodedLength = 0;
        if (source.Length < HeaderSize)
            return false;
        decodedLength = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(5));
        encodedLength = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(13));
        return true;
    }

    public static byte[] Decode(ReadOnlySpan<byte> source) => Decode(source, out _);

    // consumed counts the LZMA stream bytes the range coder read, not the header.
    public static byte[] Decode(ReadOnlySpan<byte> source, out int consumed)
    {
        if (!TryReadHeader(source, out long decodedLength, out long encodedLength))
            throw new InvalidDataException("LZMA header is truncated.");
        if (decodedLength < 0 || decodedLength > Array.MaxLength)
            throw new InvalidDataException($"LZMA header declares an implausible length ({decodedLength}).");

        var stream = source.Slice(HeaderSize);
        if (encodedLength >= 0)
        {
            if (encodedLength > stream.Length)
                throw new InvalidDataException($"LZMA stream is {stream.Length} bytes, header says {encodedLength}.");
            stream = stream.Slice(0, (int)encodedLength);
        }

        var output = new byte[decodedLength];
        consumed = DecodeRaw(source.Slice(0, 5), stream, output);
        return output;
    }

    // Fills output exactly. Returns the stream bytes read.
    public static int DecodeRaw(ReadOnlySpan<byte> properties, ReadOnlySpan<byte> stream, Span<byte> output)
    {
        if (properties.Length < 5)
            throw new InvalidDataException("LZMA properties are truncated.");
        int packed = properties[0];
        if (packed >= 9 * 5 * 5)
            throw new InvalidDataException($"LZMA properties byte {packed} is out of range.");
        int lc = packed % 9;
        packed /= 9;
        int lp = packed % 5;
        int pb = packed / 5;

        var literal = Probabilities(0x300 << (lc + lp));
        var isMatch = Probabilities(States << PosBitsMax);
        var isRep = Probabilities(States);
        var isRepG0 = Probabilities(States);
        var isRepG1 = Probabilities(States);
        var isRepG2 = Probabilities(States);
        var isRep0Long = Probabilities(States << PosBitsMax);
        var posSlot = Probabilities(LenToPosStates << PosSlotBits);
        var posSpecial = Probabilities(1 + FullDistances - EndPosModelIndex);
        var align = Probabilities(1 << AlignBits);
        var lengths = new LengthDecoder();
        var repLengths = new LengthDecoder();

        var rc = new RangeDecoder(stream);
        int pbMask = (1 << pb) - 1;
        int lpMask = (1 << lp) - 1;
        int end = output.Length;
        int pos = 0;
        int state = 0;
        uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;

        while (pos < end)
        {
            int posState = pos & pbMask;

            if (rc.DecodeBit(ref isMatch[(state << PosBitsMax) + posState]) == 0)
            {
                int previous = pos > 0 ? output[pos - 1] : 0;
                int probs = 0x300 * (((pos & lpMask) << lc) + (previous >> (8 - lc)));
                int symbol = 1;
                if (state >= 7)
                {
                    int matchByte = output[pos - (int)rep0 - 1];
                    do
                    {
                        int matchBit = (matchByte >> 7) & 1;
                        matchByte <<= 1;
                        int bit = rc.DecodeBit(ref literal[probs + ((1 + matchBit) << 8) + symbol]);
                        symbol = (symbol << 1) | bit;
                        if (matchBit != bit)
                            break;
                    }
                    while (symbol < 0x100);
                }
                while (symbol < 0x100)
                    symbol = (symbol << 1) | rc.DecodeBit(ref literal[probs + symbol]);

                output[pos++] = (byte)symbol;
                state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                continue;
            }

            int length;
            if (rc.DecodeBit(ref isRep[state]) != 0)
            {
                if (pos == 0)
                    throw new InvalidDataException("LZMA stream repeats a match before writing anything.");

                if (rc.DecodeBit(ref isRepG0[state]) == 0)
                {
                    if (rc.DecodeBit(ref isRep0Long[(state << PosBitsMax) + posState]) == 0)
                    {
                        state = state < 7 ? 9 : 11;
                        output[pos] = output[pos - (int)rep0 - 1];
                        pos++;
                        continue;
                    }
                }
                else
                {
                    uint distance;
                    if (rc.DecodeBit(ref isRepG1[state]) == 0)
                    {
                        distance = rep1;
                    }
                    else
                    {
                        if (rc.DecodeBit(ref isRepG2[state]) == 0)
                        {
                            distance = rep2;
                        }
                        else
                        {
                            distance = rep3;
                            rep3 = rep2;
                        }
                        rep2 = rep1;
                    }
                    rep1 = rep0;
                    rep0 = distance;
                }

                length = repLengths.Decode(ref rc, posState);
                state = state < 7 ? 8 : 11;
            }
            else
            {
                rep3 = rep2;
                rep2 = rep1;
                rep1 = rep0;
                length = lengths.Decode(ref rc, posState);
                state = state < 7 ? 7 : 10;
                rep0 = DecodeDistance(ref rc, length, posSlot, posSpecial, align);
                if (rep0 == 0xFFFFFFFFu)
                    break;
                if (rep0 >= (uint)pos)
                    throw new InvalidDataException($"LZMA match at {pos} reaches back {rep0 + 1} bytes.");
            }

            length += MatchMinLen;
            if (length > end - pos)
                throw new InvalidDataException($"LZMA match at {pos} runs {length - (end - pos)} bytes past the declared length.");

            int from = pos - (int)rep0 - 1;
            for (int i = 0; i < length; i++)
                output[pos + i] = output[from + i];
            pos += length;
        }

        if (pos != end)
            throw new InvalidDataException($"LZMA stream ended after {pos} of {end} bytes.");
        return rc.Position;
    }

    private static ushort[] Probabilities(int count)
    {
        var probs = new ushort[count];
        Array.Fill(probs, ProbabilityStart);
        return probs;
    }

    private static uint DecodeDistance(ref RangeDecoder rc, int length, ushort[] posSlot, ushort[] posSpecial, ushort[] align)
    {
        int lenState = length < LenToPosStates - 1 ? length : LenToPosStates - 1;
        int slot = BitTree(ref rc, posSlot, lenState << PosSlotBits, PosSlotBits);
        if (slot < 4)
            return (uint)slot;

        int directBits = (slot >> 1) - 1;
        uint distance = (uint)(2 | (slot & 1)) << directBits;
        if (slot < EndPosModelIndex)
            return distance + (uint)ReverseBitTree(ref rc, posSpecial, (int)distance - slot, directBits);

        distance += rc.DecodeDirectBits(directBits - AlignBits) << AlignBits;
        return distance + (uint)ReverseBitTree(ref rc, align, 0, AlignBits);
    }

    private static int BitTree(ref RangeDecoder rc, ushort[] probs, int offset, int bits)
    {
        int m = 1;
        for (int i = 0; i < bits; i++)
            m = (m << 1) + rc.DecodeBit(ref probs[offset + m]);
        return m - (1 << bits);
    }

    private static int ReverseBitTree(ref RangeDecoder rc, ushort[] probs, int offset, int bits)
    {
        int m = 1;
        int symbol = 0;
        for (int i = 0; i < bits; i++)
        {
            int bit = rc.DecodeBit(ref probs[offset + m]);
            m = (m << 1) + bit;
            symbol |= bit << i;
        }
        return symbol;
    }

    private sealed class LengthDecoder
    {
        private readonly ushort[] _choice = Probabilities(2);
        private readonly ushort[] _low = Probabilities(PosStatesMax << 3);
        private readonly ushort[] _mid = Probabilities(PosStatesMax << 3);
        private readonly ushort[] _high = Probabilities(1 << 8);

        public int Decode(ref RangeDecoder rc, int posState)
        {
            if (rc.DecodeBit(ref _choice[0]) == 0)
                return BitTree(ref rc, _low, posState << 3, 3);
            if (rc.DecodeBit(ref _choice[1]) == 0)
                return 8 + BitTree(ref rc, _mid, posState << 3, 3);
            return 16 + BitTree(ref rc, _high, 0, 8);
        }
    }

    private ref struct RangeDecoder
    {
        private readonly ReadOnlySpan<byte> _input;
        private int _position;
        private uint _range;
        private uint _code;

        public RangeDecoder(ReadOnlySpan<byte> input)
        {
            if (input.Length < 5 || input[0] != 0)
                throw new InvalidDataException("LZMA stream does not open with a range coder header.");
            _input = input;
            _range = 0xFFFFFFFFu;
            _code = 0;
            for (int i = 1; i < 5; i++)
                _code = (_code << 8) | input[i];
            _position = 5;
            if (_code == _range)
                throw new InvalidDataException("LZMA range coder starts in an impossible state.");
        }

        public readonly int Position => _position;

        public int DecodeBit(ref ushort probability)
        {
            uint p = probability;
            uint bound = (_range >> BitModelBits) * p;
            int bit;
            if (_code < bound)
            {
                probability = (ushort)(p + ((BitModelTotal - p) >> MoveBits));
                _range = bound;
                bit = 0;
            }
            else
            {
                probability = (ushort)(p - (p >> MoveBits));
                _code -= bound;
                _range -= bound;
                bit = 1;
            }
            Normalize();
            return bit;
        }

        public uint DecodeDirectBits(int count)
        {
            uint result = 0;
            for (; count > 0; count--)
            {
                _range >>= 1;
                _code -= _range;
                uint mask = 0u - (_code >> 31);
                _code += _range & mask;
                Normalize();
                result = (result << 1) + (mask + 1);
            }
            return result;
        }

        private void Normalize()
        {
            if (_range >= TopValue)
                return;
            if (_position >= _input.Length)
                throw new InvalidDataException("LZMA stream ends early.");
            _range <<= 8;
            _code = (_code << 8) | _input[_position++];
        }
    }
}
