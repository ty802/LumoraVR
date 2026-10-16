// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Godot.Hooks;

// The vertices one submesh's triangles actually use, and the submesh's indices renumbered against them.
//
// A Godot surface carries its own copy of every vertex array handed to it, so building each surface from
// the whole mesh buffer stored the whole mesh once per surface: an object with six materials kept six full
// copies of its geometry on the GPU, six of every morph target, and skinned all of it six times, and a UI
// chunk with forty material batches converted and uploaded its whole buffer forty times per rebuild. Each
// surface now carries only its own vertices. A submesh that uses every vertex takes the whole buffer as it
// stands. Buffers only grow, so a mesh rebuilt every frame settles into allocating nothing. -xlinka
internal sealed class SurfaceSubset
{
    private int _vertexCount;
    private int[] _seen = Array.Empty<int>();
    private int[] _place = Array.Empty<int>();
    private int _pass;
    private int[] _vertices = Array.Empty<int>();
    private int[] _indices = Array.Empty<int>();
    private int _used;
    private int _count;

    public SurfaceSubset()
    {
    }

    public SurfaceSubset(int vertexCount) => Reset(vertexCount);

    // Valid after a Take that returned true, until the next Take.
    public Span<int> Vertices => _vertices.AsSpan(0, _used);
    public Span<int> Indices => _indices.AsSpan(0, _count);

    public void Reset(int vertexCount)
    {
        _vertexCount = vertexCount;
        if (_seen.Length < vertexCount)
        {
            _seen = new int[vertexCount];
            _place = new int[vertexCount];
            _pass = 0;
        }
    }

    // False when the whole buffer serves the submesh as it stands: it uses every vertex, or it holds an
    // index outside the mesh, which is left for the surface to refuse exactly as it always has.
    public bool Take(int[]? indices, int count)
    {
        if (indices == null || count <= 0 || count > indices.Length)
            return false;

        // A counter instead of clearing, so a mesh with many surfaces does not wipe the whole table each time.
        if (++_pass == int.MaxValue)
        {
            Array.Clear(_seen);
            _pass = 1;
        }

        Grow(ref _vertices, Math.Min(count, _vertexCount));
        int used = 0, lo = int.MaxValue, hi = -1;
        for (int i = 0; i < count; i++)
        {
            int v = indices[i];
            if ((uint)v >= (uint)_vertexCount)
                return false;
            if (_seen[v] == _pass)
                continue;
            _seen[v] = _pass;
            _vertices[used++] = v;
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }

        if (used == _vertexCount)
            return false;

        // One unbroken run, which is what a merged part always is: kept in buffer order. Anything else keeps
        // the order its triangles first reach each vertex, which is the order the GPU will fetch them in.
        if (hi - lo + 1 == used)
        {
            for (int k = 0; k < used; k++)
                _vertices[k] = lo + k;
        }

        for (int k = 0; k < used; k++)
            _place[_vertices[k]] = k;

        Grow(ref _indices, count);
        for (int i = 0; i < count; i++)
            _indices[i] = _place[indices[i]];

        _used = used;
        _count = count;
        return true;
    }

    private static void Grow(ref int[] buffer, int needed)
    {
        if (buffer.Length < needed)
            buffer = new int[Math.Max(needed, buffer.Length + buffer.Length / 2)];
    }

    public static void Gather<T>(ReadOnlySpan<T> source, ReadOnlySpan<int> vertices, Span<T> into)
    {
        for (int k = 0; k < vertices.Length; k++)
            into[k] = source[vertices[k]];
    }

    // For channels packed four to a vertex: tangents, bone indices, bone weights.
    public static void Gather4<T>(ReadOnlySpan<T> source, ReadOnlySpan<int> vertices, Span<T> into)
    {
        for (int k = 0; k < vertices.Length; k++)
        {
            int from = vertices[k] * 4, to = k * 4;
            into[to] = source[from];
            into[to + 1] = source[from + 1];
            into[to + 2] = source[from + 2];
            into[to + 3] = source[from + 3];
        }
    }

    public static T[] Gather<T>(T[] source, ReadOnlySpan<int> vertices)
    {
        var result = new T[vertices.Length];
        Gather<T>(source, vertices, result);
        return result;
    }

    public static T[] Gather4<T>(T[] source, ReadOnlySpan<int> vertices)
    {
        var result = new T[vertices.Length * 4];
        Gather4<T>(source, vertices, result);
        return result;
    }

    // A grow-only scratch buffer and the span of it that is in use, for callers that rebuild every frame.
    public static Span<T> Scratch<T>(ref T[]? buffer, int length)
    {
        if (buffer == null || buffer.Length < length)
            buffer = new T[Math.Max(length, (buffer?.Length ?? 0) + (buffer?.Length ?? 0) / 2)];
        return buffer.AsSpan(0, length);
    }
}
