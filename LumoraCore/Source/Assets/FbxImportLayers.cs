// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Lumora.Core.Assets;

// Assimp copies every FBX vertex channel into every blend shape before post-processing. Extra
// authoring color layers can therefore cost gigabytes even though Phos only reads color channel 0.
// Hide only references to channels Phos cannot store, keeping all four supported UV channels.
// Same-length node renames preserve binary offsets and leave the original file and input untouched.
internal static class FbxImportLayers
{
    private readonly record struct Node(int End, int Properties, int Children, int Name, int NameLength, ulong Count);

    internal static byte[] Prepare(byte[] source)
    {
        if (source.Length < 27 || !source.AsSpan(0, 23).SequenceEqual("Kaydara FBX Binary  \0\x1a\0"u8))
            return source;

        bool wide = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(23)) >= 7500;
        var edits = new List<int>();
        foreach (var root in Children(27, source.Length))
        {
            if (!Named(root, "Objects"u8)) continue;
            foreach (var geometry in Children(root.Children, root.End))
            {
                if (!Named(geometry, "Geometry"u8)) continue;
                foreach (var layer in Children(geometry.Children, geometry.End))
                {
                    if (!Named(layer, "Layer"u8)) continue;
                    foreach (var reference in Children(layer.Children, layer.End))
                    {
                        if (!Named(reference, "LayerElement"u8)) continue;
                        int limit = int.MaxValue;
                        int index = -1;
                        foreach (var field in Children(reference.Children, reference.End))
                        {
                            if (Named(field, "Type"u8) && field.Count == 1)
                            {
                                var value = StringProperty(field);
                                if (value.SequenceEqual("LayerElementColor"u8)) limit = 1;
                                else if (value.SequenceEqual("LayerElementUV"u8)) limit = 4;
                            }
                            else if (Named(field, "TypedIndex"u8) && field.Count == 1
                                && field.Children - field.Properties == 5 && source[field.Properties] == (byte)'I')
                                index = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(field.Properties + 1));
                        }
                        if (limit != int.MaxValue && index >= limit)
                            edits.Add(reference.Name);
                    }
                }
            }
        }

        if (edits.Count == 0) return source;
        var filtered = (byte[])source.Clone();
        foreach (int offset in edits) filtered[offset] = (byte)'_';
        return filtered;

        bool Named(Node node, ReadOnlySpan<byte> name)
            => source.AsSpan(node.Name, node.NameLength).SequenceEqual(name);

        ReadOnlySpan<byte> StringProperty(Node node)
        {
            int size = node.Children - node.Properties;
            if (size < 5 || source[node.Properties] != (byte)'S') return default;
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(node.Properties + 1));
            if (length != size - 5) throw new InvalidDataException("Invalid FBX string property length.");
            return source.AsSpan(node.Properties + 5, (int)length);
        }

        IEnumerable<Node> Children(int start, int end)
        {
            int header = wide ? 25 : 13;
            while (start < end)
            {
                if (end - start < header) throw new InvalidDataException("Truncated FBX node header.");
                ulong nodeEnd = Number(start);
                ulong count = Number(start + (wide ? 8 : 4));
                ulong propertyLength = Number(start + (wide ? 16 : 8));
                int nameLength = source[start + header - 1];
                if (nodeEnd == 0)
                {
                    if (count != 0 || propertyLength != 0 || nameLength != 0)
                        throw new InvalidDataException("Invalid FBX end marker.");
                    yield break;
                }
                int name = start + header;
                long properties = (long)name + nameLength;
                if (nodeEnd > (ulong)end || nodeEnd < (ulong)properties
                    || propertyLength > nodeEnd - (ulong)properties)
                    throw new InvalidDataException("Invalid FBX node bounds.");
                yield return new Node((int)nodeEnd, (int)properties, (int)(properties + (long)propertyLength), name, nameLength, count);
                start = (int)nodeEnd;
            }
        }

        ulong Number(int offset) => wide
            ? BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset));
    }
}
