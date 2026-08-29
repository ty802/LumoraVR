// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// The rectangle a text renderer's glyphs actually occupy, which is the thing alignment and line
// spacing move around and which is invisible until something is laid out against it.
//
// Measured from the generated mesh rather than recomputed from the font: the renderer already built
// the glyph quads, and re-deriving the box from metrics would be a second implementation of the
// layout that can disagree with the first. No pads - text extent is a RESULT, and a pad that appeared
// to drag it would be editing nothing. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(TextRenderer))]
public class TextGizmo : ComponentGizmo
{
    // Soft magenta, so text chrome is not mistaken for a collider or a camera.
    protected override color BaseTint => new(0.95f, 0.5f, 0.95f, 0.85f);

    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<TextRenderer>() is not { } text)
            return;
        fields.Add(text.Text);
        fields.Add(text.Size);
        fields.Add(text.HorizontalAlign);
        fields.Add(text.VerticalAlign);
        fields.Add(text.LineSpacing);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        if (TargetAs<TextRenderer>() is not { } text)
            return;

        var bounds = text.GetBoundingBox();
        if (bounds.Min.x > bounds.Max.x || bounds.Min.y > bounds.Max.y)
            return; // nothing generated yet

        var size = bounds.Size;
        var center = bounds.Center;

        // Flat text gets a rectangle; anything with real depth (an extruded or outlined build) gets the
        // full box, because a rectangle would hide half of it.
        if (size.z <= FlatThreshold)
            wire.Rect(new float3(center.x, center.y, center.z), size.x, size.y);
        else
            wire.Box(bounds);

        // Baseline origin tick: where the renderer's own zero sits inside the block, which is what
        // alignment is actually moving.
        wire.Cross(float3.Zero, OriginMarkerSize);
    }

    private const float FlatThreshold = 1e-4f;
    private const float OriginMarkerSize = 0.01f;
}
