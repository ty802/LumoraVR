// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// Convex hull of a point cloud. Quickhull: seed a tetrahedron from four extreme points, then keep
// pulling the point that sticks furthest out of some face onto the hull, deleting every face that
// point can see and stitching new ones across the resulting horizon.
//
// Points are held in per-face conflict lists rather than rescanned every round, so the whole cloud is
// only visited once at the start and after that the cost tracks the number of points that actually
// end up on the hull, not the number fed in. That is what makes running it over an imported mesh at
// full vertex count affordable.
//
// The faces the apex can see are found by walking neighbours out from the seed face, not by testing
// every face against the plane. Those two agree in exact arithmetic, but in floating point a face on
// the far side of the hull can pass a plane test it has no business passing, and deleting it tears a
// second hole whose horizon is a separate loop. Stitching two loops onto one apex leaves a surface
// that is no longer a closed hull, and every later visibility test is then answered against garbage.
// Walking neighbours cannot leave the region connected to the seed, so the horizon is always a single
// closed loop. Adjacency is a directed-edge map: each face owns its three edges, and the neighbour
// across an edge is whoever owns that edge reversed.
//
// Everything is measured against an epsilon scaled to the extent of the cloud itself. A fixed epsilon
// is wrong at both ends: a hull the size of a room would swallow real detail, and one the size of a
// fingernail would be called degenerate. -xlinka
public static class ConvexHullSolver
{
    public enum Result
    {
        // no points, or every point in the same place
        Empty,

        // all points on one line; a line has no surface, so nothing is emitted
        Collinear,

        // all points on one plane; the 2D hull is emitted as a single-sided fan
        Planar,

        // a closed hull with volume
        Volume
    }

    // A well-formed cloud converges long before this; the guard only exists so a pathological one
    // cannot spin the frame away.
    private const int MaxIterations = 500000;

    private sealed class Face
    {
        public int A;
        public int B;
        public int C;
        public float3 Normal;
        public float Offset;
        public bool Dead;
        public int Mark;
        public List<int>? Outside;
        public int Farthest = -1;
        public float FarthestDistance;

        public float DistanceTo(float3 p) => float3.Dot(Normal, p) - Offset;
    }

    // minPointDistance: points closer together than this are thinned to one before the hull runs.
    // Cell-based, so it thins reliably rather than guaranteeing an exact minimum separation; 0 or
    // less picks a tolerance from the size of the cloud.
    // hullIndices: triangle indices into hullPoints, wound counter-clockwise seen from outside
    // (right-hand normal points out).
    public static Result Solve(IReadOnlyList<float3> input, float minPointDistance, List<float3> hullPoints, List<int> hullIndices)
    {
        hullPoints.Clear();
        hullIndices.Clear();

        if (input == null || input.Count == 0)
            return Result.Empty;

        float3 min = input[0];
        float3 max = input[0];
        for (int i = 1; i < input.Count; i++)
        {
            float3 p = input[i];
            min = new float3(System.Math.Min(min.x, p.x), System.Math.Min(min.y, p.y), System.Math.Min(min.z, p.z));
            max = new float3(System.Math.Max(max.x, p.x), System.Math.Max(max.y, p.y), System.Math.Max(max.z, p.z));
        }

        float3 size = max - min;
        float extent = System.Math.Max(size.x, System.Math.Max(size.y, size.z));
        if (extent <= 1e-9f)
            return Result.Empty;

        float epsilon = System.Math.Max(extent * 1e-5f, 1e-9f);
        List<float3> points = Thin(input, minPointDistance > 0f ? minPointDistance : extent * 1e-6f);
        if (points.Count < 4)
            return SolvePlanarOrLess(points, epsilon, hullPoints, hullIndices);

        if (!FindInitialSimplex(points, epsilon, out int i0, out int i1, out int i2, out int i3, out Result degenerate))
        {
            return degenerate == Result.Planar
                ? SolvePlanarOrLess(points, epsilon, hullPoints, hullIndices)
                : degenerate;
        }

        var edgeOwner = new Dictionary<long, Face>(points.Count * 6);
        var faces = new List<Face>(points.Count * 2);
        AddFace(faces, edgeOwner, MakeFace(points, i0, i1, i2, i3));
        AddFace(faces, edgeOwner, MakeFace(points, i0, i1, i3, i2));
        AddFace(faces, edgeOwner, MakeFace(points, i0, i2, i3, i1));
        AddFace(faces, edgeOwner, MakeFace(points, i1, i2, i3, i0));

        var onHull = new bool[points.Count];
        onHull[i0] = onHull[i1] = onHull[i2] = onHull[i3] = true;

        // First and only full sweep of the cloud: every point outside something gets parked on one
        // face. From here on only those lists are touched.
        for (int i = 0; i < points.Count; i++)
        {
            if (!onHull[i])
                AssignToFace(points, faces, i, epsilon);
        }

        var pending = new Stack<Face>();
        foreach (var face in faces)
        {
            if (face.Farthest >= 0)
                pending.Push(face);
        }

        var visible = new List<Face>();
        var walk = new Stack<Face>();
        var orphans = new List<int>();
        var horizon = new List<long>();
        int mark = 0;
        int iterations = 0;

        while (pending.Count > 0)
        {
            if (++iterations > MaxIterations)
                break;

            Face seed = pending.Pop();
            if (seed.Dead || seed.Farthest < 0)
                continue;

            int apex = seed.Farthest;
            if (onHull[apex])
            {
                // Already stitched in by an earlier round. Re-adding it would lay a second cap over
                // the region it already closed.
                seed.Outside?.Remove(apex);
                Rescore(points, seed);
                if (seed.Farthest >= 0)
                    pending.Push(seed);
                continue;
            }

            float3 apexPoint = points[apex];

            // Flood the visible region outward from the seed across shared edges.
            mark++;
            visible.Clear();
            walk.Clear();
            seed.Mark = mark;
            walk.Push(seed);
            while (walk.Count > 0)
            {
                Face face = walk.Pop();
                visible.Add(face);
                PushVisibleNeighbour(edgeOwner, walk, face.B, face.A, apexPoint, epsilon, mark);
                PushVisibleNeighbour(edgeOwner, walk, face.C, face.B, apexPoint, epsilon, mark);
                PushVisibleNeighbour(edgeOwner, walk, face.A, face.C, apexPoint, epsilon, mark);
            }

            // An edge of a visible face whose neighbour is not visible is on the horizon, already
            // wound so the new face inherits the outward side. Collected before anything is deleted,
            // while the marks still say who was in.
            horizon.Clear();
            orphans.Clear();
            foreach (var face in visible)
            {
                CollectHorizon(edgeOwner, horizon, face.A, face.B, mark);
                CollectHorizon(edgeOwner, horizon, face.B, face.C, mark);
                CollectHorizon(edgeOwner, horizon, face.C, face.A, mark);
            }

            foreach (var face in visible)
            {
                face.Dead = true;
                RemoveEdges(edgeOwner, face);
                if (face.Outside == null)
                    continue;
                foreach (int p in face.Outside)
                {
                    if (p != apex && !onHull[p])
                        orphans.Add(p);
                }
                face.Outside.Clear();
            }

            onHull[apex] = true;
            int firstNew = faces.Count;
            foreach (long edge in horizon)
            {
                int u = (int)(edge >> 32);
                int v = (int)(edge & 0xFFFFFFFFL);
                Face? face = TryMakeFace(points, u, v, apex);
                if (face != null)
                    AddFace(faces, edgeOwner, face);
            }

            // A point that was outside a deleted face can only be outside one of the faces that
            // replaced it, so the search starts where the new ones begin.
            for (int i = 0; i < orphans.Count; i++)
                AssignToFace(points, faces, orphans[i], epsilon, firstNew);

            for (int i = firstNew; i < faces.Count; i++)
            {
                if (faces[i].Farthest >= 0)
                    pending.Push(faces[i]);
            }
        }

        // Only the points that survived onto a live face make it into the output.
        var remap = new int[points.Count];
        for (int i = 0; i < remap.Length; i++)
            remap[i] = -1;

        foreach (var face in faces)
        {
            if (face.Dead)
                continue;
            hullIndices.Add(Remap(points, remap, hullPoints, face.A));
            hullIndices.Add(Remap(points, remap, hullPoints, face.B));
            hullIndices.Add(Remap(points, remap, hullPoints, face.C));
        }

        if (hullIndices.Count == 0)
        {
            hullPoints.Clear();
            return Result.Empty;
        }

        return Result.Volume;
    }

    private static long Key(int a, int b) => ((long)a << 32) | (uint)b;

    private static void AddFace(List<Face> faces, Dictionary<long, Face> edgeOwner, Face face)
    {
        faces.Add(face);
        edgeOwner[Key(face.A, face.B)] = face;
        edgeOwner[Key(face.B, face.C)] = face;
        edgeOwner[Key(face.C, face.A)] = face;
    }

    private static void RemoveEdges(Dictionary<long, Face> edgeOwner, Face face)
    {
        // Only drop an entry this face still owns: a replacement face may already have claimed the
        // edge, and removing it then would orphan the new one.
        TryRemoveEdge(edgeOwner, Key(face.A, face.B), face);
        TryRemoveEdge(edgeOwner, Key(face.B, face.C), face);
        TryRemoveEdge(edgeOwner, Key(face.C, face.A), face);
    }

    private static void TryRemoveEdge(Dictionary<long, Face> edgeOwner, long key, Face face)
    {
        if (edgeOwner.TryGetValue(key, out Face? owner) && ReferenceEquals(owner, face))
            edgeOwner.Remove(key);
    }

    private static void PushVisibleNeighbour(Dictionary<long, Face> edgeOwner, Stack<Face> walk, int u, int v, float3 apex, float epsilon, int mark)
    {
        if (!edgeOwner.TryGetValue(Key(u, v), out Face? neighbour))
            return;
        if (neighbour.Dead || neighbour.Mark == mark)
            return;
        if (neighbour.DistanceTo(apex) <= epsilon)
            return;

        neighbour.Mark = mark;
        walk.Push(neighbour);
    }

    private static void CollectHorizon(Dictionary<long, Face> edgeOwner, List<long> horizon, int a, int b, int mark)
    {
        bool neighbourVisible = edgeOwner.TryGetValue(Key(b, a), out Face? neighbour)
            && !neighbour.Dead
            && neighbour.Mark == mark;
        if (!neighbourVisible)
            horizon.Add(Key(a, b));
    }

    private static int Remap(List<float3> points, int[] remap, List<float3> hullPoints, int index)
    {
        if (remap[index] < 0)
        {
            remap[index] = hullPoints.Count;
            hullPoints.Add(points[index]);
        }
        return remap[index];
    }

    private static Face MakeFace(List<float3> points, int a, int b, int c, int interior)
    {
        var face = new Face { A = a, B = b, C = c };
        ComputeFacePlane(points, face);
        if (face.DistanceTo(points[interior]) > 0f)
        {
            // The reference point came out on the outside, so the winding is inverted. Swap two
            // corners and re-plane.
            (face.B, face.C) = (face.C, face.B);
            ComputeFacePlane(points, face);
        }
        return face;
    }

    // Returns null only for an exactly zero-area triangle, where the apex sits on the edge itself and
    // there is no plane to speak of. Rejecting anything wider than that would leave a real hole in the
    // surface and break the walk that crosses it.
    private static Face? TryMakeFace(List<float3> points, int a, int b, int c)
    {
        float3 n = float3.Cross(points[b] - points[a], points[c] - points[a]);
        if (n.LengthSquared <= 0f)
            return null;

        var face = new Face { A = a, B = b, C = c };
        ComputeFacePlane(points, face);
        return face;
    }

    private static void ComputeFacePlane(List<float3> points, Face face)
    {
        float3 n = float3.Cross(points[face.B] - points[face.A], points[face.C] - points[face.A]).Normalized;
        face.Normal = n;
        face.Offset = float3.Dot(n, points[face.A]);
    }

    private static void AssignToFace(List<float3> points, List<Face> faces, int point, float epsilon, int startIndex = 0)
    {
        float3 p = points[point];
        Face? best = null;
        float bestDistance = epsilon;

        for (int i = startIndex; i < faces.Count; i++)
        {
            Face face = faces[i];
            if (face.Dead)
                continue;
            float d = face.DistanceTo(p);
            if (d > bestDistance)
            {
                bestDistance = d;
                best = face;
            }
        }

        if (best == null)
            return;

        (best.Outside ??= new List<int>()).Add(point);
        if (best.Farthest < 0 || bestDistance > best.FarthestDistance)
        {
            best.Farthest = point;
            best.FarthestDistance = bestDistance;
        }
    }

    private static void Rescore(List<float3> points, Face face)
    {
        face.Farthest = -1;
        face.FarthestDistance = 0f;
        if (face.Outside == null)
            return;

        foreach (int p in face.Outside)
        {
            float d = face.DistanceTo(points[p]);
            if (face.Farthest < 0 || d > face.FarthestDistance)
            {
                face.Farthest = p;
                face.FarthestDistance = d;
            }
        }
    }

    // Picks four points that actually span three dimensions: the widest pair among the six axis
    // extremes, the point furthest off that line, then the point furthest off that plane. Bailing out
    // here is how the collinear and planar cases get detected, rather than letting the main loop try
    // to build a closed hull out of a flat cloud.
    private static bool FindInitialSimplex(List<float3> points, float epsilon, out int i0, out int i1, out int i2, out int i3, out Result degenerate)
    {
        i0 = i1 = i2 = i3 = -1;
        degenerate = Result.Empty;

        Span<int> extremes = stackalloc int[6];
        for (int axis = 0; axis < 3; axis++)
        {
            int minIndex = 0;
            int maxIndex = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i][axis] < points[minIndex][axis]) minIndex = i;
                if (points[i][axis] > points[maxIndex][axis]) maxIndex = i;
            }
            extremes[axis * 2] = minIndex;
            extremes[axis * 2 + 1] = maxIndex;
        }

        float best = 0f;
        for (int a = 0; a < 6; a++)
        {
            for (int b = a + 1; b < 6; b++)
            {
                float d = (points[extremes[a]] - points[extremes[b]]).LengthSquared;
                if (d > best)
                {
                    best = d;
                    i0 = extremes[a];
                    i1 = extremes[b];
                }
            }
        }

        if (i0 < 0 || MathF.Sqrt(best) <= epsilon)
        {
            degenerate = Result.Empty;
            return false;
        }

        float3 p0 = points[i0];
        float3 lineDir = (points[i1] - p0).Normalized;
        best = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            float d = float3.Cross(points[i] - p0, lineDir).Length;
            if (d > best)
            {
                best = d;
                i2 = i;
            }
        }

        if (i2 < 0 || best <= epsilon)
        {
            degenerate = Result.Collinear;
            return false;
        }

        float3 planeNormal = float3.Cross(points[i1] - p0, points[i2] - p0).Normalized;
        best = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            float d = System.Math.Abs(float3.Dot(points[i] - p0, planeNormal));
            if (d > best)
            {
                best = d;
                i3 = i;
            }
        }

        if (i3 < 0 || best <= epsilon)
        {
            degenerate = Result.Planar;
            return false;
        }

        return true;
    }

    // Snapping to a grid of the given size keeps one point per cell, which removes exact duplicates
    // outright and thins dense clusters. Two points in adjacent cells can still end up closer than
    // the cell size; this is a cost control, not a separation guarantee.
    private static List<float3> Thin(IReadOnlyList<float3> input, float cellSize)
    {
        var seen = new HashSet<(long, long, long)>();
        var result = new List<float3>(input.Count);
        float inv = 1f / System.Math.Max(cellSize, 1e-9f);

        for (int i = 0; i < input.Count; i++)
        {
            float3 p = input[i];
            var key = (
                (long)MathF.Round(p.x * inv),
                (long)MathF.Round(p.y * inv),
                (long)MathF.Round(p.z * inv));
            if (seen.Add(key))
                result.Add(p);
        }

        return result;
    }

    // Flat or near-flat input: the 2D hull in the plane the cloud lies in, emitted as one fan. Nothing
    // here pretends to have volume, so a collider built from it is a sheet and behaves like one.
    private static Result SolvePlanarOrLess(List<float3> points, float epsilon, List<float3> hullPoints, List<int> hullIndices)
    {
        if (points.Count < 3)
            return points.Count == 0 ? Result.Empty : Result.Collinear;

        // Plane basis from the two directions with the most spread.
        float3 origin = points[0];
        float3 axisU = float3.Zero;
        float best = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            float d = (points[i] - origin).LengthSquared;
            if (d > best)
            {
                best = d;
                axisU = points[i] - origin;
            }
        }
        if (best <= epsilon * epsilon)
            return Result.Empty;
        axisU = axisU.Normalized;

        float3 normal = float3.Zero;
        best = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            float3 n = float3.Cross(axisU, points[i] - origin);
            float len = n.Length;
            if (len > best)
            {
                best = len;
                normal = n;
            }
        }
        if (best <= epsilon)
            return Result.Collinear;
        normal = normal.Normalized;
        float3 axisV = float3.Cross(normal, axisU);

        int count = points.Count;
        var order = new int[count];
        var u = new float[count];
        var v = new float[count];
        for (int i = 0; i < count; i++)
        {
            float3 rel = points[i] - origin;
            u[i] = float3.Dot(rel, axisU);
            v[i] = float3.Dot(rel, axisV);
            order[i] = i;
        }

        Array.Sort(order, (a, b) => u[a] != u[b] ? u[a].CompareTo(u[b]) : v[a].CompareTo(v[b]));

        // Monotone chain: lower hull left to right, then the upper hull back again.
        var chain = new int[count * 2];
        int k = 0;
        for (int i = 0; i < count; i++)
        {
            int p = order[i];
            while (k >= 2 && Cross2(u, v, chain[k - 2], chain[k - 1], p) <= 0f) k--;
            chain[k++] = p;
        }
        int lower = k + 1;
        for (int i = count - 2; i >= 0; i--)
        {
            int p = order[i];
            while (k >= lower && Cross2(u, v, chain[k - 2], chain[k - 1], p) <= 0f) k--;
            chain[k++] = p;
        }
        k--; // the start point gets written twice

        if (k < 3)
            return Result.Collinear;

        for (int i = 0; i < k; i++)
            hullPoints.Add(points[chain[i]]);

        for (int i = 1; i < k - 1; i++)
        {
            hullIndices.Add(0);
            hullIndices.Add(i);
            hullIndices.Add(i + 1);
        }

        return Result.Planar;
    }

    private static float Cross2(float[] u, float[] v, int a, int b, int c)
    {
        return (u[b] - u[a]) * (v[c] - v[a]) - (v[b] - v[a]) * (u[c] - u[a]);
    }
}
