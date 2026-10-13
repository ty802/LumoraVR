// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// A label over a paragraph, for a value written in sentences (a world's description) rather than a
// setting: the row's whole width and a few lines, where the control column would cut it to one.
internal sealed class SettingsProseTemplate : SettingsRowTemplate
{
    public SettingsProseTemplate(SettingsFonts fonts) : base(fonts) { }

    protected override bool HintFromDetail => false;

    private static float ValueTop => SettingsRowFit.StackPad + SettingsRowFit.StackLabel + SettingsRowFit.StackGap;

    private static string ValueOf(ListingItem item) => item is ListingLabel label ? label.Value : item.Detail;

    private static float ValueWidth(SettingsRowFit fit) => fit.Width - SettingsMetrics.RowInset * 2f;

    // Read after measuring: the measure is what reads the line height off the font.
    private static float HeightOf(SettingsRowFit? fit, int lines)
        => ValueTop + System.Math.Max(1, lines) * (fit?.LineHeight ?? SettingsMetrics.HintHeight) + SettingsRowFit.StackPad;

    public override float HeightFor(ListingItem item)
    {
        if (Fit == null)
            return HeightOf(null, 1);
        int lines = Fit.MeasureLines(Fonts.Body, ValueOf(item), DashTheme.FontSmall, ValueWidth(Fit), SettingsRowFit.ProseLines);
        return HeightOf(Fit, lines);
    }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);
        float inset = SettingsMetrics.RowInset;
        var label = SettingsUI.Label(row, "Label", Fonts.Body, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), float2.One,
            new float2(inset, -(SettingsRowFit.StackPad + SettingsRowFit.StackLabel)),
            new float2(-inset, -SettingsRowFit.StackPad), shrinkToFit: true);
        // Sized to exactly the lines the row was measured for; the ellipsis only ever ends the last one.
        var value = SettingsUI.Label(row, "Value", Fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, float2.One,
            new float2(inset, SettingsRowFit.StackPad), new float2(-inset, -ValueTop));
        value.WordWrap.Value = Fit != null;
        value.VerticalAlignment.Value = TextVerticalAlignment.Top;
        return new ProseRow { Background = background, Label = label, Value = value, Fit = Fit };
    }

    private sealed class ProseRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Label;
        public required Text Value;
        public SettingsRowFit? Fit;

        private string? _measured;
        private float _measuredWidth;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            ListingStyle.SetText(Label, item.Label);
            ListingStyle.SetTextColor(Label, item.Interactable ? DashTheme.Text : DashTheme.TextMuted);
            string value = ValueOf(item);
            ListingStyle.SetText(Value, value);
            ListingStyle.SetTextColor(Value, item.Interactable ? DashTheme.TextDim : DashTheme.TextMuted);

            if (Fit is not { Measured: true })
                return;
            float width = ValueWidth(Fit);
            if (_measuredWidth == width && string.Equals(_measured, value, StringComparison.Ordinal))
                return;
            // The text can change while it is on screen (the host edits the description), and a row placed
            // for another line count asks for the list to be measured again.
            int lines = Fit.MeasureLines(Value.Font.Target, value, DashTheme.FontSmall, width, SettingsRowFit.ProseLines);
            _measured = Value.Font.Target?.Asset is { IsValid: true } ? value : null;
            _measuredWidth = width;
            var rect = SettingsUI.Rect(Slot);
            float placed = rect.OffsetMax.Value.y - rect.OffsetMin.Value.y;
            if (MathF.Abs(placed - HeightOf(Fit, lines)) > 0.5f)
                Fit.MarkStale();
        }

        public override void Unbind() => _measured = null;
    }
}
