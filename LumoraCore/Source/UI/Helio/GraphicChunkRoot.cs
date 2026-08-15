// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

[HideInInspector]
public sealed class GraphicChunkRoot : UIComputeComponent
{
    // 0 = normal UI. a value > 0 reserves a render-priority band ABOVE all normal chunks so a
    // modal/overlay draws on top of everything else in the canvas (Godot caps per-surface
    // render_priority at 127 and every normal chunk packs into the same top band, so overlapping
    // overlays need their own reserved band). higher level = higher band.
    public int OverlayLevel { get; set; }

    // render-space translation (canvas pixels) applied to this chunk's whole mesh via
    // ChunkSlot.LocalPosition. ScrollRect sets this to scroll content by shifting the chunk
    // instead of mutating the content rect. zero = no offset.
    public float2 RenderOffset { get; set; }

    // When true, this chunk is a ScrollRect's content: its mesh is baked ONCE and scrolled by the clip_offset uniform
    // (see RenderOffset). Because the mesh moves as a unit, its geometry is baked in FULL (nothing trimmed to
    // the viewport) and the viewport window rides on the material instead - clip_offset moves the vert and the
    // clip test together, so a fixed canvas-space window keeps clipping correctly however far it slides. Only
    // a mask that MOVES WITH the content is trimmed at bake, since a material rect can't ride a live offset
    // (see Canvas.ComputeInheritedClips). -xlinka
    public bool ScrollContent { get; set; }

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
    }
}
