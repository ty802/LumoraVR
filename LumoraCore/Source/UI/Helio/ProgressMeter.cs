// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

// non-interactive progress fill: drives a child "Fill" rect's AnchorMax.x from a 0..1 value so a
// track shows progress. a Slider without the input.
public sealed class ProgressMeter : UIComponent
{
    // 0..1
    public readonly Sync<float> Progress;

    // Drives the fill rect's AnchorMax so the bar grows left-to-right. Declared member: replicates and saves.
    public readonly FieldDrive<float2> FillAnchorMaxDrive = new();

    public ProgressMeter()
    {
        Progress = new Sync<float>(this, 0f);
    }

    public override void OnStart()
    {
        base.OnStart();
        RebindVisuals();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateFill();
    }

    // Default the fill drive from the built child structure when nothing named a target. Accepts a
    // direct "Fill" child or a "Track"/"Fill" pair.
    private void RebindVisuals()
    {
        if (FillAnchorMaxDrive.ShouldApplyDefault)
        {
            var fill = Slot?.FindChild("Fill", recursive: false)?.GetComponent<RectTransform>()
                       ?? Slot?.FindChild("Track", recursive: false)?.FindChild("Fill", recursive: false)?.GetComponent<RectTransform>();
            if (fill != null)
                FillAnchorMaxDrive.DriveTarget(fill.AnchorMax);
        }
        UpdateFill();
    }

    private void UpdateFill()
    {
        if (!FillAnchorMaxDrive.IsLinkValid)
            return;

        float t = Progress.Value;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        FillAnchorMaxDrive.SetValue(new float2(t, 1f));
    }
}
