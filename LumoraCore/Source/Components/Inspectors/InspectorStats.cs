// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components;

// Shared label/value row for the read-only statistics blocks that ICustomInspectorUI
// components append to their inspector section.
// Themed from the hosting panel's UI tree (ui.Root) and NOT from the component's own world
// slot: a material or asset provider generally has no UITheme above it, and Helio text with no
// resolved font renders absolutely nothing - a silently blank block rather than an error. Every
// stat block wants that same fix, so it lives here once instead of being re-derived per
// component. -xlinka
public static class InspectorStats
{
    public const float RowHeight = 24f;

    // an empty label continues the previous row's value column. returns the value text so a caller
    // that wants a figure to keep moving can hold onto it and rewrite it.
    public static Text AddRow(UIBuilder ui, string label, string value)
    {
        InspectorUI.FixedRow(ui.Root, label, RowHeight, out var rowUi, ui.Root);

        rowUi.PushStyle();
        rowUi.MinWidth(150f);
        rowUi.PreferredWidth(190f);
        rowUi.FlexibleWidth(0f);
        var labelText = rowUi.Text(label, InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(labelText.RectTransform!);
        labelText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        labelText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();

        rowUi.PushStyle();
        rowUi.FlexibleWidth(1f);
        var valueText = rowUi.Text(value, InspectorUI.FontSize - 1f, InspectorUI.TextColor);
        InspectorUI.FillParent(valueText.RectTransform!);
        valueText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        valueText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();

        return valueText;
    }

    // binary units, one decimal above KB
    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024f:0.#} KB";
        return $"{bytes / (1024f * 1024f):0.##} MB";
    }
}
