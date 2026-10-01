// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lumora.Core.Desktop;

// A value with the type it is sent as, for the places D-Bus wants a variant.
internal readonly record struct DBusVariant(string Signature, object Value);

// An object path, so a string and a path are told apart when written.
internal readonly record struct DBusObjectPath(string Value)
{
    public override string ToString() => Value;
}

// D-Bus marshalling, little-endian only: everything this talks to runs on the same machine. Values are
// read into plain objects: numbers and strings as themselves, arrays as lists, dictionaries as
// dictionaries, structs as object arrays, variants as whatever they hold, and fds as indices into the
// message's fd list. -xlinka
internal sealed class DBusWriter
{
    private readonly MemoryStream _buffer = new();

    public int Length => (int)_buffer.Length;

    public byte[] ToArray() => _buffer.ToArray();

    public void Align(int alignment)
    {
        while (_buffer.Length % alignment != 0)
            _buffer.WriteByte(0);
    }

    public void WriteByte(byte value) => _buffer.WriteByte(value);

    public void WriteUInt32(uint value)
    {
        Align(4);
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        _buffer.Write(bytes);
    }

    public void PatchUInt32(int at, uint value)
    {
        long end = _buffer.Position;
        _buffer.Position = at;
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        _buffer.Write(bytes);
        _buffer.Position = end;
    }

    private void WriteRaw(ReadOnlySpan<byte> bytes) => _buffer.Write(bytes);

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32((uint)bytes.Length);
        WriteRaw(bytes);
        WriteByte(0);
    }

    public void WriteSignature(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteByte((byte)bytes.Length);
        WriteRaw(bytes);
        WriteByte(0);
    }

    // Writes value as the single complete type at the head of signature.
    public void Write(string signature, object value)
    {
        int at = 0;
        Write(signature, ref at, value);
    }

    public void WriteAll(string signature, object[] values)
    {
        int at = 0;
        foreach (var value in values)
            Write(signature, ref at, value);
    }

    private void Write(string signature, ref int at, object value)
    {
        char code = signature[at++];
        Span<byte> eight = stackalloc byte[8];
        switch (code)
        {
            case 'y':
                WriteByte(Convert.ToByte(value));
                break;
            case 'b':
                WriteUInt32((bool)value ? 1u : 0u);
                break;
            case 'i':
                Align(4);
                BitConverter.TryWriteBytes(eight, Convert.ToInt32(value));
                WriteRaw(eight[..4]);
                break;
            case 'u':
            case 'h':
                WriteUInt32(Convert.ToUInt32(value));
                break;
            case 'x':
            case 't':
            case 'd':
                Align(8);
                if (code == 'd')
                    BitConverter.TryWriteBytes(eight, Convert.ToDouble(value));
                else if (code == 'x')
                    BitConverter.TryWriteBytes(eight, Convert.ToInt64(value));
                else
                    BitConverter.TryWriteBytes(eight, Convert.ToUInt64(value));
                WriteRaw(eight);
                break;
            case 's':
            case 'o':
                WriteString(value is DBusObjectPath path ? path.Value : (string)value);
                break;
            case 'g':
                WriteSignature((string)value);
                break;
            case 'v':
            {
                var variant = (DBusVariant)value;
                WriteSignature(variant.Signature);
                Write(variant.Signature, variant.Value);
                break;
            }
            case 'a':
            {
                int elementStart = at;
                SkipType(signature, ref at);
                string element = signature[elementStart..at];
                WriteUInt32(0);
                int lengthAt = Length - 4;
                Align(AlignmentOf(element[0]));
                int start = Length;
                if (element[0] == '{')
                {
                    string key = element.Substring(1, 1);
                    string item = element[2..^1];
                    foreach (var pair in (IEnumerable<KeyValuePair<string, DBusVariant>>)value)
                    {
                        Align(8);
                        Write(key, pair.Key);
                        Write(item, item == "v" ? pair.Value : pair.Value.Value);
                    }
                }
                else
                {
                    foreach (var item in (System.Collections.IEnumerable)value)
                        Write(element, item);
                }
                PatchUInt32(lengthAt, (uint)(Length - start));
                break;
            }
            case '(':
            {
                Align(8);
                int fields = at;
                var parts = (object[])value;
                for (int i = 0; i < parts.Length; i++)
                    Write(signature, ref fields, parts[i]);
                at = fields + 1;
                break;
            }
            default:
                throw new NotSupportedException($"D-Bus type '{code}' is not written here");
        }
    }

    public static int AlignmentOf(char code) => code switch
    {
        'y' or 'g' or 'v' => 1,
        'n' or 'q' => 2,
        'x' or 't' or 'd' or '(' or '{' => 8,
        _ => 4,
    };

    // Moves at past one complete type.
    public static void SkipType(string signature, ref int at)
    {
        char code = signature[at++];
        if (code == 'a')
        {
            SkipType(signature, ref at);
            return;
        }
        if (code is '(' or '{')
        {
            char close = code == '(' ? ')' : '}';
            while (signature[at] != close)
                SkipType(signature, ref at);
            at++;
        }
    }
}

internal sealed class DBusReader
{
    private readonly byte[] _data;
    private int _at;

    public DBusReader(byte[] data, int start)
    {
        _data = data;
        _at = start;
    }

    public int Position => _at;

    public void Align(int alignment)
    {
        _at = (_at + alignment - 1) / alignment * alignment;
    }

    public uint ReadUInt32()
    {
        Align(4);
        uint value = BitConverter.ToUInt32(_data, _at);
        _at += 4;
        return value;
    }

    public object[] ReadAll(string signature, int end)
    {
        var values = new List<object>();
        int at = 0;
        while (at < signature.Length && _at <= end)
            values.Add(Read(signature, ref at));
        return values.ToArray();
    }

    public object Read(string signature)
    {
        int at = 0;
        return Read(signature, ref at);
    }

    private object Read(string signature, ref int at)
    {
        char code = signature[at++];
        switch (code)
        {
            case 'y':
                return _data[_at++];
            case 'b':
                return ReadUInt32() != 0;
            case 'n':
                Align(2);
                _at += 2;
                return BitConverter.ToInt16(_data, _at - 2);
            case 'q':
                Align(2);
                _at += 2;
                return BitConverter.ToUInt16(_data, _at - 2);
            case 'i':
                Align(4);
                _at += 4;
                return BitConverter.ToInt32(_data, _at - 4);
            case 'u':
            case 'h':
                return ReadUInt32();
            case 'x':
                Align(8);
                _at += 8;
                return BitConverter.ToInt64(_data, _at - 8);
            case 't':
                Align(8);
                _at += 8;
                return BitConverter.ToUInt64(_data, _at - 8);
            case 'd':
                Align(8);
                _at += 8;
                return BitConverter.ToDouble(_data, _at - 8);
            case 's':
            case 'o':
            {
                int length = (int)ReadUInt32();
                string text = Encoding.UTF8.GetString(_data, _at, length);
                _at += length + 1;
                return text;
            }
            case 'g':
                return ReadSignature();
            case 'v':
            {
                string inner = ReadSignature();
                return Read(inner);
            }
            case 'a':
            {
                int elementStart = at;
                DBusWriter.SkipType(signature, ref at);
                string element = signature[elementStart..at];
                int length = (int)ReadUInt32();
                Align(DBusWriter.AlignmentOf(element[0]));
                int end = _at + length;
                if (element[0] == '{')
                {
                    string key = element.Substring(1, 1);
                    string item = element[2..^1];
                    var map = new Dictionary<object, object>();
                    while (_at < end)
                    {
                        Align(8);
                        var k = Read(key);
                        map[k] = Read(item);
                    }
                    return map;
                }
                var list = new List<object>();
                while (_at < end)
                    list.Add(Read(element));
                return list;
            }
            case '(':
            {
                Align(8);
                var parts = new List<object>();
                while (signature[at] != ')')
                    parts.Add(Read(signature, ref at));
                at++;
                return parts.ToArray();
            }
            default:
                throw new NotSupportedException($"D-Bus type '{code}' is not read here");
        }
    }

    private string ReadSignature()
    {
        int length = _data[_at++];
        string text = Encoding.ASCII.GetString(_data, _at, length);
        _at += length + 1;
        return text;
    }
}
