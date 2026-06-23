// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Helio.UI;

public sealed class GraphicChunkRoot : UIComputeComponent
{
    /// <summary>
    /// Render layer for overlays. 0 = normal UI. A value > 0 reserves a render-priority band
    /// ABOVE all normal chunks so a modal/overlay draws on top of everything else in the canvas
    /// (Godot caps per-surface render_priority at 127 and every normal chunk packs into the same
    /// top band, so overlapping overlays need their own reserved band). Higher level = higher band.
    /// </summary>
    public int OverlayLevel { get; set; }

    /// <summary>
    /// Render-space translation (canvas pixels) applied to this chunk's whole mesh via ChunkSlot.LocalPosition.
    /// ScrollRect sets this to scroll content by shifting the chunk instead of mutating the content rect; the
    /// canvas counter-translates the inherited clip so the viewport window stays fixed. Zero = no offset.
    /// </summary>
    public float2 RenderOffset { get; set; }

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
    }
}
