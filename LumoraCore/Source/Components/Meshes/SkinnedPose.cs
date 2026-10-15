// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// A skinned mesh's posed shape, worked out here the way the renderer skins it: every mesh bone found in the
// renderer's skeleton by name (the same forgiving match the renderer uses, unknown names on the first bone), each
// vertex moved by its bones' world transforms times their bind poses, blend shapes first at their current weights.
// Only the vertices asked for are touched, and only the bones those vertices use are read. -xlinka
public sealed class SkinnedPose
{
    public SkinnedMeshRenderer Renderer { get; }

    public PhosMesh Mesh { get; }

    public int BoneCount => _bones.Length;

    public int UnresolvedBones { get; private set; }

    // No bone data: the mesh rides its renderer's slot.
    public bool IsRigid => _bones.Length == 0;

    private readonly Slot?[] _bones;
    private readonly float4x4[] _bind;
    private readonly float4x4[] _skin;
    private readonly float4x4[] _seen;
    private readonly bool[] _used;
    private int[] _usedList = Array.Empty<int>();
    private float4x4 _rigidSeen;
    private float[] _weights = Array.Empty<float>();
    private readonly List<int> _activeShapes = new();

    private SkinnedPose(SkinnedMeshRenderer renderer, PhosMesh mesh, Slot?[] bones, float4x4[] bind)
    {
        Renderer = renderer;
        Mesh = mesh;
        _bones = bones;
        _bind = bind;
        _skin = new float4x4[bones.Length];
        _seen = new float4x4[bones.Length];
        _used = new bool[bones.Length];
    }

    // Null with a reason when the renderer cannot be followed yet or at all.
    public static SkinnedPose? TryCreate(SkinnedMeshRenderer renderer, out string? problem)
    {
        problem = MeshSource.Refusal(renderer, keepsRenderers: true);
        if (problem != null)
            return null;
        var mesh = renderer.MeshAsset.Asset?.MeshData;
        if (mesh == null || mesh.VertexCount == 0)
        {
            problem = "waiting for the skinned mesh to load";
            return null;
        }

        int boneCount = mesh.HasBoneBindings ? mesh.BoneCount : 0;
        var bones = new Slot?[boneCount];
        var bind = new float4x4[boneCount];
        int unresolved = 0;
        if (boneCount > 0)
        {
            var skeleton = renderer.Skeleton.Target;
            if (skeleton == null || skeleton.IsDestroyed || skeleton.BoneSlots.Count == 0)
            {
                problem = "waiting for the skinned mesh's skeleton";
                return null;
            }
            var matcher = new BoneNameMatcher();
            int count = System.Math.Min(skeleton.BoneNames.Count, skeleton.BoneSlots.Count);
            for (int b = 0; b < count; b++)
                matcher.Add(skeleton.BoneNames[b], b);
            for (int i = 0; i < boneCount; i++)
            {
                if (!matcher.TryResolve(mesh.GetBoneName(i), out int index, out _))
                {
                    unresolved++;
                    index = 0;
                }
                bones[i] = skeleton.BoneSlots[index];
                bind[i] = mesh.GetBoneBindPose(i);
            }
        }

        return new SkinnedPose(renderer, mesh, bones, bind) { UnresolvedBones = unresolved };
    }

    public Slot? BoneSlot(int meshBone) => meshBone >= 0 && meshBone < _bones.Length ? _bones[meshBone] : null;

    // The bones these vertices lean on; only they are read from then on.
    public void Use(int[] vertices)
    {
        Array.Clear(_used, 0, _used.Length);
        if (IsRigid)
            return;
        var bindings = Mesh.RawBoneBindings;
        foreach (int v in vertices)
        {
            var b = bindings[v];
            Mark(b.boneIndices.x, b.boneWeights.x);
            Mark(b.boneIndices.y, b.boneWeights.y);
            Mark(b.boneIndices.z, b.boneWeights.z);
            Mark(b.boneIndices.w, b.boneWeights.w);
        }
        var list = new List<int>();
        for (int i = 0; i < _used.Length; i++)
            if (_used[i])
                list.Add(i);
        _usedList = list.ToArray();
        for (int i = 0; i < _seen.Length; i++)
            _seen[i] = float4x4.Zero;
        _rigidSeen = float4x4.Zero;

        void Mark(float index, float weight)
        {
            int i = (int)index;
            if (weight > 0f && i >= 0 && i < _used.Length)
                _used[i] = true;
        }
    }

    // This frame's bone transforms. True when a bone in use, the renderer's slot for a rigid mesh, or a blend shape
    // weight moved since the last call.
    public bool Refresh()
    {
        bool moved = false;
        if (IsRigid)
        {
            var slot = Renderer.Slot;
            var m = slot != null && !slot.IsDestroyed ? slot.LocalToWorld : float4x4.Identity;
            moved = m != _rigidSeen;
            _rigidSeen = m;
        }
        else
        {
            foreach (int i in _usedList)
            {
                var bone = _bones[i];
                var world = bone != null && !bone.IsDestroyed ? bone.LocalToWorld : float4x4.Identity;
                if (world != _seen[i])
                {
                    moved = true;
                    _seen[i] = world;
                }
                _skin[i] = world * _bind[i];
            }
        }

        int shapes = System.Math.Min(Renderer.BlendShapeCount, Mesh.BlendShapeCount);
        if (_weights.Length != shapes)
        {
            _weights = new float[shapes];
            moved = true;
        }
        _activeShapes.Clear();
        for (int s = 0; s < shapes; s++)
        {
            float w = Renderer.GetEffectiveBlendShapeWeight(s);
            if (w != _weights[s])
            {
                moved = true;
                _weights[s] = w;
            }
            if (MathF.Abs(w) > 1e-4f && Mesh.BlendShapes[s].Frames.Length > 0 && Mesh.BlendShapes[s].Frames[0].positions.Length > 0)
                _activeShapes.Add(s);
        }
        return moved;
    }

    // Posed world positions (and normals and tangents when asked) of the listed vertices, from the last Refresh.
    public void Sample(int[] vertices, float3[] positions, float3[]? normals, float4[]? tangents)
    {
        var mesh = Mesh;
        var basePositions = mesh.RawPositions;
        var baseNormals = mesh.HasNormals ? mesh.RawNormals : null;
        var baseTangents = mesh.HasTangents ? mesh.RawTangents : null;
        var bindings = IsRigid ? null : mesh.RawBoneBindings;

        for (int k = 0; k < vertices.Length; k++)
        {
            int v = vertices[k];
            var p = basePositions[v];
            var n = baseNormals != null ? baseNormals[v] : float3.Up;
            foreach (int s in _activeShapes)
            {
                var frame = mesh.BlendShapes[s].Frames[0];
                float w = _weights[s];
                if (v < frame.positions.Length)
                    p += frame.positions[v] * w;
                if (normals != null && v < frame.normals.Length)
                    n += frame.normals[v] * w;
            }

            if (bindings == null)
            {
                positions[k] = _rigidSeen.MultiplyPoint(in p);
                if (normals != null)
                    normals[k] = Unit(_rigidSeen.MultiplyVector(in n));
                if (tangents != null && baseTangents != null)
                {
                    var t = baseTangents[v];
                    var tt = Unit(_rigidSeen.MultiplyVector(new float3(t.x, t.y, t.z)));
                    tangents[k] = new float4(tt.x, tt.y, tt.z, t.w);
                }
                continue;
            }

            // The vertex's bones blended into one transform first, then applied once to the position, the normal and
            // the tangent: a third of the work of skinning each of them through every bone.
            var b = bindings[v];
            float total = 0f;
            float4 c0 = default, c1 = default, c2 = default, c3 = default;
            Blend(b.boneIndices.x, b.boneWeights.x, ref c0, ref c1, ref c2, ref c3, ref total);
            Blend(b.boneIndices.y, b.boneWeights.y, ref c0, ref c1, ref c2, ref c3, ref total);
            Blend(b.boneIndices.z, b.boneWeights.z, ref c0, ref c1, ref c2, ref c3, ref total);
            Blend(b.boneIndices.w, b.boneWeights.w, ref c0, ref c1, ref c2, ref c3, ref total);
            if (total <= 1e-6f)
            {
                positions[k] = p;
                if (normals != null)
                    normals[k] = Unit(n);
                if (tangents != null && baseTangents != null)
                    tangents[k] = baseTangents[v];
                continue;
            }
            if (MathF.Abs(total - 1f) > 1e-4f)
            {
                float inv = 1f / total;
                c0 *= inv;
                c1 *= inv;
                c2 *= inv;
                c3 *= inv;
            }
            positions[k] = new float3(
                c0.x * p.x + c1.x * p.y + c2.x * p.z + c3.x,
                c0.y * p.x + c1.y * p.y + c2.y * p.z + c3.y,
                c0.z * p.x + c1.z * p.y + c2.z * p.z + c3.z);
            if (normals != null)
                normals[k] = Unit(new float3(
                    c0.x * n.x + c1.x * n.y + c2.x * n.z,
                    c0.y * n.x + c1.y * n.y + c2.y * n.z,
                    c0.z * n.x + c1.z * n.y + c2.z * n.z));
            if (tangents != null && baseTangents != null)
            {
                var t = baseTangents[v];
                var tt = Unit(new float3(
                    c0.x * t.x + c1.x * t.y + c2.x * t.z,
                    c0.y * t.x + c1.y * t.y + c2.y * t.z,
                    c0.z * t.x + c1.z * t.y + c2.z * t.z));
                tangents[k] = new float4(tt.x, tt.y, tt.z, t.w);
            }
        }
    }

    private void Blend(float index, float weight, ref float4 c0, ref float4 c1, ref float4 c2, ref float4 c3, ref float total)
    {
        int i = (int)index;
        if (weight <= 0f || i < 0 || i >= _skin.Length)
            return;
        ref var m = ref _skin[i];
        c0 += m.c0 * weight;
        c1 += m.c1 * weight;
        c2 += m.c2 * weight;
        c3 += m.c3 * weight;
        total += weight;
    }

    private static float3 Unit(in float3 v)
    {
        float l = v.LengthSquared;
        return l > 1e-20f ? v / MathF.Sqrt(l) : float3.Up;
    }

    // Share of each vertex's weight on the given bones.
    public float WeightOn(int vertex, bool[] bones)
    {
        if (IsRigid)
            return 0f;
        var b = Mesh.RawBoneBindings[vertex];
        float on = 0f, total = 0f;
        Add(b.boneIndices.x, b.boneWeights.x);
        Add(b.boneIndices.y, b.boneWeights.y);
        Add(b.boneIndices.z, b.boneWeights.z);
        Add(b.boneIndices.w, b.boneWeights.w);
        return total > 1e-6f ? on / total : 0f;

        void Add(float index, float weight)
        {
            int i = (int)index;
            if (weight <= 0f || i < 0 || i >= bones.Length)
                return;
            total += weight;
            if (bones[i])
                on += weight;
        }
    }

    // Mesh bones whose slot is one of these or sits under one of them.
    public bool[] BonesUnder(IReadOnlyList<Slot> roots)
    {
        var result = new bool[_bones.Length];
        for (int i = 0; i < _bones.Length; i++)
        {
            var bone = _bones[i];
            if (bone == null || bone.IsDestroyed)
                continue;
            foreach (var root in roots)
            {
                if (root != null && !root.IsDestroyed && (bone == root || bone.IsDescendantOf(root)))
                {
                    result[i] = true;
                    break;
                }
            }
        }
        return result;
    }

    // The shallowest bones in the hierarchy that carry weight among these vertices: what a separate part (an ear, a
    // tail, a skirt) hangs from.
    public bool[] AnchorBones(int[] vertices)
    {
        var result = new bool[_bones.Length];
        if (IsRigid)
            return result;
        var carries = new bool[_bones.Length];
        var bindings = Mesh.RawBoneBindings;
        foreach (int v in vertices)
        {
            var b = bindings[v];
            Mark(b.boneIndices.x, b.boneWeights.x);
            Mark(b.boneIndices.y, b.boneWeights.y);
            Mark(b.boneIndices.z, b.boneWeights.z);
            Mark(b.boneIndices.w, b.boneWeights.w);
        }
        int shallowest = int.MaxValue;
        var depth = new int[_bones.Length];
        for (int i = 0; i < _bones.Length; i++)
        {
            if (!carries[i] || _bones[i] == null)
                continue;
            depth[i] = Depth(_bones[i]!);
            shallowest = System.Math.Min(shallowest, depth[i]);
        }
        for (int i = 0; i < _bones.Length; i++)
            result[i] = carries[i] && _bones[i] != null && depth[i] == shallowest;
        return result;

        void Mark(float index, float weight)
        {
            int i = (int)index;
            if (weight > 0.001f && i >= 0 && i < carries.Length)
                carries[i] = true;
        }
    }

    private static int Depth(Slot slot)
    {
        int depth = 0;
        for (var walk = slot.Parent; walk != null && depth < 4096; walk = walk.Parent)
            depth++;
        return depth;
    }

    public string BoneNames(bool[] bones, int max = 3)
    {
        var names = new List<string>();
        int more = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            if (!bones[i] || _bones[i] == null)
                continue;
            string name = _bones[i]!.SlotName.Value ?? "";
            if (names.Contains(name))
                continue;
            if (names.Count < max)
                names.Add(name);
            else
                more++;
        }
        return names.Count == 0 ? "none" : string.Join(", ", names) + (more > 0 ? $" and {more} more" : "");
    }
}
