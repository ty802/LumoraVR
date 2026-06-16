// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Helio.UI.Layout;

// TODO - xlinka: full multi-pass metrics solver. concrete subclasses currently only fill
// ArrangeChildren and rely on the canvas pump to call it.
public abstract class LayoutController : UIComputeComponent, ILayoutElement
{
    // simple layout hook: position children into the layout's own LocalComputeRect.
    // canvas pump calls this after parents' anchor-rects are resolved. - xlinka
    public virtual void ArrangeChildren(IReadOnlyList<RectTransform> children) { }

    // concrete layouts write these in PrepareCompute, ILayoutElement getters read them - xlinka
    protected float _minWidth;
    protected float _preferredWidth;
    protected float _flexibleWidth;
    protected float _minHeight;
    protected float _preferredHeight;
    protected float _flexibleHeight;
    protected float _area;

    public float? MinWidth => _minWidth;
    public float? PreferredWidth => _preferredWidth;
    public float? FlexibleWidth => _flexibleWidth;

    public float? MinHeight => _minHeight;
    public float? PreferredHeight => _preferredHeight;
    public float? FlexibleHeight => _flexibleHeight;

    public float? Area => _area;

    public virtual int Priority => 0;

    public LayoutMetric ChangedMetrics { get; protected set; }

    /// <summary>True if this layout must be re-run every frame even when nothing flagged a change.</summary>
    public virtual bool AlwaysRecalculateHorizontal => false;
    public virtual bool AlwaysRecalculateVertical => false;

    public virtual LayoutMetric FilterChangedMetrics(LayoutMetric metrics) => metrics;

    public virtual bool ShouldDrive(RectTransform child) => true;

    public void ClearChangedMetrics() => ChangedMetrics = LayoutMetric.None;

    public virtual void EnsureValidMetrics(LayoutDirection direction) { }
    public virtual void ChildMetricsInvalidated(LayoutDirection direction) { }
    public virtual void LayoutRectWidthChanged() { }
    public virtual void LayoutRectHeightChanged() { }

    internal virtual void ComputeLayout(LayoutDirection direction, int depth, int pass) { }

    internal virtual void UpdateComponents(List<UIComputeComponent> components)
    {
    }

    protected bool SetMetric<T>(ref T field, T value, LayoutMetric? changedMetric,
        bool invalidateHorizontal = false, bool invalidateVertical = false)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;

        if (changedMetric.HasValue)
        {
            ChangedMetrics |= changedMetric.Value;
        }

        if (invalidateHorizontal)
        {
            RectTransform?.MarkInvalidateHorizontalLayout();
        }
        if (invalidateVertical)
        {
            RectTransform?.MarkInvalidateVerticalLayout();
        }

        return true;
    }
}
