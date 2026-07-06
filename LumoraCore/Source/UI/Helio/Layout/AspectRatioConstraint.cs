// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI.Layout;

/// <summary>
/// Locks a rect to a width/height ratio (the CSS aspect-ratio). The Canvas applies
/// it after content-fit: one dimension is derived from the other per <see cref="Mode"/>.
/// </summary>
[SingleInstancePerSlot]
public class AspectRatioConstraint : UIComputeComponent
{
    public enum AspectMode
    {
        /// <summary>Keep width, set height = width / ratio.</summary>
        WidthControlsHeight,
        /// <summary>Keep height, set width = height * ratio.</summary>
        HeightControlsWidth,
        /// <summary>Shrink to fit inside the current box (letterbox).</summary>
        FitInParent,
        /// <summary>Grow to cover the current box (crop).</summary>
        EnvelopeParent,
    }

    /// <summary>Target ratio, width / height (e.g. 16f/9f).</summary>
    public readonly Sync<float> AspectRatio;
    public readonly Sync<AspectMode> Mode;

    public AspectRatioConstraint()
    {
        AspectRatio = new Sync<float>(this, 1f);
        Mode = new Sync<AspectMode>(this, AspectMode.WidthControlsHeight);
    }

    protected override void FlagChanges(RectTransform rect)
    {
        // Changing the ratio resizes this rect and can reflow its parent.
        rect.NotifyComponentsChanged();
    }

    public override void PrepareCompute() { }
}
