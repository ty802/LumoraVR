// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Text;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Assets;

/// <summary>
/// Binary (de)serializer for a PhosMesh - the ".lmesh" on-disk/asset format. Engine-agnostic: it only
/// touches PhosMesh's public surface, no Godot. This is what lets a skinned avatar mesh travel as a
/// single content-hashed asset instead of tens of thousands of replicated data-model elements.
///
/// Every read is bounds-checked and the whole decode is wrapped so a malformed/truncated blob throws a
/// clean InvalidDataException instead of corrupting state or crashing. -xlinka
/// </summary>
public static class PhosMeshSerializer
{
    private static readonly byte[] Magic = { (byte)'L', (byte)'M', (byte)'S', (byte)'H' };
    private const int Version = 1;

    // Sanity caps so a hostile/garbage header can't make us allocate or loop unbounded. -xlinka
    private const int MaxVertices = 8_000_000;
    private const int MaxCount = 8_000_000;
    private const int MaxNameBytes = 4096;

    // Geometry flags packed into one byte.
    [Flags]
    private enum MeshFlags : byte
    {
        None = 0,
        Normals = 1 << 0,
        Tangents = 1 << 1,
        Colors = 1 << 2,
        BoneBindings = 1 << 3,
    }

    public static byte[] Serialize(PhosMesh mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);

            int vc = mesh.VertexCount;
            w.Write(vc);

            var flags = MeshFlags.None;
            if (mesh.HasNormals) flags |= MeshFlags.Normals;
            if (mesh.HasTangents) flags |= MeshFlags.Tangents;
            if (mesh.HasColors) flags |= MeshFlags.Colors;
            if (mesh.HasBoneBindings) flags |= MeshFlags.BoneBindings;
            w.Write((byte)flags);

            int uvCount = System.Math.Clamp(mesh.UVChannelCount, 0, 4);
            w.Write(uvCount);

            // Per-vertex blocks (read up to VertexCount, never array.Length - the backing arrays are
            // intentionally over-allocated). -xlinka
            var pos = mesh.RawPositions;
            for (int i = 0; i < vc; i++) WriteFloat3(w, pos[i]);

            if (flags.HasFlag(MeshFlags.Normals))
            {
                var n = mesh.RawNormals;
                for (int i = 0; i < vc; i++) WriteFloat3(w, n[i]);
            }
            if (flags.HasFlag(MeshFlags.Tangents))
            {
                var t = mesh.RawTangents;
                for (int i = 0; i < vc; i++) WriteFloat4(w, t[i]);
            }
            if (flags.HasFlag(MeshFlags.Colors))
            {
                var c = mesh.RawColors;
                for (int i = 0; i < vc; i++) WriteColor(w, c[i]);
            }
            if (flags.HasFlag(MeshFlags.BoneBindings))
            {
                var b = mesh.RawBoneBindings;
                for (int i = 0; i < vc; i++)
                {
                    WriteFloat4(w, b[i].boneIndices);
                    WriteFloat4(w, b[i].boneWeights);
                }
            }

            for (int ch = 0; ch < uvCount; ch++)
            {
                var uv = GetUVChannel(mesh, ch);
                for (int i = 0; i < vc; i++)
                    WriteFloat2(w, i < uv.Length ? uv[i] : float2.Zero);
            }

            // Submeshes
            w.Write(mesh.Submeshes.Count);
            foreach (var sm in mesh.Submeshes)
            {
                w.Write((int)sm.Topology);
                int idxCount = sm.IndexCount;
                w.Write(idxCount);
                var idx = sm.RawIndices;
                for (int i = 0; i < idxCount; i++) w.Write(idx[i]);
            }

            // Bone table (name + bind pose)
            w.Write(mesh.BoneCount);
            for (int i = 0; i < mesh.BoneCount; i++)
            {
                var bone = mesh.GetBone(i);
                WriteString(w, bone.Name);
                WriteMatrix(w, bone.BindPose);
            }

            // Blend shapes
            w.Write(mesh.BlendShapes.Count);
            foreach (var shape in mesh.BlendShapes)
            {
                WriteString(w, shape.Name ?? string.Empty);
                int frameCount = shape.Frames?.Length ?? 0;
                w.Write(frameCount);
                for (int f = 0; f < frameCount; f++)
                {
                    var frame = shape.Frames![f];
                    bool hasN = frame != null && frame.normals != null && frame.normals.Length > 0;
                    bool hasT = frame != null && frame.tangents != null && frame.tangents.Length > 0;
                    w.Write(hasN);
                    w.Write(hasT);

                    for (int i = 0; i < vc; i++)
                        WriteFloat3(w, SafeGet(frame?.positions, i));
                    if (hasN)
                        for (int i = 0; i < vc; i++) WriteFloat3(w, SafeGet(frame!.normals, i));
                    if (hasT)
                        for (int i = 0; i < vc; i++) WriteFloat3(w, SafeGet(frame!.tangents, i));
                }
            }
        }
        return ms.ToArray();
    }

    public static PhosMesh Deserialize(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        try
        {
            using var ms = new MemoryStream(data, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = r.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                throw new InvalidDataException("Not an .lmesh blob (bad magic).");

            int version = r.ReadInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported .lmesh version {version} (expected {Version}).");

            int vc = r.ReadInt32();
            if (vc < 0 || vc > MaxVertices)
                throw new InvalidDataException($"Vertex count {vc} out of range.");

            var flags = (MeshFlags)r.ReadByte();
            int uvCount = r.ReadInt32();
            if (uvCount < 0 || uvCount > 4)
                throw new InvalidDataException($"UV channel count {uvCount} out of range.");

            var mesh = new PhosMesh();

            // Set flags BEFORE growing the vertex arrays so IncreaseVertexCount allocates them. -xlinka
            mesh.HasNormals = flags.HasFlag(MeshFlags.Normals);
            mesh.HasTangents = flags.HasFlag(MeshFlags.Tangents);
            mesh.HasColors = flags.HasFlag(MeshFlags.Colors);
            mesh.HasBoneBindings = flags.HasFlag(MeshFlags.BoneBindings);
            if (mesh.HasBoneBindings) mesh.EnsureBoneBindings();

            if (vc > 0) mesh.IncreaseVertexCount(vc);

            var pos = mesh.RawPositions;
            for (int i = 0; i < vc; i++) pos[i] = ReadFloat3(r);

            if (mesh.HasNormals)
            {
                var n = mesh.RawNormals;
                for (int i = 0; i < vc; i++) n[i] = ReadFloat3(r);
            }
            if (mesh.HasTangents)
            {
                var t = mesh.RawTangents;
                for (int i = 0; i < vc; i++) t[i] = ReadFloat4(r);
            }
            if (mesh.HasColors)
            {
                var c = mesh.RawColors;
                for (int i = 0; i < vc; i++) c[i] = ReadColor(r);
            }
            if (mesh.HasBoneBindings)
            {
                var b = mesh.RawBoneBindings;
                for (int i = 0; i < vc; i++)
                {
                    var indices = ReadFloat4(r);
                    var weights = ReadFloat4(r);
                    b[i] = new PhosBoneBinding(indices, weights);
                }
            }

            for (int ch = 0; ch < uvCount; ch++)
            {
                mesh.SetHasUV(ch, true);
                for (int i = 0; i < vc; i++)
                    mesh.SetUV(ch, i, ReadFloat2(r));
            }

            // Submeshes
            int submeshCount = r.ReadInt32();
            if (submeshCount < 0 || submeshCount > MaxCount)
                throw new InvalidDataException($"Submesh count {submeshCount} out of range.");
            for (int s = 0; s < submeshCount; s++)
            {
                int topology = r.ReadInt32();
                int idxCount = r.ReadInt32();
                if (idxCount < 0 || idxCount > MaxCount)
                    throw new InvalidDataException($"Submesh index count {idxCount} out of range.");

                PhosSubmesh sm = (PhosTopology)topology switch
                {
                    PhosTopology.Triangles => new PhosTriangleSubmesh(mesh),
                    PhosTopology.Points => new PhosPointSubmesh(mesh),
                    _ => throw new InvalidDataException($"Unsupported submesh topology {topology}."),
                };

                if (idxCount % sm.IndicesPerElement != 0)
                    throw new InvalidDataException($"Index count {idxCount} is not a multiple of {sm.IndicesPerElement} for topology {topology}.");

                if (idxCount > 0) sm.IncreaseCount(idxCount / sm.IndicesPerElement);
                var idx = sm.RawIndices;
                for (int i = 0; i < idxCount; i++) idx[i] = r.ReadInt32();

                mesh.Submeshes.Add(sm);
            }

            // Bone table
            int boneCount = r.ReadInt32();
            if (boneCount < 0 || boneCount > MaxCount)
                throw new InvalidDataException($"Bone count {boneCount} out of range.");
            for (int i = 0; i < boneCount; i++)
            {
                var name = ReadString(r);
                var bindPose = ReadMatrix(r);
                var bone = mesh.AddBone(name);
                bone.BindPose = bindPose;
            }

            // Blend shapes
            int blendCount = r.ReadInt32();
            if (blendCount < 0 || blendCount > MaxCount)
                throw new InvalidDataException($"Blend shape count {blendCount} out of range.");
            for (int s = 0; s < blendCount; s++)
            {
                var name = ReadString(r);
                int frameCount = r.ReadInt32();
                if (frameCount < 0 || frameCount > MaxCount)
                    throw new InvalidDataException($"Blend shape frame count {frameCount} out of range.");

                var shape = new PhosBlendShape(name, frameCount);
                for (int f = 0; f < frameCount; f++)
                {
                    bool hasN = r.ReadBoolean();
                    bool hasT = r.ReadBoolean();

                    // PhosBlendShape allocates a Frames array of NULL elements (it's a class) - we must
                    // instantiate each frame before writing into it. -xlinka
                    var frame = new PhosBlendShapeFrame { positions = new float3[vc] };
                    for (int i = 0; i < vc; i++) frame.positions[i] = ReadFloat3(r);
                    if (hasN)
                    {
                        frame.normals = new float3[vc];
                        for (int i = 0; i < vc; i++) frame.normals[i] = ReadFloat3(r);
                    }
                    if (hasT)
                    {
                        frame.tangents = new float3[vc];
                        for (int i = 0; i < vc; i++) frame.tangents[i] = ReadFloat3(r);
                    }
                    shape.Frames[f] = frame;
                }
                mesh.BlendShapes.Add(shape);
            }

            return mesh;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Truncated .lmesh blob.", ex);
        }
    }

    // --- primitive read/write helpers (BinaryWriter is little-endian on our targets) ---

    private static void WriteFloat2(BinaryWriter w, float2 v) { w.Write(v.x); w.Write(v.y); }
    private static void WriteFloat3(BinaryWriter w, float3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    private static void WriteFloat4(BinaryWriter w, float4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
    private static void WriteColor(BinaryWriter w, color v) { w.Write(v.r); w.Write(v.g); w.Write(v.b); w.Write(v.a); }
    private static void WriteMatrix(BinaryWriter w, float4x4 m)
    {
        WriteFloat4(w, m.c0); WriteFloat4(w, m.c1); WriteFloat4(w, m.c2); WriteFloat4(w, m.c3);
    }

    private static float2 ReadFloat2(BinaryReader r) => new float2 { x = r.ReadSingle(), y = r.ReadSingle() };
    private static float3 ReadFloat3(BinaryReader r) => new float3 { x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle() };
    private static float4 ReadFloat4(BinaryReader r) => new float4 { x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle(), w = r.ReadSingle() };
    private static color ReadColor(BinaryReader r) => new color { r = r.ReadSingle(), g = r.ReadSingle(), b = r.ReadSingle(), a = r.ReadSingle() };
    private static float4x4 ReadMatrix(BinaryReader r) => new float4x4 { c0 = ReadFloat4(r), c1 = ReadFloat4(r), c2 = ReadFloat4(r), c3 = ReadFloat4(r) };

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
        if (bytes.Length > MaxNameBytes)
            throw new InvalidDataException($"String length {bytes.Length} exceeds cap {MaxNameBytes}.");
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    private static string ReadString(BinaryReader r)
    {
        int len = r.ReadInt32();
        if (len < 0 || len > MaxNameBytes)
            throw new InvalidDataException($"String length {len} out of range.");
        var bytes = r.ReadBytes(len);
        if (bytes.Length != len)
            throw new InvalidDataException("Truncated string.");
        return Encoding.UTF8.GetString(bytes);
    }

    private static float2[] GetUVChannel(PhosMesh mesh, int ch) => ch switch
    {
        0 => mesh.RawUV0s,
        1 => mesh.RawUV1s,
        2 => mesh.RawUV2s,
        3 => mesh.RawUV3s,
        _ => Array.Empty<float2>(),
    };

    private static float3 SafeGet(float3[]? arr, int i) => (arr != null && i < arr.Length) ? arr[i] : float3.Zero;
}
