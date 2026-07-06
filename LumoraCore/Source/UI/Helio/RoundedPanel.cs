// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

/// <summary>
/// A rounded-rectangle panel (CSS border-radius) with an optional border. Built as a
/// procedural mesh (corner arcs tessellated) like <see cref="ArcSegment"/> - no special
/// shader, batches with the default UI material, hit-tests to the rounded shape.
/// </summary>
public sealed class RoundedPanel : Graphic
{
    public readonly Sync<color> Color;
    public readonly Sync<color> OutlineColor;
    public readonly Sync<float> CornerRadius;
    public readonly Sync<float> OutlineThickness;

    private color _color;
    private color _outlineColor;
    private float _radius;
    private float _thickness;

    public RoundedPanel()
    {
        Color = new Sync<color>(this, color.White);
        OutlineColor = new Sync<color>(this, new color(0.45f, 0.45f, 0.45f, 1f));
        CornerRadius = new Sync<float>(this, 8f);
        OutlineThickness = new Sync<float>(this, 0f);
    }

    public override bool RequiresPreGraphicsCompute => false;

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

        var submesh = renderData.GetSubmesh((IAssetProvider<MaterialAsset>?)null);

        float t = _thickness;
        if (t > 0f && _outlineColor.a > 0f)
        {
            // Border = outline-colored rounded rect, fill-colored rounded rect inset on top.
            EmitRoundedRect(mesh, submesh, rect, _radius, _outlineColor);

            var inner = Rect.FromMinMax(
                new float2(rect.xMin + t, rect.yMin + t),
                new float2(rect.xMax - t, rect.yMax - t));
            if (!inner.IsEmpty)
                EmitRoundedRect(mesh, submesh, inner, MathF.Max(_radius - t, 0f), _color);
        }
        else
        {
            EmitRoundedRect(mesh, submesh, rect, _radius, _color);
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

    private static void EmitRoundedRect(PhosMesh mesh, PhosTriangleSubmesh submesh, in Rect rect, float radius, in color tint)
    {
        float maxR = MathF.Min(rect.width, rect.height) * 0.5f;
        if (radius > maxR) radius = maxR;
        if (radius < 0f) radius = 0f;

        if (radius <= 0.01f)
        {
            int q = mesh.VertexCount;
            mesh.IncreaseVertexCount(4);
            mesh.RawPositions[q] = new float3(rect.xMin, rect.yMax, 0f);
            mesh.RawPositions[q + 1] = new float3(rect.xMax, rect.yMax, 0f);
            mesh.RawPositions[q + 2] = new float3(rect.xMax, rect.yMin, 0f);
            mesh.RawPositions[q + 3] = new float3(rect.xMin, rect.yMin, 0f);
            for (int i = 0; i < 4; i++)
            {
                mesh.RawColors[q + i] = tint;
                mesh.SetUV(0, q + i, float2.Zero);
            }
            submesh.AddQuadAsTriangles(q, q + 1, q + 2, q + 3);
            return;
        }

        int perCorner = System.Math.Max(2, (int)MathF.Ceiling(radius / 4f));

        // Perimeter points CCW, corner arcs (straight edges are spanned by the center fan).
        var pts = new List<float2>((perCorner + 1) * 4);
        AddArc(pts, rect.xMin + radius, rect.yMin + radius, radius, 180f, 270f, perCorner); // bottom-left
        AddArc(pts, rect.xMax - radius, rect.yMin + radius, radius, 270f, 360f, perCorner); // bottom-right
        AddArc(pts, rect.xMax - radius, rect.yMax - radius, radius, 0f, 90f, perCorner);    // top-right
        AddArc(pts, rect.xMin + radius, rect.yMax - radius, radius, 90f, 180f, perCorner);  // top-left

        int n = pts.Count;
        if (n < 3) return;

        float ccx = rect.xMin + rect.width * 0.5f;
        float ccy = rect.yMin + rect.height * 0.5f;

        int c0 = mesh.VertexCount;
        mesh.IncreaseVertexCount(n + 1);
        mesh.RawPositions[c0] = new float3(ccx, ccy, 0f);
        mesh.RawColors[c0] = tint;
        mesh.SetUV(0, c0, float2.Zero);
        for (int i = 0; i < n; i++)
        {
            mesh.RawPositions[c0 + 1 + i] = new float3(pts[i].x, pts[i].y, 0f);
            mesh.RawColors[c0 + 1 + i] = tint;
            mesh.SetUV(0, c0 + 1 + i, float2.Zero);
        }

        // Center fan (degenerate quads since the submesh only exposes AddQuadAsTriangles).
        for (int i = 0; i < n; i++)
        {
            int a = c0 + 1 + i;
            int b = c0 + 1 + (i + 1) % n;
            submesh.AddQuadAsTriangles(c0, a, b, b);
        }
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
