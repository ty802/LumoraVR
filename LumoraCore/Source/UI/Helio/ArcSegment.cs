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
    public readonly Sync<color> OutlineColor;
    public readonly Sync<float> OutlineThickness;
    // Corner rounding in canvas units. 0 = sharp (the classic 5-band emission). Rounded corners replace
    // each of the segment's four corners with a quarter-circle fan; the outline follows the contour. -xlinka
    public readonly Sync<float> CornerRadius;

    private float _angleStart;
    private float _arcLength;
    private float _innerRadius;
    private float _outerRadius;
    private color _tint;
    private color _outlineColor;
    private float _outlineThickness;
    private float _cornerRadius;

    public ArcSegment()
    {
        AngleStart = new Sync<float>(this, 0f);
        ArcLength = new Sync<float>(this, 90f);
        InnerRadius = new Sync<float>(this, 55f);
        OuterRadius = new Sync<float>(this, 135f);
        Tint = new Sync<color>(this, color.White);
        OutlineColor = new Sync<color>(this, new color(0.45f, 0.45f, 0.45f, 1f));
        OutlineThickness = new Sync<float>(this, 3f);
        CornerRadius = new Sync<float>(this, 0f);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkGraphicDirty();
    }

    public override void PrepareCompute()
    {
        _angleStart = AngleStart.Value;
        _arcLength = ArcLength.Value;
        _innerRadius = InnerRadius.Value;
        _outerRadius = OuterRadius.Value;
        _tint = Tint.Value;
        _outlineColor = OutlineColor.Value;
        _outlineThickness = OutlineThickness.Value;
        _cornerRadius = CornerRadius.Value;
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

        float band = _outerRadius - _innerRadius;
        float thickness = MathF.Min(MathF.Max(_outlineThickness, 0f), band * 0.4f);

        // Rounded corners only make sense on a segment with ends; a full ring has no corners.
        float rc = MathF.Min(MathF.Max(_cornerRadius, 0f), band * 0.4f);
        if (rc > 0.75f && _arcLength < 359.9f)
        {
            EmitRounded(mesh, submesh, center, rc, thickness);
            return;
        }

        if (thickness <= 0f || _outlineColor.a <= 0f)
        {
            EmitBand(mesh, submesh, center, _angleStart, _arcLength, _innerRadius, _outerRadius, _tint);
            return;
        }

        // Angular width of the end caps so the outline is uniform around the
        // whole segment. Full circles have no ends, so no caps - they'd cut a
        // visible notch into the ring.
        float midRadius = (_innerRadius + _outerRadius) * 0.5f;
        float capDegrees = _arcLength >= 359.9f
            ? 0f
            : MathF.Min(thickness / MathF.Max(midRadius, 1f) * (180f / MathF.PI), _arcLength * 0.25f);

        float fillStart = _angleStart + capDegrees;
        float fillLength = MathF.Max(_arcLength - capDegrees * 2f, 0.5f);

        // Fill, then the four outline bands: inner ring, outer ring, both caps.
        EmitBand(mesh, submesh, center, fillStart, fillLength, _innerRadius + thickness, _outerRadius - thickness, _tint);
        EmitBand(mesh, submesh, center, _angleStart, _arcLength, _innerRadius, _innerRadius + thickness, _outlineColor);
        EmitBand(mesh, submesh, center, _angleStart, _arcLength, _outerRadius - thickness, _outerRadius, _outlineColor);
        EmitBand(mesh, submesh, center, _angleStart, capDegrees, _innerRadius + thickness, _outerRadius - thickness, _outlineColor);
        EmitBand(mesh, submesh, center, _angleStart + _arcLength - capDegrees, capDegrees, _innerRadius + thickness, _outerRadius - thickness, _outlineColor);
    }

    // Rounded-corner emission. The fill is the FULL rounded slab; the outline bands + corner rings draw ON
    // TOP of it in the same submesh (later indices win in draw order), so fill and outline meet with zero
    // seams and no inset-contour math. Corner circles are tangent to the radial edges, so the angular inset
    // differs per side (the tighter inner ring eats more degrees than the outer). - xlinka
    private void EmitRounded(PhosMesh mesh, PhosTriangleSubmesh submesh, in float2 center, float rc, float thickness)
    {
        const float Rad2Deg = 180f / MathF.PI;
        float t0 = _angleStart;
        float t1 = _angleStart + _arcLength;
        float innerRing = _innerRadius + rc;   // corner-center radius, inner side
        float outerRing = _outerRadius - rc;   // corner-center radius, outer side

        float aIn = rc / MathF.Max(innerRing, 1f) * Rad2Deg;
        float aOut = rc / MathF.Max(outerRing, 1f) * Rad2Deg;
        float maxInset = _arcLength * 0.45f;
        if (aIn > maxInset || aOut > maxInset)
        {
            // Not enough angular room for the requested radius on this thin a slice - shrink to fit.
            rc *= maxInset / MathF.Max(aIn, aOut);
            innerRing = _innerRadius + rc;
            outerRing = _outerRadius - rc;
            aIn = rc / MathF.Max(innerRing, 1f) * Rad2Deg;
            aOut = rc / MathF.Max(outerRing, 1f) * Rad2Deg;
        }

        // Fill: mid band full span, edge slabs angularly inset, and a FULL disc at each corner. The corner
        // circle is tangent to both edges, so the whole disc sits inside the contour by construction - full
        // discs cost a little opaque overdraw but cannot leave chord-edged holes the way quarter fans can
        // when a sweep quadrant is off (that bug shipped: chipped inner corners with the grid showing). -xlinka
        EmitBand(mesh, submesh, center, t0, _arcLength, innerRing, outerRing, _tint);
        EmitBand(mesh, submesh, center, t0 + aIn, _arcLength - aIn * 2f, _innerRadius, innerRing, _tint);
        EmitBand(mesh, submesh, center, t0 + aOut, _arcLength - aOut * 2f, outerRing, _outerRadius, _tint);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t0 + aIn, innerRing), 360f, 0f, 0f, rc, _tint);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t0 + aOut, outerRing), 360f, 0f, 0f, rc, _tint);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t1 - aIn, innerRing), 360f, 0f, 0f, rc, _tint);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t1 - aOut, outerRing), 360f, 0f, 0f, rc, _tint);

        if (thickness <= 0f || _outlineColor.a <= 0f)
            return;

        float midRadius = (_innerRadius + _outerRadius) * 0.5f;
        float capDeg = MathF.Min(thickness / MathF.Max(midRadius, 1f) * Rad2Deg, _arcLength * 0.25f);
        float rIn = MathF.Max(rc - thickness, 0f);

        // Outline: ring bands between the corners, cap bands along the radial edges, corner rings around
        // the fans - together they trace the whole rounded contour.
        EmitBand(mesh, submesh, center, t0 + aIn, _arcLength - aIn * 2f, _innerRadius, _innerRadius + thickness, _outlineColor);
        EmitBand(mesh, submesh, center, t0 + aOut, _arcLength - aOut * 2f, _outerRadius - thickness, _outerRadius, _outlineColor);
        EmitBand(mesh, submesh, center, t0, capDeg, innerRing, outerRing, _outlineColor);
        EmitBand(mesh, submesh, center, t1 - capDeg, capDeg, innerRing, outerRing, _outlineColor);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t0 + aIn, innerRing), -(t0 + aIn) + 180f, -(t0 + aIn) + 90f, rIn, rc, _outlineColor);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t0 + aOut, outerRing), -(t0 + aOut) + 90f, -(t0 + aOut), rIn, rc, _outlineColor);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t1 - aIn, innerRing), -(t1 - aIn) + 270f, -(t1 - aIn) + 180f, rIn, rc, _outlineColor);
        EmitCornerBand(mesh, submesh, CornerCenter(center, t1 - aOut, outerRing), -(t1 - aOut) + 360f, -(t1 - aOut) + 270f, rIn, rc, _outlineColor);
    }

    // Corner-circle center for the corner tangent to the radial edge at the given polar angle/ring radius.
    // Same clockwise screen convention as EmitBand: dir(theta) = (cos, -sin). - xlinka
    private static float2 CornerCenter(in float2 center, float polarDeg, float ringRadius)
    {
        float rad = polarDeg * (MathF.PI / 180f);
        return new float2(center.x + MathF.Cos(rad) * ringRadius, center.y - MathF.Sin(rad) * ringRadius);
    }

    // Annular band around an arbitrary point, marching CARTESIAN angle from -> to. Callers pass from > to
    // (decreasing) because EmitBand's clockwise-polar march also decreases in cartesian angle - same
    // rotational direction = same triangle winding, or cull_front silently eats the corners. r0 = 0
    // collapses the inner ring into a fan (fill corners). - xlinka
    private static void EmitCornerBand(
        PhosMesh mesh,
        PhosTriangleSubmesh submesh,
        in float2 c,
        float fromDeg,
        float toDeg,
        float r0,
        float r1,
        in color tint)
    {
        float sweep = fromDeg - toDeg;
        if (sweep <= 0f || r1 <= 0f)
            return;

        int steps = System.Math.Max(2, (int)MathF.Ceiling(sweep / 10f));
        int v0 = mesh.VertexCount;
        mesh.IncreaseVertexCount((steps + 1) * 2);

        for (int i = 0; i <= steps; i++)
        {
            float rad = (fromDeg - sweep * (i / (float)steps)) * (MathF.PI / 180f);
            float2 dir = new float2(MathF.Cos(rad), MathF.Sin(rad));

            int vi = v0 + i * 2;
            mesh.RawPositions[vi] = new float3(c.x + dir.x * r0, c.y + dir.y * r0, 0f);
            mesh.RawPositions[vi + 1] = new float3(c.x + dir.x * r1, c.y + dir.y * r1, 0f);

            mesh.RawColors[vi] = tint;
            mesh.RawColors[vi + 1] = tint;

            mesh.SetUV(0, vi, float2.Zero);
            mesh.SetUV(0, vi + 1, float2.Zero);
        }

        for (int i = 0; i < steps; i++)
        {
            int a = v0 + i * 2;
            submesh.AddQuadAsTriangles(a, a + 1, a + 3, a + 2);
        }
    }

    private static void EmitBand(
        PhosMesh mesh,
        PhosTriangleSubmesh submesh,
        in float2 center,
        float angleStartDeg,
        float arcLengthDeg,
        float innerRadius,
        float outerRadius,
        in color tint)
    {
        if (arcLengthDeg <= 0f || outerRadius <= innerRadius)
            return;

        // ~6 degrees per step keeps edges smooth without flooding the chunk.
        int steps = System.Math.Max(2, (int)MathF.Ceiling(arcLengthDeg / 6f));
        int v0 = mesh.VertexCount;
        mesh.IncreaseVertexCount((steps + 1) * 2);

        for (int i = 0; i <= steps; i++)
        {
            float angleDeg = angleStartDeg + arcLengthDeg * (i / (float)steps);
            float rad = angleDeg * (MathF.PI / 180f);
            // Clockwise convention: +angle goes down-screen.
            float2 dir = new float2(MathF.Cos(rad), -MathF.Sin(rad));

            int vi = v0 + i * 2;
            mesh.RawPositions[vi] = new float3(center.x + dir.x * innerRadius, center.y + dir.y * innerRadius, 0f);
            mesh.RawPositions[vi + 1] = new float3(center.x + dir.x * outerRadius, center.y + dir.y * outerRadius, 0f);

            mesh.RawColors[vi] = tint;
            mesh.RawColors[vi + 1] = tint;

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
