// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

// Ring-segment graphic for radial menus. Angles are degrees, 0 degrees = right,
// increasing clockwise (same convention as ContextMenuPage.LayoutItems);
// radii are canvas units measured from the rect center. Hit-testing is
// polar, so interaction follows the arc shape exactly. - xlinka
public sealed class ArcSegment : Graphic
{
    public readonly Sync<float> AngleStart;
    public readonly Sync<float> ArcLength;
    public readonly Sync<float> InnerRadius;
    public readonly Sync<float> OuterRadius;
    public readonly Sync<color> Tint;

    private float _angleStart;
    private float _arcLength;
    private float _innerRadius;
    private float _outerRadius;
    private color _tint;

    public ArcSegment()
    {
        AngleStart = new Sync<float>(this, 0f);
        ArcLength = new Sync<float>(this, 90f);
        InnerRadius = new Sync<float>(this, 55f);
        OuterRadius = new Sync<float>(this, 135f);
        Tint = new Sync<color>(this, color.White);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        _angleStart = AngleStart.Value;
        _arcLength = ArcLength.Value;
        _innerRadius = InnerRadius.Value;
        _outerRadius = OuterRadius.Value;
        _tint = Tint.Value;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null || _arcLength <= 0f || _outerRadius <= _innerRadius)
            return;

        var rect = rectTransform.LocalComputeRect;
        float2 center = new float2(rect.xMin + rect.width * 0.5f, rect.yMin + rect.height * 0.5f);

        var mesh = renderData.Mesh;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);

        var submesh = renderData.GetSubmesh((IAssetProvider<MaterialAsset>?)null);

        // ~6 degrees per step keeps edges smooth without flooding the chunk.
        int steps = System.Math.Max(4, (int)MathF.Ceiling(_arcLength / 6f));
        int v0 = mesh.VertexCount;
        mesh.IncreaseVertexCount((steps + 1) * 2);

        for (int i = 0; i <= steps; i++)
        {
            float angleDeg = _angleStart + _arcLength * (i / (float)steps);
            float rad = angleDeg * (MathF.PI / 180f);
            // Clockwise convention: +angle goes down-screen.
            float2 dir = new float2(MathF.Cos(rad), -MathF.Sin(rad));

            int vi = v0 + i * 2;
            mesh.RawPositions[vi] = new float3(center.x + dir.x * _innerRadius, center.y + dir.y * _innerRadius, 0f);
            mesh.RawPositions[vi + 1] = new float3(center.x + dir.x * _outerRadius, center.y + dir.y * _outerRadius, 0f);

            mesh.RawColors[vi] = _tint;
            mesh.RawColors[vi + 1] = _tint;

            mesh.SetUV(0, vi, float2.Zero);
            mesh.SetUV(0, vi + 1, float2.Zero);
        }

        for (int i = 0; i < steps; i++)
        {
            int a = v0 + i * 2;
            // Two triangles per band quad: inner[i], outer[i], outer[i+1], inner[i+1]
            submesh.AddQuadAsTriangles(a, a + 1, a + 3, a + 2);
        }
    }

    public override bool IsPointInside(in float2 point)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null)
            return false;

        var rect = rectTransform.LocalComputeRect;
        float dx = point.x - (rect.xMin + rect.width * 0.5f);
        float dy = point.y - (rect.yMin + rect.height * 0.5f);

        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < _innerRadius || distance > _outerRadius)
            return false;

        // Invert dy to match the clockwise emission convention.
        float angle = MathF.Atan2(-dy, dx) * (180f / MathF.PI);
        float relative = angle - _angleStart;
        relative -= MathF.Floor(relative / 360f) * 360f;
        return relative <= _arcLength;
    }
}

/// <summary>
/// Button whose hit area follows an ArcSegment graphic on the same slot.
/// </summary>
public sealed class ArcButton : Button
{
    public override bool IsPointInside(in float2 point)
    {
        var arc = Slot?.GetComponent<ArcSegment>();
        return arc != null ? arc.IsPointInside(point) : base.IsPointInside(point);
    }
}
