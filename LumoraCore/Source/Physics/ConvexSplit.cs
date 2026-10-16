// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Components;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Physics;

public readonly struct ConvexSplitSettings
{
    // voxels across the whole mesh; more finds thinner parts and costs time
    public int Resolution { get; init; }
    // 0..1; how far a part may dish in before it is cut again
    public float MaxConcavity { get; init; }
    // 1 tries every cutting plane; higher skips some for speed
    public int PlaneStep { get; init; }
    public int MaxHulls { get; init; }
    public int MaxPointsPerHull { get; init; }
    // fraction of the mesh's diagonal; a part smaller across than this is left out
    public float SmallestPart { get; init; }
}

// The cutting itself happens on the platform side. Called off the world thread with plain arrays, so it
// must not touch the data model.
public interface IConvexDecomposer
{
    List<float3[]> Decompose(float3[] positions, int[] indices, ConvexSplitSettings settings);
}

public sealed class ConvexSplitResult
{
    public readonly List<float3[]> Hulls = new();
    public readonly List<float> Volumes = new();
    public float MeshVolume;
    public float HullVolume;
    public int Dropped;
    public double Seconds;
    public int PointCount
    {
        get
        {
            int total = 0;
            foreach (var hull in Hulls)
                total += hull.Length;
            return total;
        }
    }
}

// Splits a mesh into several convex hulls, so a chair keeps its seat, legs and the gap between them
// instead of one hull filling the space under it. The mesh is copied on the world thread, the split
// runs on a worker, and every hull comes back solved again by our own hull solver so its points are the
// same on every machine that loads them. -xlinka
public static class ConvexSplit
{
    public static IConvexDecomposer? Backend { get; set; }

    public static bool Available => Backend != null;

    // the mesh a collider or renderer stands on, as plain arrays; false when there is none yet
    public static bool TryCapture(Component? source, out float3[] positions, out int[] indices)
    {
        positions = Array.Empty<float3>();
        indices = Array.Empty<int>();
        var mesh = source switch
        {
            MeshCollider collider => ConvexHullPointSource.GetMesh(collider.Mesh.Target),
            _ => ConvexHullPointSource.GetMesh(ConvexHullPointSource.Resolve(source)),
        };
        if (mesh == null || mesh.VertexCount == 0)
            return false;

        int vertexCount = mesh.VertexCount;
        positions = new float3[vertexCount];
        Array.Copy(mesh.RawPositions, positions, vertexCount);

        var tris = new List<int>();
        foreach (var submesh in mesh.Submeshes)
        {
            if (submesh.Topology != PhosTopology.Triangles)
                continue;
            var raw = submesh.RawIndices;
            int count = submesh.IndexCount;
            for (int i = 0; i + 2 < count; i += 3)
            {
                int a = raw[i], b = raw[i + 1], c = raw[i + 2];
                if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount || (uint)c >= (uint)vertexCount)
                    continue;
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
            }
        }
        indices = tris.ToArray();
        return indices.Length >= 3;
    }

    public static Task<ConvexSplitResult> RunAsync(float3[] positions, int[] indices, ConvexSplitSettings settings,
        CancellationToken cancel)
    {
        var backend = Backend;
        if (backend == null)
            return Task.FromException<ConvexSplitResult>(new InvalidOperationException("No convex split on this build."));
        return Task.Run(() => Run(backend, positions, indices, settings, cancel), cancel);
    }

    public static ConvexSplitResult Run(IConvexDecomposer backend, float3[] positions, int[] indices,
        ConvexSplitSettings settings, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();
        var raw = backend.Decompose(positions, indices, settings);
        cancel.ThrowIfCancellationRequested();

        var result = new ConvexSplitResult { MeshVolume = MeshVolume(positions, indices) };
        float diagonal = Diagonal(positions);
        float smallest = System.Math.Max(settings.SmallestPart, 0f) * diagonal;
        int maxPoints = System.Math.Clamp(settings.MaxPointsPerHull, 4, 255);

        var hull = new List<float3>();
        var hullIndices = new List<int>();
        foreach (var part in raw)
        {
            cancel.ThrowIfCancellationRequested();
            if (part == null || part.Length < 4)
            {
                result.Dropped++;
                continue;
            }
            if (!SolveCapped(part, maxPoints, hull, hullIndices) || Diagonal(hull) < smallest)
            {
                result.Dropped++;
                continue;
            }
            result.Hulls.Add(hull.ToArray());
            float volume = Volume(hull, hullIndices);
            result.Volumes.Add(volume);
            result.HullVolume += volume;
        }

        result.Seconds = watch.Elapsed.TotalSeconds;
        return result;
    }

    // The solved hull of points, thinned until it is no more than maxPoints. False for a part with no volume.
    public static bool SolveCapped(IReadOnlyList<float3> points, int maxPoints, List<float3> hull, List<int> hullIndices)
    {
        float cell = 0f;
        float diagonal = Diagonal(points);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            if (ConvexHullSolver.Solve(points, cell, hull, hullIndices) != ConvexHullSolver.Result.Volume)
                return false;
            if (hull.Count <= maxPoints)
                return true;
            cell = cell <= 0f ? diagonal * 0.01f : cell * 1.5f;
        }
        return hull.Count <= maxPoints;
    }

    public static float Volume(IReadOnlyList<float3> hull, IReadOnlyList<int> triangles)
    {
        double total = 0;
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            float3 a = hull[triangles[i]], b = hull[triangles[i + 1]], c = hull[triangles[i + 2]];
            total += float3.Dot(a, float3.Cross(b, c));
        }
        return (float)System.Math.Abs(total / 6.0);
    }

    // signed volume of a closed mesh; an open one gives a rough figure
    public static float MeshVolume(float3[] positions, int[] indices)
    {
        double total = 0;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            float3 a = positions[indices[i]], b = positions[indices[i + 1]], c = positions[indices[i + 2]];
            total += float3.Dot(a, float3.Cross(b, c));
        }
        return (float)System.Math.Abs(total / 6.0);
    }

    public static float Diagonal(IReadOnlyList<float3> points)
    {
        if (points.Count == 0)
            return 0f;
        float3 min = points[0], max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            float3 p = points[i];
            min = new float3(MathF.Min(min.x, p.x), MathF.Min(min.y, p.y), MathF.Min(min.z, p.z));
            max = new float3(MathF.Max(max.x, p.x), MathF.Max(max.y, p.y), MathF.Max(max.z, p.z));
        }
        return (max - min).Length;
    }
}
