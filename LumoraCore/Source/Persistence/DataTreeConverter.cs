// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Lumora.Core.Persistence;

/// <summary>How a data-tree payload is compressed. The byte is stored in the LDT2 header.</summary>
public enum CompressionCodec : byte
{
    None = 0,
    Brotli = 1,
}

/// <summary>
/// Encodes a <see cref="DataTreeNode"/> tree to bytes and back. A typed binary format is used
/// (rather than JSON/BSON) so exact value types survive the round-trip - JSON would coerce numbers and
/// re-trigger the '@' URL-escaping on strings, and our binary is already tighter than a self-describing
/// dictionary format. The LDT2 header carries a codec byte so the compact binary can be Brotli-compressed
/// (built into .NET, no extra dependency) while staying format-versioned. Legacy uncompressed LDT1 files
/// still load.
/// </summary>
public static class DataTreeConverter
{
    private const uint Magic = 0x4C44_5431;  // "LDT1" - legacy, uncompressed: a node follows the magic.
    private const uint Magic2 = 0x4C44_5432; // "LDT2" - magic + codec byte + (optionally compressed) node.

    /// <summary>Codec used for new saves. Brotli by default; set None to write uncompressed LDT2.</summary>
    public static CompressionCodec DefaultCodec = CompressionCodec.Brotli;

    private const byte NodeValue = 0;
    private const byte NodeList = 1;
    private const byte NodeDictionary = 2;

    private enum ValueCode : byte
    {
        Null, Bool, Byte, SByte, Int16, UInt16, Int32, UInt32,
        Int64, UInt64, Single, Double, Decimal, Char, String, DateTime,
    }

    public static byte[] SaveToBytes(DataTreeNode root, CompressionCodec? codec = null)
    {
        using var stream = new MemoryStream();
        Save(root, stream, codec);
        return stream.ToArray();
    }

    public static void Save(DataTreeNode root, Stream stream, CompressionCodec? codec = null)
    {
        var useCodec = codec ?? DefaultCodec;

        // Header (magic + codec) is always uncompressed; the node payload is compressed per the codec.
        using (var header = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            header.Write(Magic2);
            header.Write((byte)useCodec);
        }

        if (useCodec == CompressionCodec.Brotli)
        {
            using var brotli = new BrotliStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            using var writer = new BinaryWriter(brotli, Encoding.UTF8, leaveOpen: true);
            WriteNode(writer, root);
        }
        else
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteNode(writer, root);
        }
    }

    public static DataTreeNode LoadFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return Load(stream);
    }

    public static DataTreeNode Load(Stream stream)
    {
        // ReadUInt32 + ReadByte consume exactly their bytes (no read-ahead), so the stream is positioned
        // right at the payload afterwards and a decompression stream can wrap the remainder cleanly.
        using var header = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        uint magic = header.ReadUInt32();

        // Legacy uncompressed format: the node tree follows the magic directly.
        if (magic == Magic)
            return ReadNode(header);

        if (magic != Magic2)
            throw new InvalidDataException("Not a Lumora data-tree stream (bad magic).");

        var codec = (CompressionCodec)header.ReadByte();
        if (codec == CompressionCodec.Brotli)
        {
            using var brotli = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new BinaryReader(brotli, Encoding.UTF8, leaveOpen: true);
            return ReadNode(reader);
        }

        if (codec != CompressionCodec.None)
            throw new InvalidDataException($"Unknown data-tree compression codec: {(byte)codec}");

        using var plain = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return ReadNode(plain);
    }

    private static void WriteNode(BinaryWriter writer, DataTreeNode node)
    {
        switch (node)
        {
            case DataTreeValue value:
                writer.Write(NodeValue);
                WriteValue(writer, value.Value);
                break;

            case DataTreeList list:
                writer.Write(NodeList);
                writer.Write7BitEncodedInt(list.Count);
                foreach (var child in list.Children)
                    WriteNode(writer, child);
                break;

            case DataTreeDictionary dictionary:
                writer.Write(NodeDictionary);
                writer.Write7BitEncodedInt(dictionary.Children.Count);
                foreach (var (key, child) in dictionary.Children)
                {
                    writer.Write(key);
                    WriteNode(writer, child);
                }
                break;

            default:
                throw new NotSupportedException($"Unknown data-tree node: {node?.GetType()}");
        }
    }

    private static DataTreeNode ReadNode(BinaryReader reader)
    {
        byte type = reader.ReadByte();
        switch (type)
        {
            case NodeValue:
                return ReadValue(reader);

            case NodeList:
            {
                var list = new DataTreeList();
                int count = reader.Read7BitEncodedInt();
                for (int i = 0; i < count; i++)
                    list.Add(ReadNode(reader));
                return list;
            }

            case NodeDictionary:
            {
                var dictionary = new DataTreeDictionary();
                int count = reader.Read7BitEncodedInt();
                for (int i = 0; i < count; i++)
                {
                    var key = reader.ReadString();
                    dictionary.Add(key, ReadNode(reader));
                }
                return dictionary;
            }

            default:
                throw new InvalidDataException($"Unknown data-tree node type byte: {type}");
        }
    }

    private static void WriteValue(BinaryWriter writer, IConvertible? value)
    {
        switch (value)
        {
            case null: writer.Write((byte)ValueCode.Null); break;
            case bool v: writer.Write((byte)ValueCode.Bool); writer.Write(v); break;
            case byte v: writer.Write((byte)ValueCode.Byte); writer.Write(v); break;
            case sbyte v: writer.Write((byte)ValueCode.SByte); writer.Write(v); break;
            case short v: writer.Write((byte)ValueCode.Int16); writer.Write(v); break;
            case ushort v: writer.Write((byte)ValueCode.UInt16); writer.Write(v); break;
            case int v: writer.Write((byte)ValueCode.Int32); writer.Write(v); break;
            case uint v: writer.Write((byte)ValueCode.UInt32); writer.Write(v); break;
            case long v: writer.Write((byte)ValueCode.Int64); writer.Write(v); break;
            case ulong v: writer.Write((byte)ValueCode.UInt64); writer.Write(v); break;
            case float v: writer.Write((byte)ValueCode.Single); writer.Write(v); break;
            case double v: writer.Write((byte)ValueCode.Double); writer.Write(v); break;
            case decimal v: writer.Write((byte)ValueCode.Decimal); writer.Write(v); break;
            case char v: writer.Write((byte)ValueCode.Char); writer.Write(v); break;
            case string v: writer.Write((byte)ValueCode.String); writer.Write(v); break;
            case DateTime v: writer.Write((byte)ValueCode.DateTime); writer.Write(v.Ticks); break;
            default:
                throw new NotSupportedException($"Cannot encode value of type {value.GetType()}");
        }
    }

    private static DataTreeValue ReadValue(BinaryReader reader)
    {
        var code = (ValueCode)reader.ReadByte();
        return code switch
        {
            ValueCode.Null => new DataTreeValue((IConvertible?)null),
            ValueCode.Bool => new DataTreeValue(reader.ReadBoolean()),
            ValueCode.Byte => new DataTreeValue(reader.ReadByte()),
            ValueCode.SByte => new DataTreeValue(reader.ReadSByte()),
            ValueCode.Int16 => new DataTreeValue(reader.ReadInt16()),
            ValueCode.UInt16 => new DataTreeValue(reader.ReadUInt16()),
            ValueCode.Int32 => new DataTreeValue(reader.ReadInt32()),
            ValueCode.UInt32 => new DataTreeValue(reader.ReadUInt32()),
            ValueCode.Int64 => new DataTreeValue(reader.ReadInt64()),
            ValueCode.UInt64 => new DataTreeValue(reader.ReadUInt64()),
            ValueCode.Single => new DataTreeValue(reader.ReadSingle()),
            ValueCode.Double => new DataTreeValue(reader.ReadDouble()),
            ValueCode.Decimal => new DataTreeValue(reader.ReadDecimal()),
            ValueCode.Char => new DataTreeValue(reader.ReadChar()),
            // RawString so the stored (already-final) string isn't re-'@'-escaped.
            ValueCode.String => DataTreeValue.RawString(reader.ReadString()),
            ValueCode.DateTime => new DataTreeValue((IConvertible)new DateTime(reader.ReadInt64())),
            _ => throw new InvalidDataException($"Unknown value code: {code}"),
        };
    }
}
