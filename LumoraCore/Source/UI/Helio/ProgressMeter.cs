// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

/// <summary>
/// Non-interactive progress fill: drives a child "Fill" rect's AnchorMax.x from a
/// 0..1 value so a track shows progress. A Slider without the input - maps to an
/// HTML progress element.
/// </summary>
public sealed class ProgressMeter : UIComponent
{
    /// <summary>Fill amount, 0..1.</summary>
    public readonly Sync<float> Progress;

    // Drives the fill rect's AnchorMax so the bar grows left-to-right.
    public FieldDrive<float2>? FillAnchorMaxDrive { get; private set; }

    public ProgressMeter()
    {
        Progress = new Sync<float>(this, 0f);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        FillAnchorMaxDrive = new FieldDrive<float2>(World);
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

    public override void OnDestroy()
    {
        FillAnchorMaxDrive?.Release();
        FillAnchorMaxDrive = null;
        base.OnDestroy();
    }

    // Re-establish the fill drive from the built child structure (a duplicated
    // meter doesn't re-run the builder, and FieldDrive targets aren't sync
    // members). Accepts a direct "Fill" child or a "Track"/"Fill" pair.
    private void RebindVisuals()
    {
        var fill = Slot?.FindChild("Fill", recursive: false)?.GetComponent<RectTransform>()
                   ?? Slot?.FindChild("Track", recursive: false)?.FindChild("Fill", recursive: false)?.GetComponent<RectTransform>();
        if (fill != null)
            FillAnchorMaxDrive?.DriveTarget(fill.AnchorMax);
        UpdateFill();
    }

    private void UpdateFill()
    {
        if (FillAnchorMaxDrive?.IsLinkValid != true)
            return;

        float t = Progress.Value;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        FillAnchorMaxDrive.SetValue(new float2(t, 1f));
    }
}
