// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI.Layout;

// Resizes its slot's RectTransform to fit its CONTENT, per axis. The metrics come from the slot's own
// layout elements (a VerticalLayout sums its rows, Text reports its wrapped size, etc.) - this component
// just feeds that aggregated size back onto the rect. Without it a container only ever gets its anchor
// box, so shrink-wrap panels/lists/dropdowns/tooltips render at the wrong size and clip or strand their
// content. Off (Disabled) per axis by default, so it changes nothing unless you opt a container in. -xlinka
[SingleInstancePerSlot]
public class ContentSizeFitter : UIComputeComponent
{
    public readonly Sync<SizeFit> HorizontalFit;
    public readonly Sync<SizeFit> VerticalFit;

    public ContentSizeFitter()
    {
        HorizontalFit = new Sync<SizeFit>(this, SizeFit.Disabled);
        VerticalFit = new Sync<SizeFit>(this, SizeFit.Disabled);
    }

    protected override void FlagChanges(RectTransform rect)
    {
        // A fit-mode change can resize this rect and reflow its parent, so do a full relayout. -xlinka
        rect.NotifyComponentsChanged();
    }

    public override void PrepareCompute()
    {
    }
}
