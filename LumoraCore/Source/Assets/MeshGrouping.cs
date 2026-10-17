// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Assets;

// Which of a model's meshes become one renderer.
//
// A node's meshes are one source object cut up by material, so the parts of a node that agree on how they
// deform go back together as one mesh with a submesh each: parts with bones, parts with blend shapes and no
// bones, and plain parts. A group the baked mesh format or the renderer could not hold is cut into
// consecutive chunks. Plain data in and out, so the grouping is decided while the parse is alive and nothing
// after it has to keep the scene. -xlinka
internal static class MeshGrouping
{
    internal enum Kind
    {
        Skinned,
        Morph,
        Plain,
    }

    internal sealed class Part
    {
        public int Index;
        public int VertexCount;
        public int TriangleCount;
        public int MaterialIndex;
        public bool HasBones;
        public bool HasMorphs;
        public int BoneCount;
        public int Flags;
        public long BlendUpperBytes;
        public long BlendBytes = -1;

        public Kind Kind => HasBones ? Kind.Skinned : HasMorphs ? Kind.Morph : Kind.Plain;

        public bool Usable => VertexCount > 0 && TriangleCount > 0;
    }

    internal sealed class Unit
    {
        public Kind Kind;
        public int[] Parts = Array.Empty<int>();

        public string Key => $"{(int)Kind}:{string.Join(",", Parts)}";
    }

    // The .lmesh reader's caps and the renderer's surface limit. The body budget keeps a tenth of the reader's
    // 768 MB back for what the estimate leaves out. -xlinka
    public const int MaxVertices = 8_000_000;
    public const int MaxSubmeshIndices = 8_000_000;
    public const int MaxSurfaces = 256;
    public const long BodyBudget = 768L * 1024 * 1024 * 9 / 10;

    public const int FlagTangents = 1;
    public const int FlagColors = 2;
    public const int FlagUV1 = 4;
    public const int FlagUV2 = 8;
    public const int FlagUV3 = 16;
    public const int FlagBones = 32;

    public static Part[] Capture(Assimp.Scene scene)
    {
        var parts = new Part[scene?.MeshCount ?? 0];
        for (int mi = 0; mi < parts.Length; mi++)
        {
            var part = new Part { Index = mi };
            parts[mi] = part;
            var mesh = scene!.Meshes[mi];
            if (mesh == null)
                continue;

            part.MaterialIndex = mesh.MaterialIndex;
            part.HasBones = mesh.HasBones;
            part.HasMorphs = mesh.HasMeshAnimationAttachments;
            if (!mesh.HasVertices || mesh.VertexCount == 0)
                continue;

            part.VertexCount = mesh.VertexCount;
            part.TriangleCount = MeshDecoder.CountTriangles(mesh);
            part.BoneCount = mesh.HasBones ? mesh.BoneCount : 0;

            int flags = 0;
            if (mesh.HasTangentBasis) flags |= FlagTangents;
            if (mesh.HasVertexColors(0)) flags |= FlagColors;
            if (HasUV(mesh, 1)) flags |= FlagUV1;
            if (HasUV(mesh, 2)) flags |= FlagUV2;
            if (HasUV(mesh, 3)) flags |= FlagUV3;
            if (mesh.HasBones) flags |= FlagBones;
            part.Flags = flags;

            if (part.HasMorphs)
            {
                bool normals = false, tangents = false;
                foreach (var att in mesh.MeshAnimationAttachments)
                {
                    if (att == null)
                        continue;
                    normals |= att.HasNormals && mesh.HasNormals;
                    tangents |= att.HasTangentBasis;
                }
                long perVertex = 16 + (normals ? 12 : 0) + (tangents ? 12 : 0);
                long shapes = mesh.MeshAnimationAttachmentCount;
                part.BlendUpperBytes = shapes * part.VertexCount * perVertex + shapes * 64;
            }
        }
        return parts;
    }

    // What a part's blend shapes cost on disk, where only the vertices a shape moves are kept. Read only when
    // the cheap upper bound says a chunk might not fit. -xlinka
    public static long ExactBlendBytes(Assimp.Mesh? mesh)
    {
        if (mesh == null || !mesh.HasMeshAnimationAttachments || !mesh.HasVertices)
            return 0;

        var baseVerts = mesh.Vertices;
        var baseNormals = mesh.HasNormals ? mesh.Normals : null;
        long total = 0;
        foreach (var att in mesh.MeshAnimationAttachments)
        {
            if (att == null)
                continue;
            var verts = att.HasVertices ? att.Vertices : null;
            var normals = att.HasNormals && baseNormals != null ? att.Normals : null;
            long moved = 0;
            for (int i = 0; i < baseVerts.Count; i++)
            {
                if ((verts != null && i < verts.Count && Moved(verts[i] - baseVerts[i]))
                    || (normals != null && i < normals.Count && i < baseNormals!.Count && Moved(normals[i] - baseNormals[i])))
                    moved++;
            }
            long perVertex = 16 + (normals != null ? 12 : 0) + (att.HasTangentBasis ? 12 : 0);
            total += moved * perVertex + 64;
        }
        return total;
    }

    // One node's meshes as units: one per kind, in the order the node first names it, each cut into chunks
    // that fit. A mesh with no triangles is left out and added to noTriangles. exactBlend is asked for a part's
    // exact blend shape cost only when its upper bound does not fit. -xlinka
    public static List<Unit> ForNode(IReadOnlyList<int> meshIndices, Part[] parts, Func<int, long>? exactBlend,
        ISet<int>? noTriangles)
    {
        var units = new List<Unit>();
        if (meshIndices == null || parts == null)
            return units;

        var kinds = new List<Kind>(3);
        var byKind = new Dictionary<Kind, List<Part>>();
        var seen = new HashSet<int>();
        foreach (int mi in meshIndices)
        {
            if (mi < 0 || mi >= parts.Length || !seen.Add(mi))
                continue;
            var part = parts[mi];
            if (part == null || !part.Usable)
            {
                noTriangles?.Add(mi);
                continue;
            }
            if (!byKind.TryGetValue(part.Kind, out var list))
            {
                list = new List<Part>();
                byKind[part.Kind] = list;
                kinds.Add(part.Kind);
            }
            list.Add(part);
        }

        foreach (var kind in kinds)
            Chunk(kind, byKind[kind], exactBlend, units);
        return units;
    }

    private static void Chunk(Kind kind, List<Part> parts, Func<int, long>? exactBlend, List<Unit> units)
    {
        var chunk = new List<Part>();
        long vertices = 0, fixedBytes = 0, blendUpper = 0;
        int flags = 0;
        foreach (var part in parts)
        {
            if (chunk.Count > 0 && !Fits(chunk, vertices, fixedBytes, blendUpper, flags, part, exactBlend))
            {
                units.Add(ToUnit(kind, chunk));
                chunk.Clear();
                vertices = fixedBytes = blendUpper = 0;
                flags = 0;
            }
            chunk.Add(part);
            vertices += part.VertexCount;
            fixedBytes += FixedBytes(part);
            blendUpper += part.BlendUpperBytes;
            flags |= part.Flags;
        }
        if (chunk.Count > 0)
            units.Add(ToUnit(kind, chunk));
    }

    // Whether next joins chunk. A part too big for the format on its own stays alone, where it bakes exactly
    // as a single mesh always has. -xlinka
    private static bool Fits(List<Part> chunk, long vertices, long fixedBytes, long blendUpper, int flags, Part next,
        Func<int, long>? exactBlend)
    {
        if (chunk.Count + 1 > MaxSurfaces)
            return false;
        if (Oversized(chunk[0]) || Oversized(next))
            return false;
        long total = vertices + next.VertexCount;
        if (total > MaxVertices)
            return false;
        long body = total * BytesPerVertex(flags | next.Flags) + fixedBytes + FixedBytes(next);
        if (body > BodyBudget)
            return false;
        if (body + blendUpper + next.BlendUpperBytes <= BodyBudget)
            return true;
        if (exactBlend == null)
            return false;
        long blend = Exact(next, exactBlend);
        foreach (var part in chunk)
            blend += Exact(part, exactBlend);
        return body + blend <= BodyBudget;
    }

    private static bool Oversized(Part part) =>
        (long)part.TriangleCount * 3 > MaxSubmeshIndices || part.VertexCount > MaxVertices;

    private static long Exact(Part part, Func<int, long> exactBlend)
    {
        if (part.BlendBytes < 0)
            part.BlendBytes = part.HasMorphs ? exactBlend(part.Index) : 0;
        return part.BlendBytes;
    }

    // The baked format's per-vertex streams for these channels; position, normal and the first UV channel
    // are always written.
    private static long BytesPerVertex(int flags)
    {
        long bytes = 12 + 12 + 8;
        if ((flags & FlagTangents) != 0) bytes += 16;
        if ((flags & FlagColors) != 0) bytes += 16;
        if ((flags & FlagBones) != 0) bytes += 32;
        if ((flags & FlagUV1) != 0) bytes += 8;
        if ((flags & FlagUV2) != 0) bytes += 8;
        if ((flags & FlagUV3) != 0) bytes += 8;
        return bytes;
    }

    // Indices, a submesh header, and a generous allowance per bone entry.
    private static long FixedBytes(Part part) => (long)part.TriangleCount * 12 + 8 + part.BoneCount * 128L;

    private static Unit ToUnit(Kind kind, List<Part> chunk)
    {
        var indices = new int[chunk.Count];
        for (int i = 0; i < chunk.Count; i++)
            indices[i] = chunk[i].Index;
        return new Unit { Kind = kind, Parts = indices };
    }

    private static bool Moved(System.Numerics.Vector3 d) =>
        System.Math.Abs(d.X) > 1e-6f || System.Math.Abs(d.Y) > 1e-6f || System.Math.Abs(d.Z) > 1e-6f;

    private static bool HasUV(Assimp.Mesh mesh, int channel) =>
        mesh.TextureCoordinateChannelCount > channel
        && mesh.TextureCoordinateChannels[channel] != null
        && mesh.TextureCoordinateChannels[channel].Count > 0;
}
