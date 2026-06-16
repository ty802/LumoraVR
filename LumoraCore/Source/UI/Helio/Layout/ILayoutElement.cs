// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI.Layout;

/// <summary>
/// A UI element that contributes layout sizing constraints to its parent.
/// Implemented by both leaf renderables (Image, Text) and layout containers.
/// All size getters return null when the element has no opinion on that metric.
/// </summary>
public interface ILayoutElement
{
    float? MinWidth { get; }
    float? PreferredWidth { get; }
    float? FlexibleWidth { get; }

    float? MinHeight { get; }
    float? PreferredHeight { get; }
    float? FlexibleHeight { get; }

    /// <summary>Optional area constraint (for elements with aspect-driven sizing).</summary>
    float? Area { get; }

    /// <summary>
    /// Higher priority overrides lower-priority elements on the same RectTransform.
    /// </summary>
    int Priority { get; }

    /// <summary>Metrics that have changed since the last layout pass.</summary>
    LayoutMetric ChangedMetrics { get; }

    void PrepareCompute();
    void ClearChangedMetrics();
    void EnsureValidMetrics(LayoutDirection direction);
    LayoutMetric FilterChangedMetrics(LayoutMetric metrics);
    void LayoutRectWidthChanged();
    void LayoutRectHeightChanged();
}
