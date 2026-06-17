// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

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

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
    }
}
