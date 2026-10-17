// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

public class PhosMesh
{
    // Vertex Data Arrays
    internal float3[] positions = Array.Empty<float3>();
    internal float3[] normals = Array.Empty<float3>();
    internal float4[] tangents = Array.Empty<float4>();
    internal color[] colors = Array.Empty<color>();
    internal PhosUVArray[] uvChannels = new PhosUVArray[4];
    internal PhosBoneBinding[] boneBindings = Array.Empty<PhosBoneBinding>();
    internal List<PhosBone> bones = new List<PhosBone>();
    internal BitArray flags = new BitArray(0);

    // Vertex Tracking
    internal int[] vertexIDs = Array.Empty<int>();  // For version tracking
    private int _currentVertexID = 0;

    // Arrays a Clear let go of. A procedural mesh whose topology changes clears and builds again, and a big
    // one (a canvas chunk, a graph's wires) used to drop and regrow every channel each time: about 10 MB of
    // large-object garbage a rebuild for one graph's wires. The next build takes them back, filled exactly
    // as a new array would be, so nothing reads what the last build left. -xlinka
    private float3[] _sparePositions = Array.Empty<float3>();
    private float3[] _spareNormals = Array.Empty<float3>();
    private float4[] _spareTangents = Array.Empty<float4>();
    private color[] _spareColors = Array.Empty<color>();
    private PhosBoneBinding[] _spareBoneBindings = Array.Empty<PhosBoneBinding>();
    private readonly float2[][] _spareUVs = { Array.Empty<float2>(), Array.Empty<float2>(), Array.Empty<float2>(), Array.Empty<float2>() };
    private readonly List<int[]> _spareIndices = new List<int[]>();
    private const int SpareIndexArrays = 4;

    // Room the channels are kept at, grown geometrically. Its own number, not positions.Length: a channel
    // that took back a big spare must not drag every other channel up to that size.
    private int _capacity;

    // Bumped by every Clear. A cleared mesh can hand the same array back with other contents, so anything
    // that caches on an array's identity keys on this as well.
    public int ClearCount { get; private set; }

    public int VertexCount { get; private set; }

    // Changes on every vertex removal.
    internal int VerticesVersion { get; private set; } = -1;

    // Required for vertex and triangle removal.
    public bool TrackRemovals { get; set; }

    // Metadata Flags
    public bool HasNormals { get; set; }

    public bool HasTangents { get; set; }

    public bool HasColors { get; set; }

    public bool HasBoneBindings { get; set; }

    public bool HasFlags { get; set; }

    // 0..4.
    public int UVChannelCount { get; private set; }

    public bool HasUV0s
    {
        get => uvChannels[0].HasData;
        set => SetHasUV(0, value);
    }

    public bool HasUV1s
    {
        get => uvChannels[1].HasData;
        set => SetHasUV(1, value);
    }

    // The live backing arrays, no copies. They run past VertexCount, so read up to VertexCount only.
    public float3[] RawPositions => positions;

    public float3[] RawNormals => normals;

    public float4[] RawTangents => tangents;

    public color[] RawColors => colors;

    public float2[] RawUV0s => uvChannels[0].uv2D ?? Array.Empty<float2>();

    public float2[] RawUV1s => uvChannels[1].uv2D ?? Array.Empty<float2>();

    public float2[] RawUV2s => uvChannels[2].uv2D ?? Array.Empty<float2>();

    public float2[] RawUV3s => uvChannels[3].uv2D ?? Array.Empty<float2>();

    public PhosBoneBinding[] RawBoneBindings => boneBindings;

    public List<PhosSubmesh> Submeshes { get; } = new List<PhosSubmesh>();

    public List<PhosBlendShape> BlendShapes { get; } = new List<PhosBlendShape>();

    public PhosMesh()
    {
    }

    // Vertex Management

    public void IncreaseVertexCount(int count)
    {
        int oldCount = VertexCount;
        VertexCount += count;

        // Grow backing arrays geometrically. Emitters (UI glyphs/quads) call this once per quad,
        // so resizing to the exact VertexCount every time made buffer growth O(n^2): each quad
        // reallocated and copied the whole buffer. Over-allocate to amortize to O(n). VertexCount
        // stays exact and every consumer reads up to VertexCount, never array.Length. - xlinka
        if (_capacity < VertexCount)
        {
            _capacity = System.Math.Max(VertexCount, System.Math.Max(_capacity * 2, 64));
        }
        int capacity = _capacity;

        EnsureArray(true, ref positions, capacity, float3.Zero, ref _sparePositions);
        EnsureArray(HasNormals, ref normals, capacity, float3.Zero, ref _spareNormals);
        EnsureArray(HasTangents, ref tangents, capacity, float4.Zero, ref _spareTangents);
        EnsureArray(HasColors, ref colors, capacity, color.White, ref _spareColors);
        EnsureArray(HasBoneBindings, ref boneBindings, capacity, new PhosBoneBinding(), ref _spareBoneBindings);

        for (int i = 0; i < UVChannelCount; i++)
        {
            if (uvChannels[i].uv2D != null)
                EnsureArray(true, ref uvChannels[i].uv2D!, capacity, float2.Zero, ref _spareUVs[i]);
        }

        if (HasFlags)
        {
            var newFlags = new BitArray(VertexCount);
            for (int i = 0; i < oldCount; i++)
                newFlags[i] = flags[i];
            flags = newFlags;
        }

        // Track vertex IDs for removal detection
        if (TrackRemovals)
        {
            EnsureArray(true, ref vertexIDs, VertexCount, 0);
            for (int i = oldCount; i < VertexCount; i++)
                vertexIDs[i] = _currentVertexID++;
        }
    }

    public PhosVertex GetVertex(int index)
    {
        if (index < 0 || index >= VertexCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        return new PhosVertex(index, this);
    }

    public void RemoveVertices(int index, int count, bool updateSubmeshes = true)
    {
        if (count == 0) return;
        if (index < 0 || index >= VertexCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (index + count > VertexCount)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (updateSubmeshes)
        {
            foreach (var submesh in Submeshes)
                submesh.VerticesRemoved(index, count);
        }

        RemoveElements(positions, index, count, VertexCount, false, float3.Zero);
        RemoveElements(normals, index, count, VertexCount, false, float3.Zero);
        RemoveElements(tangents, index, count, VertexCount, false, float4.Zero);
        RemoveElements(colors, index, count, VertexCount, false, color.White);
        RemoveElements(boneBindings, index, count, VertexCount, false, new PhosBoneBinding());
        RemoveElements(vertexIDs, index, count, VertexCount, true, int.MaxValue);

        for (int i = 0; i < UVChannelCount; i++)
        {
            if (uvChannels[i].uv2D != null)
                RemoveElements(uvChannels[i].uv2D!, index, count, VertexCount, false, float2.Zero);
        }

        VertexCount -= count;
        VerticesVersion--;
    }

    public void RemoveVertices(Collections.PhosVertexCollection vertices)
    {
        if (vertices.Count == 0) return;

        // Back to front, so the indices still to go stay valid.
        var sorted = vertices.OrderByDescending(v => v.Index).ToList();

        foreach (var vertex in sorted)
        {
            RemoveVertices(vertex.Index, 1, true);
        }
    }

    public void RemoveTriangles(Collections.PhosTriangleCollection triangles)
    {
        if (triangles.Count == 0) return;

        var bySubmesh = triangles.GroupBy(t => t.Submesh);

        foreach (var group in bySubmesh)
        {
            group.Key.Remove(new Collections.PhosTriangleCollection(group.ToList()));
        }
    }

    public void RemovePoints(Collections.PhosPointCollection points)
    {
        if (points.Count == 0) return;

        var bySubmesh = points.GroupBy(p => p.Submesh);

        foreach (var group in bySubmesh)
        {
            group.Key.Remove(new Collections.PhosPointCollection(group.ToList()));
        }
    }

    public void Clear()
    {
        ClearCount++;
        VertexCount = 0;
        _capacity = 0;
        Keep(ref _sparePositions, ref positions);
        Keep(ref _spareNormals, ref normals);
        Keep(ref _spareTangents, ref tangents);
        Keep(ref _spareColors, ref colors);
        Keep(ref _spareBoneBindings, ref boneBindings);
        flags = new BitArray(0);
        vertexIDs = Array.Empty<int>();

        // Reset metadata flags so arrays get properly recreated
        HasNormals = false;
        HasTangents = false;
        HasColors = false;
        HasBoneBindings = false;
        HasFlags = false;
        UVChannelCount = 0;

        for (int i = 0; i < 4; i++)
        {
            if (uvChannels[i].uv2D is { } uv)
                Keep(ref _spareUVs[i], ref uv);
            uvChannels[i] = new PhosUVArray();
        }

        foreach (var submesh in Submeshes)
            KeepIndices(submesh.Release());
        Submeshes.Clear();
        BlendShapes.Clear();
        _blendShapeByName.Clear();
        _mappedBlendShapeCount = 0;
        bones.Clear();
    }

    // UV Channel Management

    public void SetHasUV(int channel, bool state)
    {
        if (channel < 0 || channel >= 4)
            throw new ArgumentOutOfRangeException(nameof(channel));

        if (state)
        {
            if (uvChannels[channel].uv2D == null)
            {
                uvChannels[channel].uv2D = Fresh(ref _spareUVs[channel], VertexCount, float2.Zero);
            }
            UVChannelCount = System.Math.Max(UVChannelCount, channel + 1);
        }
    }

    internal void CheckUV(int channel)
    {
        if (channel < 0 || channel >= 4)
            throw new ArgumentOutOfRangeException(nameof(channel));

        if (uvChannels[channel].uv2D == null)
            SetHasUV(channel, true);
    }

    // Creates the channel when missing.
    internal float2[] GetRawUVs(int channel)
    {
        CheckUV(channel);
        return uvChannels[channel].uv2D!;
    }

    public void SetUV(int channel, int vertexIndex, float2 uv)
    {
        CheckUV(channel);
        if (vertexIndex >= 0 && vertexIndex < VertexCount)
        {
            uvChannels[channel].uv2D![vertexIndex] = uv;
        }
    }

    // Never creates the channel.
    internal PhosUVArray TryGetRawUV_Array(int channel)
    {
        if (channel < 0 || channel >= 4)
            return new PhosUVArray();
        return uvChannels[channel];
    }

    // Normals/Tangents/Colors Management

    internal void CheckNormals()
    {
        if (!HasNormals)
        {
            HasNormals = true;
            normals = Fresh(ref _spareNormals, VertexCount, float3.Zero);
        }
    }

    internal void CheckTangents()
    {
        if (!HasTangents)
        {
            HasTangents = true;
            tangents = Fresh(ref _spareTangents, VertexCount, float4.Zero);
        }
    }

    internal void CheckColors()
    {
        if (!HasColors)
        {
            HasColors = true;
            colors = Fresh(ref _spareColors, VertexCount, color.White);
        }
    }

    internal void CheckBoneBindings()
    {
        if (!HasBoneBindings)
        {
            HasBoneBindings = true;
            boneBindings = Fresh(ref _spareBoneBindings, VertexCount, new PhosBoneBinding());
        }
    }

    internal void CheckFlags()
    {
        if (!HasFlags)
        {
            HasFlags = true;
            flags = new BitArray(VertexCount);
        }
    }

    // Submesh Management

    public int IndexOfSubmesh(PhosSubmesh submesh)
    {
        return Submeshes.IndexOf(submesh);
    }

    // Bone Management

    public int BoneCount => bones.Count;

    public PhosBone GetBone(int index) => bones[index];

    public int IndexOfBone(PhosBone bone) => bones.IndexOf(bone);

    public string GetBoneName(int index) => bones[index].Name;

    public float4x4 GetBoneBindPose(int index) => bones[index].BindPose;

    // BindPose starts as identity.
    public PhosBone AddBone(string name = "Bone")
    {
        var bone = new PhosBone(name);
        bones.Add(bone);
        return bone;
    }

    // Ensure the per-vertex bone-binding array exists, sized to VertexCount. Thin public wrapper over
    // the internal CheckBoneBindings so the asset path can request bindings without reaching into
    // internals. - xlinka
    public void EnsureBoneBindings() => CheckBoneBindings();

    // Blend Shape Management

    // A delta smaller than this in every component of every channel is one the exporter wrote out for
    // completeness and nothing can ever see. Facial sources ship hundreds of them per mesh.
    public const float BlendShapeDeltaEpsilon = 0.001f;

    // BlendShapes stays a plain List because its ORDER is the public contract (renderers and drivers
    // address shapes by index). The map is a lookup cache over that list, never a second source of
    // truth: name lookup used to be a linear scan and decode does one lookup per shape, so a face mesh
    // with 756 shapes burned a quarter of a million string compares before reading a single delta.
    // Callers still append to the list directly, so a count that disagrees with the map rebuilds it.
    // -xlinka
    private readonly Dictionary<string, PhosBlendShape> _blendShapeByName = new Dictionary<string, PhosBlendShape>(StringComparer.Ordinal);
    private int _mappedBlendShapeCount = -1;

    // Rename or in-place replacement leaves the count unchanged, so it cannot be detected; say so here.
    public void InvalidateBlendShapeMap() => _mappedBlendShapeCount = -1;

    private Dictionary<string, PhosBlendShape> BlendShapeMap()
    {
        if (_mappedBlendShapeCount != BlendShapes.Count)
        {
            _blendShapeByName.Clear();
            for (int i = 0; i < BlendShapes.Count; i++)
            {
                var key = BlendShapes[i].Name ?? string.Empty;
                if (!_blendShapeByName.ContainsKey(key))
                    _blendShapeByName[key] = BlendShapes[i];
            }
            _mappedBlendShapeCount = BlendShapes.Count;
        }
        return _blendShapeByName;
    }

    // Look up without creating, which the create-on-miss GetBlendShape cannot express.
    public bool TryGetBlendShape(string name, out PhosBlendShape shape)
        => BlendShapeMap().TryGetValue(name ?? string.Empty, out shape!);

    // Frames come back unsized on purpose: the producer already holds packed delta arrays and assigns
    // them whole, so allocating VertexCount here would only be overwritten and thrown away. Use
    // PhosBlendShapeFrame.Allocate when the frame really is filled per vertex.
    public PhosBlendShape AddBlendShape(string name, int frameCount = 1)
    {
        var shape = new PhosBlendShape(name, frameCount);
        BlendShapes.Add(shape);

        if (_mappedBlendShapeCount >= 0)
        {
            var key = shape.Name ?? string.Empty;
            if (!_blendShapeByName.ContainsKey(key))
                _blendShapeByName[key] = shape;
            _mappedBlendShapeCount = BlendShapes.Count;
        }
        return shape;
    }

    // Creates the shape on a miss.
    public PhosBlendShape GetBlendShape(string name)
    {
        if (TryGetBlendShape(name, out var existing))
            return existing;
        return AddBlendShape(name, 1);
    }

    // Drop every shape whose deltas are all below the epsilon, returning how many went. Reported
    // indices are the ORIGINAL positions, ascending, because a caller remapping animation tracks needs
    // to know where a shape used to sit, not where it ended up.
    //
    // Half of a real avatar's shapes are dead weight: the incident file carried 756 attachments of
    // which 387 never moved a vertex, and every one of them was costing a dense per-vertex array in
    // memory and a full block on disk. -xlinka
    //
    // epsilon is the caller's, because only the caller knows the model's scale: a delta that is noise on a
    // metre-authored mesh is real motion on the same mesh authored in centimetres. Default keeps the old
    // absolute behaviour for callers that have no bounds to hand.
    public int StripEmptyBlendShapes(List<int>? removedIndices = null, float epsilon = BlendShapeDeltaEpsilon)
    {
        int reportStart = removedIndices?.Count ?? 0;
        int removed = 0;

        // Back to front: each removal then shifts only entries already examined.
        for (int i = BlendShapes.Count - 1; i >= 0; i--)
        {
            if (!IsBlendShapeEmpty(BlendShapes[i], epsilon))
                continue;

            BlendShapes.RemoveAt(i);
            removedIndices?.Add(i);
            removed++;
        }

        if (removed > 0)
        {
            // Duplicate names make per-entry map removal ambiguous, and the map is only a cache.
            InvalidateBlendShapeMap();
            removedIndices?.Reverse(reportStart, removedIndices.Count - reportStart);
        }
        return removed;
    }

    private static bool IsBlendShapeEmpty(PhosBlendShape shape, float epsilon)
    {
        var frames = shape?.Frames;
        if (frames == null)
            return true;

        for (int f = 0; f < frames.Length; f++)
        {
            var frame = frames[f];
            if (frame == null)
                continue;
            // Normals and tangents are unit vectors whatever the model's units are, so a scale-derived
            // epsilon does not apply to them: take whichever threshold is stricter and never let a large
            // model's positional epsilon swallow real shading motion.
            float directional = System.Math.Min(epsilon, BlendShapeDeltaEpsilon);
            if (AnyDeltaAboveEpsilon(frame.positions, epsilon)
                || AnyDeltaAboveEpsilon(frame.normals, directional)
                || AnyDeltaAboveEpsilon(frame.tangents, directional))
                return false;
        }
        return true;
    }

    private static bool AnyDeltaAboveEpsilon(float3[] deltas, float epsilon)
    {
        if (deltas == null)
            return false;

        for (int i = 0; i < deltas.Length; i++)
        {
            var d = deltas[i];
            if (System.Math.Abs(d.x) >= epsilon
                || System.Math.Abs(d.y) >= epsilon
                || System.Math.Abs(d.z) >= epsilon)
                return true;
        }
        return false;
    }

    public int BlendShapeCount => BlendShapes.Count;

    public int BlendShapeIndex(string name) => BlendShapes.FindIndex(bs => bs.Name == name);

    public bool HasBlendShape(string name) => BlendShapeMap().ContainsKey(name ?? string.Empty);

    // Utility Methods

    public BoundingBox CalculateBoundingBox()
    {
        var bounds = new BoundingBox();
        bounds.MakeEmpty();

        for (int i = 0; i < VertexCount; i++)
            bounds.Encapsulate(positions[i]);

        return bounds;
    }

    // Internal Helper Methods

    internal void EnsureArray<T>(bool condition, ref T[] array, int size, T defaultValue)
    {
        if (!condition) return;

        if (array == null || array.Length < size)
        {
            int oldLength = array?.Length ?? 0;
            Array.Resize(ref array, size);

            for (int i = oldLength; i < size; i++)
                array[i] = defaultValue;
        }
    }

    // Growth that takes a channel's spare back when it is big enough. Whatever the live array held stays where
    // it was and the rest is the default, the same as a resize gives.
    internal void EnsureArray<T>(bool condition, ref T[] array, int size, T defaultValue, ref T[] spare)
    {
        if (!condition || array.Length >= size)
            return;
        if (spare.Length >= size)
        {
            int kept = array.Length;
            var reused = spare;
            spare = Array.Empty<T>();
            if (kept > 0)
                Array.Copy(array, reused, kept);
            Array.Fill(reused, defaultValue, kept, reused.Length - kept);
            array = reused;
            return;
        }
        EnsureArray(true, ref array, size, defaultValue);
    }

    // A channel switched on with this many vertices already in place: all default, from its spare when that
    // is big enough.
    private static T[] Fresh<T>(ref T[] spare, int size, T value)
    {
        if (size == 0)
            return Array.Empty<T>();
        if (spare.Length >= size)
        {
            var reused = spare;
            spare = Array.Empty<T>();
            Array.Fill(reused, value);
            return reused;
        }
        var made = new T[size];
        if (!EqualityComparer<T>.Default.Equals(value, default!))
            Array.Fill(made, value);
        return made;
    }

    private static void Keep<T>(ref T[] spare, ref T[] live)
    {
        if (live.Length > spare.Length)
            spare = live;
        live = Array.Empty<T>();
    }

    private void KeepIndices(int[] indices)
    {
        if (indices.Length == 0)
            return;
        _spareIndices.Add(indices);
        if (_spareIndices.Count <= SpareIndexArrays)
            return;
        int smallest = 0;
        for (int i = 1; i < _spareIndices.Count; i++)
        {
            if (_spareIndices[i].Length < _spareIndices[smallest].Length)
                smallest = i;
        }
        _spareIndices.RemoveAt(smallest);
    }

    // A submesh's index room, from the smallest spare that fits when there is one. Zeroed past what it held,
    // as a new array is.
    internal void EnsureIndices(ref int[] indices, int size)
    {
        if (indices.Length >= size)
            return;
        int best = -1;
        for (int i = 0; i < _spareIndices.Count; i++)
        {
            if (_spareIndices[i].Length >= size && (best < 0 || _spareIndices[i].Length < _spareIndices[best].Length))
                best = i;
        }
        if (best < 0)
        {
            EnsureArray(true, ref indices, size, 0);
            return;
        }
        var reused = _spareIndices[best];
        _spareIndices.RemoveAt(best);
        int kept = indices.Length;
        if (kept > 0)
            Array.Copy(indices, reused, kept);
        Array.Clear(reused, kept, reused.Length - kept);
        indices = reused;
    }

    internal void RemoveElements<T>(T[] array, int index, int count, int totalCount, bool clear, T clearValue)
    {
        if (array == null || array.Length == 0) return;

        if (index + count < totalCount)
        {
            Array.Copy(array, index + count, array, index, totalCount - index - count);
        }

        if (clear)
        {
            for (int i = totalCount - count; i < totalCount; i++)
                array[i] = clearValue;
        }
    }

    internal void RemoveElements(int[] array, Action<int, int> callback)
    {
        var toRemove = new List<(int index, int count)>();

        // Group consecutive indices
        Array.Sort(array);
        int startIndex = array[0];
        int count = 1;

        for (int i = 1; i < array.Length; i++)
        {
            if (array[i] == array[i - 1] + 1)
            {
                count++;
            }
            else
            {
                toRemove.Add((startIndex, count));
                startIndex = array[i];
                count = 1;
            }
        }
        toRemove.Add((startIndex, count));

        for (int i = toRemove.Count - 1; i >= 0; i--)
        {
            callback(toRemove[i].index, toRemove[i].count);
        }
    }
}
