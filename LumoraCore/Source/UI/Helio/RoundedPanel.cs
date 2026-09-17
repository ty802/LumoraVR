// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

// rounded-rectangle panel with an optional border. built as a procedural mesh (corner arcs
// tessellated) like ArcSegment - no special shader, batches with the default UI material,
// hit-tests to the rounded shape.
[ComponentCategory("UI/Helio/Graphics")]
public sealed class RoundedPanel : Graphic
{
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<color> Color;
    public readonly Sync<color> OutlineColor;
    public readonly Sync<float> CornerRadius;
    public readonly Sync<float> OutlineThickness;

    private color _color;
    private color _outlineColor;
    private float _radius;
    private float _thickness;
    private IAssetProvider<MaterialAsset>? _material;

    public RoundedPanel()
    {
        Material = new AssetRef<MaterialAsset>(this);
        Color = new Sync<color>(this, color.White);
        OutlineColor = new Sync<color>(this, new color(0.45f, 0.45f, 0.45f, 1f));
        CornerRadius = new Sync<float>(this, 8f);
        OutlineThickness = new Sync<float>(this, 0f);
    }

    public override bool RequiresPreGraphicsCompute => false;

    // Trims its quads to RenderData.GeometryClipRect, so it can carry a clip window that rides the chunk.
    public override bool TrimsGeometryToClip => true;

    // The rounded rect fits inside its rect, and the outline draws on the inside of the edge. -xlinka
    public override Rect? MeasureBounds() => RectTransform?.LocalComputeRect;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkGraphicDirty();
    }

    public override void PrepareCompute()
    {
        _color = Color.Value;
        _outlineColor = OutlineColor.Value;
        _radius = CornerRadius.Value;
        _thickness = OutlineThickness.Value;
        _material = Material.Target;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null)
            return;

        var rect = rectTransform.LocalComputeRect;
        if (rect.IsEmpty)
            return;

        var clipRect = renderData.GeometryClipRect;
        if (clipRect.HasValue && !rect.Overlaps(clipRect.Value))
            return;

        var mesh = renderData.Mesh;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);

        var submesh = renderData.GetSubmesh(_material);

        float t = _thickness;
        if (t > 0f && _outlineColor.a > 0f)
        {
            var inner = Rect.FromMinMax(
                new float2(rect.xMin + t, rect.yMin + t),
                new float2(rect.xMax - t, rect.yMax - t));
            if (inner.IsEmpty)
            {
                EmitRoundedRect(mesh, submesh, rect, _radius, _outlineColor, clipRect);
                return;
            }

            // A real ring: an opaque rectangle underneath the translucent fill hides the glass,
            // wastes fill rate and can z-fight when a caller supplies a depth-writing material.
            float radius = System.Math.Clamp(_radius, 0f, MathF.Min(rect.width, rect.height) * 0.5f);
            float innerRadius = MathF.Max(radius - t, 0f);
            int segments = CornerSegments(radius);
            var outerPoints = RoundedPerimeter(rect, radius, segments);
            var innerPoints = RoundedPerimeter(inner, innerRadius, segments);
            for (int i = 0; i < outerPoints.Count; i++)
            {
                int next = (i + 1) % outerPoints.Count;
                EmitPolygon(mesh, submesh, new List<float2>(4)
                {
                    outerPoints[i], outerPoints[next], innerPoints[next], innerPoints[i]
                }, _outlineColor, clipRect);
            }
            if (_color.a > 0f)
                EmitPolygon(mesh, submesh, innerPoints, _color, clipRect);
        }
        else if (_color.a > 0f)
        {
            EmitRoundedRect(mesh, submesh, rect, _radius, _color, clipRect);
        }
    }

    public override bool IsPointInside(in float2 point)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null)
            return false;

        var rect = rectTransform.LocalComputeRect;
        if (!rect.Contains(point))
            return false;

        float r = MathF.Min(_radius, MathF.Min(rect.width, rect.height) * 0.5f);
        if (r <= 0f)
            return true;

        // Clamp the point to the inner box; in a corner region (cx,cy) is the arc center.
        float cx = point.x < rect.xMin + r ? rect.xMin + r : (point.x > rect.xMax - r ? rect.xMax - r : point.x);
        float cy = point.y < rect.yMin + r ? rect.yMin + r : (point.y > rect.yMax - r ? rect.yMax - r : point.y);
        float dx = point.x - cx;
        float dy = point.y - cy;
        return dx * dx + dy * dy <= r * r;
    }

    internal static void EmitRoundedRect(PhosMesh mesh, PhosTriangleSubmesh submesh, in Rect rect, float radius, in color tint, Rect? clipRect = null)
    {
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
        float maxR = MathF.Min(rect.width, rect.height) * 0.5f;
        if (radius > maxR) radius = maxR;
        if (radius < 0f) radius = 0f;

        if (radius <= 0.01f)
        {
            RawImage.GenerateImage(mesh, submesh, rect, Rect.UnitRect, null, false, tint, clipRect);
            return;
        }

        EmitPolygon(mesh, submesh, RoundedPerimeter(rect, radius, CornerSegments(radius)), tint, clipRect);
    }

    private static int CornerSegments(float radius) => System.Math.Clamp((int)MathF.Ceiling(radius / 4f), 4, 32);

    private static List<float2> RoundedPerimeter(in Rect rect, float radius, int perCorner)
    {
        // Perimeter points CCW, corner arcs (straight edges are spanned by the center fan).
        var pts = new List<float2>((perCorner + 1) * 4);
        AddArc(pts, rect.xMin + radius, rect.yMin + radius, radius, 180f, 270f, perCorner); // bottom-left
        AddArc(pts, rect.xMax - radius, rect.yMin + radius, radius, 270f, 360f, perCorner); // bottom-right
        AddArc(pts, rect.xMax - radius, rect.yMax - radius, radius, 0f, 90f, perCorner);    // top-right
        AddArc(pts, rect.xMin + radius, rect.yMax - radius, radius, 90f, 180f, perCorner);  // top-left
        return pts;
    }

    private static void EmitPolygon(PhosMesh mesh, PhosTriangleSubmesh submesh, List<float2> pts,
        in color tint, Rect? clipRect)
    {
        if (clipRect is Rect clip)
        {
            pts = ClipEdge(pts, 0, clip.xMin, true);
            pts = ClipEdge(pts, 0, clip.xMax, false);
            pts = ClipEdge(pts, 1, clip.yMin, true);
            pts = ClipEdge(pts, 1, clip.yMax, false);
        }

        int n = pts.Count;
        if (n < 3) return;

        var center = float2.Zero;
        foreach (var point in pts) center += point;
        center /= n;

        int c0 = mesh.VertexCount;
        mesh.IncreaseVertexCount(n + 1);
        mesh.RawPositions[c0] = new float3(center.x, center.y, 0f);
        mesh.RawColors[c0] = tint;
        mesh.SetUV(0, c0, float2.Zero);
        for (int i = 0; i < n; i++)
        {
            mesh.RawPositions[c0 + 1 + i] = new float3(pts[i].x, pts[i].y, 0f);
            mesh.RawColors[c0 + 1 + i] = tint;
            mesh.SetUV(0, c0 + 1 + i, float2.Zero);
        }

        // Match RawImage and text: CCW triangles in canvas space. AddQuadAsTriangles reverses
        // its inputs internally; AddTriangle does not. Reversing this fan would put every rounded
        // control on the back of the canvas while its text stays on the front.
        for (int i = 0; i < n; i++)
        {
            int a = c0 + 1 + i;
            int b = c0 + 1 + (i + 1) % n;
            submesh.AddTriangle(c0, a, b);
        }
    }

    // Clip the convex perimeter before triangulating so rounded fields obey nested scroll masks. -xlinka
    private static List<float2> ClipEdge(List<float2> input, int axis, float edge, bool keepGreater)
    {
        var output = new List<float2>(input.Count + 2);
        if (input.Count == 0) return output;
        var previous = input[input.Count - 1];
        float previousDistance = (axis == 0 ? previous.x : previous.y) - edge;
        bool previousInside = keepGreater ? previousDistance >= 0f : previousDistance <= 0f;
        foreach (var current in input)
        {
            float distance = (axis == 0 ? current.x : current.y) - edge;
            bool inside = keepGreater ? distance >= 0f : distance <= 0f;
            if (inside != previousInside)
            {
                float t = previousDistance / (previousDistance - distance);
                output.Add(previous + (current - previous) * t);
            }
            if (inside) output.Add(current);
            previous = current;
            previousDistance = distance;
            previousInside = inside;
        }
        return output;
    }

    private static void AddArc(List<float2> pts, float cx, float cy, float radius, float fromDeg, float toDeg, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float deg = fromDeg + (toDeg - fromDeg) * (i / (float)steps);
            float rad = deg * (MathF.PI / 180f);
            pts.Add(new float2(cx + MathF.Cos(rad) * radius, cy + MathF.Sin(rad) * radius));
        }
    }
}
