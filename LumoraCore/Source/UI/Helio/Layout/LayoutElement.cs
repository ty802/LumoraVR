// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

public sealed class LayoutElement : UIComputeComponent, ILayoutElement
{
    /// <summary>Outer margin (x=left, y=bottom, z=right, w=top), the CSS margin. Default zero.</summary>
    public readonly Sync<float4> Margin;

    public readonly Sync<float> MinWidth;
    public readonly Sync<float> PreferredWidth;
    public readonly Sync<float> FlexibleWidth;
    public readonly Sync<float> MinHeight;
    public readonly Sync<float> PreferredHeight;
    public readonly Sync<float> FlexibleHeight;
    public readonly Sync<float> Area;
    public readonly Sync<int> PriorityValue;
    public readonly Sync<bool> UseZeroMetrics;

    private float? _minWidth;
    private float? _preferredWidth;
    private float? _flexibleWidth;
    private float? _minHeight;
    private float? _preferredHeight;
    private float? _flexibleHeight;
    private float? _area;
    private int _priority;

    public LayoutElement()
    {
        Margin = new Sync<float4>(this, float4.Zero);
        MinWidth = new Sync<float>(this, -1f);
        PreferredWidth = new Sync<float>(this, -1f);
        FlexibleWidth = new Sync<float>(this, -1f);
        MinHeight = new Sync<float>(this, -1f);
        PreferredHeight = new Sync<float>(this, -1f);
        FlexibleHeight = new Sync<float>(this, -1f);
        Area = new Sync<float>(this, -1f);
        PriorityValue = new Sync<int>(this, 1);
        UseZeroMetrics = new Sync<bool>(this, false);
    }

    float? ILayoutElement.MinWidth => _minWidth;
    float? ILayoutElement.PreferredWidth => _preferredWidth;
    float? ILayoutElement.FlexibleWidth => _flexibleWidth;
    float? ILayoutElement.MinHeight => _minHeight;
    float? ILayoutElement.PreferredHeight => _preferredHeight;
    float? ILayoutElement.FlexibleHeight => _flexibleHeight;
    float? ILayoutElement.Area => _area;
    int ILayoutElement.Priority => _priority;

    public LayoutMetric ChangedMetrics { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        // PER-AXIS routing: a leaf element's width metrics are independent of its height metrics, so a width
        // change invalidates only the HORIZONTAL axis and a height change only the VERTICAL - the measure pass
        // then reuses the unaffected axis's cached metric. Area/Priority/UseZeroMetrics can affect either, so
        // they invalidate both. These per-Sync handlers replace the catch-all FlagChanges (a no-op below);
        // routing through FlagChanges would collapse every change back to both axes and defeat the skip. -xlinka
        MinWidth.OnChanged += _ => RectTransform?.MarkInvalidateHorizontalLayout();
        PreferredWidth.OnChanged += _ => RectTransform?.MarkInvalidateHorizontalLayout();
        FlexibleWidth.OnChanged += _ => RectTransform?.MarkInvalidateHorizontalLayout();
        MinHeight.OnChanged += _ => RectTransform?.MarkInvalidateVerticalLayout();
        PreferredHeight.OnChanged += _ => RectTransform?.MarkInvalidateVerticalLayout();
        FlexibleHeight.OnChanged += _ => RectTransform?.MarkInvalidateVerticalLayout();
        Area.OnChanged += _ => RectTransform?.MarkChangeDirty();
        PriorityValue.OnChanged += _ => RectTransform?.MarkChangeDirty();
        UseZeroMetrics.OnChanged += _ => RectTransform?.MarkChangeDirty();
        // Margin shifts/sizes the element on BOTH axes, so invalidate both.
        Margin.OnChanged += _ => RectTransform?.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        PrepareValue(ref _minWidth, MinWidth.Value, LayoutMetric.MinWidth);
        PrepareValue(ref _preferredWidth, PreferredWidth.Value, LayoutMetric.PreferredWidth);
        PrepareValue(ref _flexibleWidth, FlexibleWidth.Value, LayoutMetric.FlexibleWidth);
        PrepareValue(ref _minHeight, MinHeight.Value, LayoutMetric.MinHeight);
        PrepareValue(ref _preferredHeight, PreferredHeight.Value, LayoutMetric.PreferredHeight);
        PrepareValue(ref _flexibleHeight, FlexibleHeight.Value, LayoutMetric.FlexibleHeight);
        PrepareValue(ref _area, Area.Value, LayoutMetric.Area);
        _priority = PriorityValue.Value;
    }

    public void ClearChangedMetrics()
    {
        ChangedMetrics = LayoutMetric.None;
    }

    public void EnsureValidMetrics(LayoutDirection direction) { }

    public LayoutMetric FilterChangedMetrics(LayoutMetric metrics)
    {
        return metrics & ~OverriddenMetrics;
    }

    public void LayoutRectWidthChanged() { }
    public void LayoutRectHeightChanged() { }

    protected override void FlagChanges(RectTransform rect)
    {
        // Per-axis routing is handled by the per-Sync OnChanged handlers wired in OnAwake (width->H,
        // height->V, Area/Priority/UseZeroMetrics->both). Keeping this a no-op stops the catch-all from
        // collapsing every metric change back to both axes, which is what enables the per-axis measure skip.
        // Structural enable/disable/attach still routes through NotifyComponentsChanged (both axes). -xlinka
    }

    private LayoutMetric OverriddenMetrics
    {
        get
        {
            LayoutMetric metrics = LayoutMetric.None;
            if (_minWidth.HasValue) metrics |= LayoutMetric.MinWidth;
            if (_preferredWidth.HasValue) metrics |= LayoutMetric.PreferredWidth;
            if (_flexibleWidth.HasValue) metrics |= LayoutMetric.FlexibleWidth;
            if (_minHeight.HasValue) metrics |= LayoutMetric.MinHeight;
            if (_preferredHeight.HasValue) metrics |= LayoutMetric.PreferredHeight;
            if (_flexibleHeight.HasValue) metrics |= LayoutMetric.FlexibleHeight;
            if (_area.HasValue) metrics |= LayoutMetric.Area;
            return metrics;
        }
    }

    private void PrepareValue(ref float? field, float value, LayoutMetric metric)
    {
        float? next;
        if (UseZeroMetrics.Value)
        {
            next = value < 0f ? null : value;
        }
        else
        {
            next = value <= 0f ? null : value;
        }

        if (field != next)
        {
            field = next;
            ChangedMetrics |= metric;
        }
    }
}
