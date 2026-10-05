// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets.Interop;

// Decoder for the serialized object graph a package carries as its main asset.
//
// Layout is a 9-byte header - the ASCII tag, an int32 version, and one byte naming the codec - wrapped
// around a BSON document. The codec byte is the part worth getting right: the tag is followed by the
// VERSION first, so reading the byte immediately after the tag gives you the version's low byte and a
// confident wrong answer about the compression.
//
// The payload decodes into Lumora's own DataTree nodes, so everything downstream (URL rewriting, the
// loader, the inspector) works on one node model rather than a parallel foreign one. -xlinka
public static class PackageGraphReader
{
    private const string Tag = "FrDT";
    private const int MaxSupportedVersion = 0;

    public enum Codec
    {
        None = 0,
        LZ4 = 1,
        LZMA = 2,
        Brotli = 3,
    }

    public readonly struct Header
    {
        public readonly int Version;
        public readonly Codec Codec;
        public Header(int version, Codec codec) { Version = version; Codec = codec; }
    }

    public static bool TryReadHeader(Stream stream, out Header header)
    {
        header = default;
        Span<byte> buffer = stackalloc byte[9];
        if (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) != buffer.Length)
            return false;
        for (int i = 0; i < Tag.Length; i++)
        {
            if (buffer[i] != (byte)Tag[i])
                return false;
        }
        int version = BinaryPrimitives.ReadInt32LittleEndian(buffer[4..8]);
        header = new Header(version, (Codec)buffer[8]);
        return true;
    }

    public static DataTreeDictionary Read(byte[] asset)
    {
        using var stream = new MemoryStream(asset, writable: false);
        return Read(stream);
    }

    public static DataTreeDictionary Read(Stream stream)
    {
        if (!TryReadHeader(stream, out var header))
            throw new InvalidDataException("Not a package object graph (bad header tag).");
        if (header.Version > MaxSupportedVersion)
            throw new NotSupportedException($"Package graph version {header.Version} is newer than supported ({MaxSupportedVersion}).");

        byte[] document = Decompress(stream, header.Codec);
        int offset = 0;
        var node = ReadDocument(document, ref offset, document.Length, new Decoder());
        return node as DataTreeDictionary
            ?? throw new InvalidDataException("Package object graph root is not a dictionary.");
    }

    private static byte[] Decompress(Stream stream, Codec codec)
    {
        using var output = new MemoryStream();
        switch (codec)
        {
            case Codec.None:
                stream.CopyTo(output);
                break;

            case Codec.Brotli:
                using (var brotli = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true))
                    brotli.CopyTo(output);
                break;

            case Codec.LZ4:
            {
                using var raw = new MemoryStream();
                stream.CopyTo(raw);
                var decoded = PackageLz4.Decode(raw.GetBuffer().AsSpan(0, (int)raw.Length));
                output.Write(decoded, 0, decoded.Length);
                break;
            }

            default:
                // LZMA has not turned up in any package we have seen, and pulling in a decoder for a
                // codec with no observed traffic is not worth the dependency. Say so plainly instead of
                // failing with a corrupt-data error that would send someone hunting the wrong bug.
                throw new NotSupportedException(
                    $"Package graph uses the {codec} codec, which this importer does not decode.");
        }
        return output.ToArray();
    }

    // ---- BSON ----
    //
    // Preserve the primitive types used by package graphs, including BSON UTC dates. Unsupported
    // types still fail explicitly: silently skipping a value could corrupt component data or links.

    private const byte TypeDouble = 0x01;
    private const byte TypeString = 0x02;
    private const byte TypeDocument = 0x03;
    private const byte TypeArray = 0x04;
    private const byte TypeBool = 0x08;
    private const byte TypeDateTime = 0x09;
    private const byte TypeNull = 0x0A;
    private const byte TypeInt32 = 0x10;
    private const byte TypeUInt64 = 0x11;
    private const byte TypeInt64 = 0x12;

    // What one decode shares across every document in it. A package graph repeats the same few keys
    // millions of times and most of its documents are arrays, so each distinct key becomes a string once,
    // array positions are checked straight from the bytes without one, and the lists a document collects
    // its elements in are reused: a graph of 196612 slots took 10 s to decode and left the collector
    // pausing the world for seconds afterwards. -xlinka
    private sealed class Decoder
    {
        private readonly Dictionary<ulong, string> _keys = new();
        private readonly Stack<List<(string? Key, DataTreeNode Node)>> _lists = new();

        public List<(string? Key, DataTreeNode Node)> RentList() => _lists.Count > 0 ? _lists.Pop() : new();

        public void ReturnList(List<(string? Key, DataTreeNode Node)> list)
        {
            list.Clear();
            _lists.Push(list);
        }

        public string Key(byte[] b, int start, int length)
        {
            if (length > 64)
                return Encoding.UTF8.GetString(b, start, length);
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < length; i++)
                hash = (hash ^ b[start + i]) * 1099511628211UL;
            hash ^= (ulong)length << 56;
            if (_keys.TryGetValue(hash, out var known) && Spells(known, b, start, length))
                return known;
            var made = Encoding.UTF8.GetString(b, start, length);
            _keys[hash] = made;
            return made;
        }

        // Byte for char, so only an ASCII key can match; anything else is simply made again.
        private static bool Spells(string key, byte[] b, int start, int length)
        {
            if (key.Length != length)
                return false;
            for (int i = 0; i < length; i++)
            {
                if (key[i] != b[start + i])
                    return false;
            }
            return true;
        }
    }

    private static DataTreeNode ReadDocument(byte[] b, ref int offset, int limit, Decoder decoder)
    {
        int start = offset;
        RequireBytes(offset, 4, limit);
        int size = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
        if (size < 5 || size > limit - start)
            throw new InvalidDataException($"Invalid BSON document length {size} at {start}.");
        int end = start + size;
        offset += 4;

        // A BSON array is a document whose keys are "0", "1", "2"... Decide which node we are building
        // from the keys rather than from the parent's element type, so a nested array read through the
        // document path still comes out as a list. Keys that are just positions are left unmade; a
        // document that turns out not to be an array gets them back from their position.
        var pairs = decoder.RentList();
        bool looksLikeArray = true;
        int index = 0;

        while (offset < end - 1)
        {
            byte type = b[offset++];
            int keyStart = offset;
            int keyEnd = keyStart;
            while (keyEnd < end - 1 && b[keyEnd] != 0x00)
                keyEnd++;
            if (keyEnd >= end - 1)
                throw new InvalidDataException($"Unterminated BSON key at {keyStart}.");
            offset = keyEnd + 1;

            if (looksLikeArray && !IsPosition(b, keyStart, keyEnd - keyStart, index))
                looksLikeArray = false;
            string? key = looksLikeArray ? null : decoder.Key(b, keyStart, keyEnd - keyStart);
            DataTreeNode value = ReadElement(b, ref offset, type, end - 1, decoder);
            index++;
            pairs.Add((key, value));
        }

        if (offset != end - 1 || b[offset] != 0x00)
            throw new InvalidDataException($"BSON document at {start} is missing its terminator.");
        offset = end;

        DataTreeNode result;
        if (looksLikeArray && pairs.Count > 0)
        {
            var list = new DataTreeList();
            list.Children.Capacity = pairs.Count;
            foreach (var (_, node) in pairs)
                list.Add(node);
            result = list;
        }
        else
        {
            var dict = new DataTreeDictionary();
            dict.Children.EnsureCapacity(pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                var (key, node) = pairs[i];
                dict.Children[key ?? i.ToString(CultureInfo.InvariantCulture)] = node;
            }
            result = dict;
        }
        decoder.ReturnList(pairs);
        return result;
    }

    private static bool IsPosition(byte[] b, int start, int length, int index)
    {
        Span<byte> digits = stackalloc byte[11];
        return index.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture)
            && written == length && digits[..written].SequenceEqual(b.AsSpan(start, length));
    }

    private static DataTreeNode ReadElement(byte[] b, ref int offset, byte type, int limit, Decoder decoder)
    {
        switch (type)
        {
            case TypeDouble:
            {
                RequireBytes(offset, 8, limit);
                double v = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(offset, 8)));
                offset += 8;
                return new DataTreeValue(v);
            }
            case TypeString:
            {
                RequireBytes(offset, 4, limit);
                int len = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
                offset += 4;
                if (len < 1 || len > limit - offset)
                    throw new InvalidDataException($"BSON string length {len} at {offset} runs past the buffer.");
                if (b[offset + len - 1] != 0)
                    throw new InvalidDataException($"BSON string at {offset} is missing its terminator.");
                string s = Encoding.UTF8.GetString(b, offset, len - 1);
                offset += len;
                // RawString keeps the leading '@' that marks a URL intact; the normal string constructor
                // would treat it as something to escape and the URL rewrite pass would then miss it.
                return DataTreeValue.RawString(s);
            }
            case TypeDocument:
            case TypeArray:
                return ReadDocument(b, ref offset, limit, decoder);
            case TypeBool:
            {
                RequireBytes(offset, 1, limit);
                bool v = b[offset] != 0;
                offset += 1;
                return new DataTreeValue(v);
            }
            case TypeDateTime:
            {
                RequireBytes(offset, 8, limit);
                long milliseconds = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(offset, 8));
                offset += 8;
                try
                {
                    // BSON dates are signed milliseconds since the Unix epoch, not .NET ticks.
                    return new DataTreeValue(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new InvalidDataException($"BSON UTC date {milliseconds} at {offset - 8} is outside the supported DateTime range.", ex);
                }
            }
            case TypeNull:
                return new DataTreeValue((IConvertible?)null);
            case TypeInt32:
            {
                RequireBytes(offset, 4, limit);
                int v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
                offset += 4;
                return new DataTreeValue(v);
            }
            case TypeInt64:
            {
                RequireBytes(offset, 8, limit);
                long v = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(offset, 8));
                offset += 8;
                return new DataTreeValue(v);
            }
            case TypeUInt64:
            {
                RequireBytes(offset, 8, limit);
                ulong v = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(offset, 8));
                offset += 8;
                return new DataTreeValue(v);
            }
            default:
                throw new InvalidDataException(
                    $"Unsupported BSON element type 0x{type:X2} at offset {offset}.");
        }
    }

    private static void RequireBytes(int offset, int count, int limit)
    {
        if (offset < 0 || count > limit - offset)
            throw new InvalidDataException($"Truncated BSON value at offset {offset}: expected {count} bytes before {limit}.");
    }
}
