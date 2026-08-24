// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Collects the line segments a component gizmo wants drawn, in the gizmo slot's local space. Output
// is a flat vertex list read two at a time (a, b, a, b, ...) plus a parallel per-vertex color list.
//
// Line topology, not a mesh. The procedural mesh pipeline is triangles all the way down, so every
// wire shape built there is a tube - a wire box becomes twelve capped cylinders, a wire sphere three
// tori - and a collider gizmo would end up carrying more geometry than the collider it annotates.
// Emitting segments and letting the hook push them into one line-primitive surface costs one draw
// call and no mesh assets, which is also what the slot gizmo's bounds outline already does.
//
// Every emitter writes in the gizmo's own local space, so the shapes ride the slot transform and a
// moving target re-emits nothing. Rebuilds happen when a field the shape reads changes, and only
// then. -xlinka
public sealed class GizmoWireBuilder
{
    private readonly List<float3> _vertices = new();
    private readonly List<color> _colors = new();

    public IReadOnlyList<float3> Vertices => _vertices;

    public IReadOnlyList<color> Colors => _colors;

    public color Tint { get; set; } = new color(1f, 1f, 1f, 1f);

    public int Count => _vertices.Count;

    public void Clear()
    {
        _vertices.Clear();
        _colors.Clear();
    }

    public void Line(in float3 a, in float3 b)
    {
        _vertices.Add(a);
        _colors.Add(Tint);
        _vertices.Add(b);
        _colors.Add(Tint);
    }

    public void Box(in float3 center, in float3 size)
    {
        var h = size * 0.5f;
        var c000 = center + new float3(-h.x, -h.y, -h.z);
        var c100 = center + new float3(h.x, -h.y, -h.z);
        var c010 = center + new float3(-h.x, h.y, -h.z);
        var c110 = center + new float3(h.x, h.y, -h.z);
        var c001 = center + new float3(-h.x, -h.y, h.z);
        var c101 = center + new float3(h.x, -h.y, h.z);
        var c011 = center + new float3(-h.x, h.y, h.z);
        var c111 = center + new float3(h.x, h.y, h.z);

        Line(c000, c100); Line(c100, c101); Line(c101, c001); Line(c001, c000);
        Line(c010, c110); Line(c110, c111); Line(c111, c011); Line(c011, c010);
        Line(c000, c010); Line(c100, c110); Line(c101, c111); Line(c001, c011);
    }

    // bounds arrive as a min/max pair
    public void Box(in BoundingBox bounds) => Box(bounds.Center, bounds.Size);

    public void Circle(in float3 center, in float3 axis, float radius, int segments = 32)
    {
        if (radius <= 0f || segments < 3)
            return;
        Basis(axis, out var u, out var v);
        Arc(center, u, v, radius, 0f, MathF.PI * 2f, segments);
    }

    // radians; angle zero points along u
    public void Arc(in float3 center, in float3 u, in float3 v, float radius, float startAngle, float sweep,
        int segments = 32)
    {
        if (radius <= 0f || segments < 1)
            return;
        var previous = center + (u * MathF.Cos(startAngle) + v * MathF.Sin(startAngle)) * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = startAngle + sweep * (i / (float)segments);
            var point = center + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            Line(previous, point);
            previous = point;
        }
    }

    // reads as a sphere from every angle without paying for a lat/long cage
    public void Sphere(in float3 center, float radius, int segments = 32)
    {
        Circle(center, float3.Right, radius, segments);
        Circle(center, float3.Up, radius, segments);
        Circle(center, float3.Forward, radius, segments);
    }

    // along +Y
    public void Cylinder(in float3 center, float radius, float height, int segments = 24)
    {
        float half = height * 0.5f;
        var top = center + new float3(0f, half, 0f);
        var bottom = center - new float3(0f, half, 0f);
        Circle(top, float3.Up, radius, segments);
        Circle(bottom, float3.Up, radius, segments);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI * 0.5f;
            var offset = new float3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            Line(bottom + offset, top + offset);
        }
    }

    // along +Y; height is the total tip-to-tip length matching the collider field, so the straight
    // section is what's left after the two hemisphere caps
    public void Capsule(in float3 center, float radius, float height, int segments = 24)
    {
        float straight = MathF.Max(0f, height - radius * 2f) * 0.5f;
        var top = center + new float3(0f, straight, 0f);
        var bottom = center - new float3(0f, straight, 0f);

        Circle(top, float3.Up, radius, segments);
        Circle(bottom, float3.Up, radius, segments);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI * 0.5f;
            var offset = new float3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            Line(bottom + offset, top + offset);
        }

        int capSegments = System.Math.Max(4, segments / 2);
        Arc(top, float3.Right, float3.Up, radius, 0f, MathF.PI, capSegments);
        Arc(top, float3.Forward, float3.Up, radius, 0f, MathF.PI, capSegments);
        Arc(bottom, float3.Right, float3.Down, radius, 0f, MathF.PI, capSegments);
        Arc(bottom, float3.Forward, float3.Down, radius, 0f, MathF.PI, capSegments);
    }

    public void Cone(in float3 apex, in float3 direction, float baseRadius, float height, int segments = 24,
        int ribs = 4)
    {
        if (height <= 0f)
            return;
        var axis = direction.LengthSquared < 1e-10f ? float3.Up : direction.Normalized;
        var rim = apex + axis * height;
        Circle(rim, axis, baseRadius, segments);

        Basis(axis, out var u, out var v);
        int spokes = System.Math.Max(2, ribs);
        for (int i = 0; i < spokes; i++)
        {
            float angle = i * MathF.PI * 2f / spokes;
            Line(apex, rim + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * baseRadius);
        }
    }

    // XY plane, where flat content (text, quads) lives
    public void Rect(in float3 center, float width, float height)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        var a = center + new float3(-hw, -hh, 0f);
        var b = center + new float3(hw, -hh, 0f);
        var c = center + new float3(hw, hh, 0f);
        var d = center + new float3(-hw, hh, 0f);
        Line(a, b); Line(b, c); Line(c, d); Line(d, a);
    }

    // for a point that has a position but no extent
    public void Cross(in float3 center, float size)
    {
        Line(center - new float3(size, 0f, 0f), center + new float3(size, 0f, 0f));
        Line(center - new float3(0f, size, 0f), center + new float3(0f, size, 0f));
        Line(center - new float3(0f, 0f, size), center + new float3(0f, 0f, size));
    }

    public void Arrow(in float3 origin, in float3 direction, float length, float headSize)
    {
        var axis = direction.LengthSquared < 1e-10f ? float3.Up : direction.Normalized;
        var tip = origin + axis * length;
        Line(origin, tip);

        Basis(axis, out var u, out var v);
        var back = tip - axis * headSize;
        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI * 0.5f;
            Line(tip, back + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * (headSize * 0.5f));
        }
    }

    // verticalFovDegrees is the full vertical angle
    public void Frustum(in float3 origin, in float3 forward, in float3 up, in float3 right,
        float verticalFovDegrees, float aspect, float near, float far)
    {
        if (far <= near)
            return;
        float tan = MathF.Tan(verticalFovDegrees * 0.5f * MathF.PI / 180f);
        Span<float3> nearCorners = stackalloc float3[4];
        Span<float3> farCorners = stackalloc float3[4];
        PlaneCorners(origin, forward, up, right, tan * near, tan * near * aspect, near, nearCorners);
        PlaneCorners(origin, forward, up, right, tan * far, tan * far * aspect, far, farCorners);
        FrustumEdges(nearCorners, farCorners);
    }

    public void OrthoVolume(in float3 origin, in float3 forward, in float3 up, in float3 right,
        float halfHeight, float aspect, float near, float far)
    {
        if (far <= near)
            return;
        Span<float3> nearCorners = stackalloc float3[4];
        Span<float3> farCorners = stackalloc float3[4];
        PlaneCorners(origin, forward, up, right, halfHeight, halfHeight * aspect, near, nearCorners);
        PlaneCorners(origin, forward, up, right, halfHeight, halfHeight * aspect, far, farCorners);
        FrustumEdges(nearCorners, farCorners);
    }

    private void FrustumEdges(ReadOnlySpan<float3> near, ReadOnlySpan<float3> far)
    {
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) & 3;
            Line(near[i], near[next]);
            Line(far[i], far[next]);
            Line(near[i], far[i]);
        }
    }

    private static void PlaneCorners(in float3 origin, in float3 forward, in float3 up, in float3 right,
        float halfHeight, float halfWidth, float distance, Span<float3> corners)
    {
        var center = origin + forward * distance;
        corners[0] = center - right * halfWidth - up * halfHeight;
        corners[1] = center + right * halfWidth - up * halfHeight;
        corners[2] = center + right * halfWidth + up * halfHeight;
        corners[3] = center - right * halfWidth + up * halfHeight;
    }

    // Any orthonormal pair spanning the plane perpendicular to axis. The helper
    // vector is picked to be the one furthest from the axis, so the cross product never collapses.
    // LookRotation is deliberately not used near this: it returns the inverse rotation, and a circle
    // built on it goes edge-on at oblique angles. -xlinka
    private static void Basis(in float3 axis, out float3 u, out float3 v)
    {
        var n = axis.LengthSquared < 1e-10f ? float3.Up : axis.Normalized;
        var helper = MathF.Abs(n.y) < 0.9f ? float3.Up : float3.Right;
        u = float3.Cross(helper, n).Normalized;
        v = float3.Cross(n, u);
    }
}
