// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lumora.Core.Assets.Interop.Unity;

// The blend shapes of each mesh node in a binary FBX, in the order the file connects them, with the weight
// each one is saved at. A project numbers a renderer's shapes in that order ("m_BlendShapeWeights.Array.
// data[12]"), and the model reader hands them back in an order of its own, so the file is the only place
// the numbering can be read from. Only the object table and the connections are walked; geometry is
// skipped over by its recorded end. -xlinka
public static class UnityFbxShapes
{
    public readonly record struct Shape(string Name, string ShapeName, float Weight);

    // Mesh node name -> its shapes in the project's order. Empty for an ASCII or unreadable file.
    public static Dictionary<string, List<Shape>> Read(string path)
    {
        var result = new Dictionary<string, List<Shape>>(StringComparer.Ordinal);
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            var magic = reader.ReadBytes(21);
            if (magic.Length < 21 || Encoding.ASCII.GetString(magic, 0, 18) != "Kaydara FBX Binary")
                return result;
            reader.ReadBytes(2);
            uint version = reader.ReadUInt32();
            bool wide = version >= 7500;

            var objects = new Dictionary<long, (string Kind, string Name, string Class, float Percent)>();
            var parents = new List<(long Child, long Parent)>();
            while (stream.Position < stream.Length)
            {
                long end = wide ? (long)reader.ReadUInt64() : reader.ReadUInt32();
                if (wide) { reader.ReadUInt64(); reader.ReadUInt64(); } else { reader.ReadUInt32(); reader.ReadUInt32(); }
                int nameLength = reader.ReadByte();
                if (end == 0)
                    break;
                var name = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));
                if (name == "Objects")
                    ReadObjects(reader, end, wide, objects);
                else if (name == "Connections")
                    ReadConnections(reader, end, wide, parents);
                stream.Position = end;
            }

            var children = new Dictionary<long, List<long>>();
            foreach (var (child, parent) in parents)
            {
                if (!children.TryGetValue(parent, out var list))
                    children[parent] = list = new List<long>();
                list.Add(child);
            }
            List<long> Of(long id) => children.TryGetValue(id, out var list) ? list : new List<long>();

            foreach (var (id, model) in objects)
            {
                if (model.Kind != "Model")
                    continue;
                foreach (var geometry in Of(id))
                {
                    if (!objects.TryGetValue(geometry, out var g) || g.Kind != "Geometry" || g.Class != "Mesh")
                        continue;
                    var shapes = new List<Shape>();
                    foreach (var deformer in Of(geometry))
                    {
                        if (!objects.TryGetValue(deformer, out var d) || d.Class != "BlendShape")
                            continue;
                        foreach (var channel in Of(deformer))
                        {
                            if (!objects.TryGetValue(channel, out var c) || c.Class != "BlendShapeChannel")
                                continue;
                            string shapeName = c.Name;
                            foreach (var shape in Of(channel))
                                if (objects.TryGetValue(shape, out var s) && s.Kind == "Geometry")
                                {
                                    shapeName = s.Name;
                                    break;
                                }
                            shapes.Add(new Shape(c.Name, shapeName, c.Percent));
                        }
                    }
                    if (shapes.Count > 0)
                        result.TryAdd(model.Name, shapes);
                }
            }
        }
        catch (Exception)
        {
            result.Clear();
        }
        return result;
    }

    private static void ReadObjects(BinaryReader reader, long end, bool wide, Dictionary<long, (string, string, string, float)> objects)
    {
        var stream = reader.BaseStream;
        while (stream.Position < end)
        {
            long childEnd = wide ? (long)reader.ReadUInt64() : reader.ReadUInt32();
            long count = wide ? (long)reader.ReadUInt64() : reader.ReadUInt32();
            if (wide) reader.ReadUInt64(); else reader.ReadUInt32();
            int nameLength = reader.ReadByte();
            if (childEnd == 0)
                break;
            var kind = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));
            if (kind is "Model" or "Geometry" or "Deformer" && count >= 3)
            {
                long id = Convert.ToInt64(ReadProperty(reader));
                var name = Clean(ReadProperty(reader) as string ?? "");
                var cls = ReadProperty(reader) as string ?? "";
                for (long k = 3; k < count; k++)
                    ReadProperty(reader);
                float percent = 0f;
                if (kind == "Deformer" && cls == "BlendShapeChannel")
                {
                    while (stream.Position < childEnd)
                    {
                        long innerEnd = wide ? (long)reader.ReadUInt64() : reader.ReadUInt32();
                        long innerCount = wide ? (long)reader.ReadUInt64() : reader.ReadUInt32();
                        if (wide) reader.ReadUInt64(); else reader.ReadUInt32();
                        int innerLength = reader.ReadByte();
                        if (innerEnd == 0)
                            break;
                        var field = Encoding.ASCII.GetString(reader.ReadBytes(innerLength));
                        if (field == "DeformPercent" && innerCount > 0)
                            percent = Convert.ToSingle(ReadProperty(reader));
                        stream.Position = innerEnd;
                    }
                }
                objects[id] = (kind, name, cls, percent);
            }
            stream.Position = childEnd;
        }
    }

    private static void ReadConnections(BinaryReader reader, long end, bool wide, List<(long, long)> parents)
    {
        var stream = reader.BaseStream;
        while (stream.Position < end)
        {
            long childEnd = wide ? (long)reader.ReadUInt64() : reader.ReadUInt32();
            long count = wide ? (long)reader.ReadUInt64() : reader.ReadUInt32();
            if (wide) reader.ReadUInt64(); else reader.ReadUInt32();
            int nameLength = reader.ReadByte();
            if (childEnd == 0)
                break;
            var kind = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));
            if (kind == "C" && count >= 3 && ReadProperty(reader) is string type && type == "OO")
                parents.Add((Convert.ToInt64(ReadProperty(reader)), Convert.ToInt64(ReadProperty(reader))));
            stream.Position = childEnd;
        }
    }

    // "Name\0\x01Class" -> "Name"; an exporter's "Class::Name" -> "Name".
    private static string Clean(string raw)
    {
        int zero = raw.IndexOf('\0');
        if (zero >= 0)
            raw = raw[..zero];
        int colons = raw.IndexOf("::", StringComparison.Ordinal);
        return colons >= 0 ? raw[(colons + 2)..] : raw;
    }

    private static object? ReadProperty(BinaryReader reader)
    {
        char type = (char)reader.ReadByte();
        switch (type)
        {
            case 'Y': return reader.ReadInt16();
            case 'C': return reader.ReadByte() != 0;
            case 'I': return reader.ReadInt32();
            case 'F': return reader.ReadSingle();
            case 'D': return reader.ReadDouble();
            case 'L': return reader.ReadInt64();
            case 'S':
            case 'R':
            {
                int length = reader.ReadInt32();
                var bytes = reader.ReadBytes(length);
                return type == 'S' ? Encoding.UTF8.GetString(bytes) : bytes;
            }
            case 'f':
            case 'd':
            case 'l':
            case 'i':
            case 'b':
            {
                reader.ReadUInt32();
                reader.ReadUInt32();
                uint stored = reader.ReadUInt32();
                reader.BaseStream.Position += stored;
                return null;
            }
            default:
                throw new InvalidDataException("Unknown FBX property type " + type);
        }
    }
}
