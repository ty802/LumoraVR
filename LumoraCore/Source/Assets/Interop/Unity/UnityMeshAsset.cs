// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Assets.Interop.Unity;

// A mesh saved as a text asset: vertex streams and an index buffer written out as hex. Each vertex channel
// says which stream it lives in, its offset in a vertex, its format and how many components it has; a
// stream's vertices are interleaved, and each stream starts on a 16-byte boundary after the one before. The
// newer layout has fourteen channels (position, normal, tangent, colour, eight UV sets, skin weights and
// indices) and the older eight (position, normal, colour, four UV sets, tangent), each with its own format
// numbering. Positions, normals, tangents, colours and the first two UV sets come across; skinning and
// shapes do not. A binary asset (or a compressed mesh) is not read. -xlinka
internal static class UnityMeshAsset
{
    private const int MeshClass = 43;

    private readonly record struct Channel(int Stream, int Offset, int Format, int Dimension);

    public static PhosMesh? Read(string? text, long fileId)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith("%YAML", StringComparison.Ordinal))
            return null;
        UnityDocument? doc = null;
        foreach (var d in UnityYaml.Parse(text))
        {
            if (d.ClassId != MeshClass)
                continue;
            if (doc == null || d.FileId == fileId)
                doc = d;
        }
        if (doc == null || (doc["m_MeshCompression"]?.Int() ?? 0) != 0)
            return null;
        var vertexData = doc["m_VertexData"];
        int count = vertexData?["m_VertexCount"]?.Int() ?? 0;
        var bytes = Hex(vertexData?["_typelessdata"]?.Text);
        var indexBytes = Hex(doc["m_IndexBuffer"]?.Text);
        if (vertexData == null || count <= 0 || bytes == null || indexBytes == null)
            return null;

        var channels = new List<Channel>();
        if (vertexData["m_Channels"] is UnitySeq list)
            foreach (var item in list.Items)
                channels.Add(new Channel(item["stream"]?.Int() ?? 0, item["offset"]?.Int() ?? 0, item["format"]?.Int() ?? 0, (item["dimension"]?.Int() ?? 0) & 0xF));
        bool newer = channels.Count >= 14;
        if (channels.Count < 8)
            return null;

        // Stream strides and starts.
        var strides = new int[4];
        foreach (var c in channels)
            if (c.Dimension > 0 && c.Stream is >= 0 and < 4)
                strides[c.Stream] = System.Math.Max(strides[c.Stream], c.Offset + Size(c.Format, newer) * c.Dimension);
        var starts = new int[4];
        int at = 0;
        for (int s = 0; s < 4; s++)
        {
            starts[s] = at;
            if (strides[s] > 0)
                at = (at + strides[s] * count + 15) & ~15;
        }

        Channel? Find(int newIndex, int oldIndex)
        {
            int index = newer ? newIndex : oldIndex;
            return index < channels.Count && channels[index].Dimension > 0 ? channels[index] : null;
        }
        var position = Find(0, 0);
        if (position == null)
            return null;
        var normal = Find(1, 1);
        var tangent = Find(2, 7);
        var colour = Find(3, 2);
        var uv0 = Find(4, 3);
        var uv1 = Find(5, 4);

        var mesh = new PhosMesh { HasNormals = normal != null, HasTangents = tangent != null, HasColors = colour != null };
        if (uv0 != null)
            mesh.HasUV0s = true;
        if (uv1 != null)
            mesh.HasUV1s = true;
        mesh.IncreaseVertexCount(count);
        var v = new float[4];
        for (int i = 0; i < count; i++)
        {
            ReadVertex(bytes, position.Value, i, starts, strides, newer, v);
            mesh.RawPositions[i] = new float3(-v[0], v[1], v[2]);
            if (normal is { } n)
            {
                ReadVertex(bytes, n, i, starts, strides, newer, v);
                mesh.RawNormals[i] = new float3(-v[0], v[1], v[2]);
            }
            if (tangent is { } t)
            {
                ReadVertex(bytes, t, i, starts, strides, newer, v);
                mesh.RawTangents[i] = new float4(-v[0], v[1], v[2], t.Dimension >= 4 ? -v[3] : -1f);
            }
            if (colour is { } c)
            {
                ReadVertex(bytes, c, i, starts, strides, newer, v);
                mesh.RawColors[i] = new color(v[0], v[1], v[2], c.Dimension >= 4 ? v[3] : 1f);
            }
            if (uv0 is { } a)
            {
                ReadVertex(bytes, a, i, starts, strides, newer, v);
                mesh.SetUV(0, i, new float2(v[0], 1f - v[1]));
            }
            if (uv1 is { } b)
            {
                ReadVertex(bytes, b, i, starts, strides, newer, v);
                mesh.SetUV(1, i, new float2(v[0], 1f - v[1]));
            }
        }

        bool wide = (doc["m_IndexFormat"]?.Int() ?? 0) == 1;
        int indexSize = wide ? 4 : 2;
        int Index(int byteAt) => wide ? (int)BinaryPrimitives.ReadUInt32LittleEndian(indexBytes.AsSpan(byteAt)) : BinaryPrimitives.ReadUInt16LittleEndian(indexBytes.AsSpan(byteAt));
        if (doc["m_SubMeshes"] is UnitySeq submeshes)
        {
            foreach (var item in submeshes.Items)
            {
                int first = item["firstByte"]?.Int() ?? 0;
                int indices = item["indexCount"]?.Int() ?? 0;
                int topology = item["topology"]?.Int() ?? 0;
                int baseVertex = item["baseVertex"]?.Int() ?? 0;
                var submesh = new PhosTriangleSubmesh(mesh);
                mesh.Submeshes.Add(submesh);
                int corners = topology == 2 ? 4 : topology == 0 ? 3 : 0;
                if (corners == 0 || first < 0 || first + indices * indexSize > indexBytes.Length)
                    continue;
                for (int k = 0; k + corners <= indices; k += corners)
                {
                    int p0 = Index(first + k * indexSize) + baseVertex;
                    int p1 = Index(first + (k + 1) * indexSize) + baseVertex;
                    int p2 = Index(first + (k + 2) * indexSize) + baseVertex;
                    if (p0 >= count || p1 >= count || p2 >= count)
                        continue;
                    submesh.AddTriangle(p0, p1, p2);
                    if (corners == 4)
                    {
                        int p3 = Index(first + (k + 3) * indexSize) + baseVertex;
                        if (p3 < count)
                            submesh.AddTriangle(p0, p2, p3);
                    }
                }
            }
        }
        return mesh.Submeshes.Count > 0 ? mesh : null;
    }

    private static byte[]? Hex(string? text)
    {
        if (string.IsNullOrEmpty(text) || (text.Length & 1) != 0)
            return null;
        try
        {
            return Convert.FromHexString(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Bytes per component. The newer numbering: float, half, unorm8, snorm8, unorm16, snorm16, uint8, sint8,
    // uint16, sint16, uint32, sint32. The older: float, half, colour (unorm8), byte, uint32.
    private static int Size(int format, bool newer) => newer
        ? format switch { 0 or 10 or 11 => 4, 1 or 4 or 5 or 8 or 9 => 2, _ => 1 }
        : format switch { 0 or 4 => 4, 1 => 2, _ => 1 };

    private static void ReadVertex(byte[] data, Channel channel, int vertex, int[] starts, int[] strides, bool newer, float[] into)
    {
        int size = Size(channel.Format, newer);
        int at = starts[channel.Stream] + strides[channel.Stream] * vertex + channel.Offset;
        for (int i = 0; i < 4; i++)
            into[i] = 0f;
        for (int i = 0; i < channel.Dimension && i < 4; i++)
        {
            int p = at + i * size;
            if (p + size > data.Length)
                break;
            var span = data.AsSpan(p);
            into[i] = newer
                ? channel.Format switch
                {
                    0 => BinaryPrimitives.ReadSingleLittleEndian(span),
                    1 => (float)BinaryPrimitives.ReadHalfLittleEndian(span),
                    2 => span[0] / 255f,
                    3 => System.Math.Max((sbyte)span[0] / 127f, -1f),
                    4 => BinaryPrimitives.ReadUInt16LittleEndian(span) / 65535f,
                    5 => System.Math.Max(BinaryPrimitives.ReadInt16LittleEndian(span) / 32767f, -1f),
                    6 => span[0],
                    7 => (sbyte)span[0],
                    8 => BinaryPrimitives.ReadUInt16LittleEndian(span),
                    9 => BinaryPrimitives.ReadInt16LittleEndian(span),
                    10 => BinaryPrimitives.ReadUInt32LittleEndian(span),
                    _ => BinaryPrimitives.ReadInt32LittleEndian(span),
                }
                : channel.Format switch
                {
                    0 => BinaryPrimitives.ReadSingleLittleEndian(span),
                    1 => (float)BinaryPrimitives.ReadHalfLittleEndian(span),
                    2 => span[0] / 255f,
                    3 => span[0],
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(span),
                };
        }
    }
}
