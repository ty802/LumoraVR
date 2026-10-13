// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Assets;
using Lumora.Core.Input;
using Lumora.Core.Input.Actions;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// How the rows of one list lay out at the width the list has right now. The screen that owns the list
// feeds it the list's width every frame; the templates read it for each row's height and geometry, and a
// width change is the screen's cue to Refresh the list so every row is measured again. Below StackBelow, or
// with a label column under LabelMin, a label beside its control leaves one of them too narrow, so the
// label goes above the control. -xlinka
internal sealed class SettingsRowFit
{
    public const float StackBelow = 380f;
    public const float StackPad = 8f;
    public const float StackLabel = 22f;
    public const float StackGap = 4f;
    public const float SidePad = 8f;
    // A hint wraps to this many lines and a note or a prose value to ProseLines; the last line kept ends
    // in an ellipsis.
    public const int HintLines = 3;
    public const int ProseLines = 4;
    // A label column narrower than this wraps its hint every few words, so the label goes above instead.
    public const float LabelMin = 150f;

    private static readonly TextShaper Shaper = new();

    private bool _stale;
    private IAssetProvider<FontSet>? _awaitedFont;

    public float Width { get; private set; }
    public bool Measured => Width > 0f;
    public bool Stacked => Measured
        && (Width < StackBelow || Width * SettingsMetrics.LabelSplit - SettingsMetrics.RowInset - SettingsMetrics.LabelGutter < LabelMin);

    // Height of one line of small text, read off the font the last time something was measured with it.
    public float LineHeight { get; private set; } = SettingsMetrics.HintHeight;

    // Room the control column has: beside the label at the shared label share, or the row less its insets.
    public float ControlWidth => Stacked
        ? Width - SettingsMetrics.RowInset * 2f
        : Width * (1f - SettingsMetrics.LabelSplit) - SettingsMetrics.RowInset;

    // Room the label column has, which is the width a hint wraps at.
    public float LabelWidth(float labelSplit, float labelRightInset) => Stacked
        ? Width - SettingsMetrics.RowInset * 2f
        : Width * labelSplit - SettingsMetrics.RowInset - labelRightInset;

    // One line keeps the band every row had before hints wrapped, so a short hint moves nothing.
    public float HintBand(int lines) => lines <= 0 ? 0f : lines == 1 ? SettingsMetrics.HintHeight : lines * LineHeight;

    public float LabelBand(int hintLines) => StackLabel + HintBand(hintLines);

    public float RowHeight(int hintLines, float controlHeight)
        => Stacked
            ? StackPad + LabelBand(hintLines) + StackGap + controlHeight + StackPad
            : MathF.Max(MathF.Max(SettingsMetrics.RowHeight, controlHeight + SidePad * 2f),
                SettingsMetrics.RowHeight - SettingsMetrics.HintHeight + HintBand(hintLines));

    // Lines text takes wrapped at this width, shaped exactly as the Text drawing it shapes it, capped at
    // max. Shaping reads the font server, so this runs where HeightFor and Bind run, on the main thread.
    // A font that has not landed yet counts one line, and its landing measures the rows again.
    public int MeasureLines(IAssetProvider<FontSet>? font, string text, float size, float width, int max)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var set = font?.Asset;
        if (set == null || !set.IsValid)
        {
            _awaitedFont = font;
            return 1;
        }
        if (width <= 0f)
            return 1;
        TextShaper.RequestGlyphs(set, text, size);
        Shaper.Shape(set, text, size, true, width);
        float line = set.GetLineHeight(size);
        if (size == DashTheme.FontSmall && line > 0f)
            LineHeight = line;
        return System.Math.Clamp(Shaper.Lines.Count, 1, max);
    }

    // A row whose height no longer matches what it shows (a strip whose options arrived late) asks for
    // the list to be measured again.
    public void MarkStale() => _stale = true;

    // True when the rows need measuring again.
    public bool Update(float width)
    {
        width = MathF.Round(width);
        bool changed = width > 0f && width != Width;
        if (changed)
            Width = width;
        if (_awaitedFont != null && _awaitedFont.Asset is { IsValid: true })
        {
            _awaitedFont = null;
            _stale = true;
        }
        if (!changed && !_stale)
            return false;
        _stale = false;
        return true;
    }
}

// The parts every settings row shares: one rounded surface, a label column that can carry a second
// quieter line, and an empty control column. Templates fill the control column and never touch the
// geometry, so a toggle, a slider and a pill strip all line up down the page.
internal sealed class SettingsRowShell
{
    public required RoundedPanel Background;
    public required Text LabelText;
    public required RectTransform LabelRect;
    public required Text HintText;
    public required Slot HintSlot;
    public required RectTransform HintRect;
    public required Slot Control;
    public required RectTransform LabelsRect;
    public required RectTransform ControlRect;
    public Button? Click;
    public SettingsRowFit? Fit;
    public float LabelSplit;
    public float LabelRightInset;
    public float ControlLeftInset;
    public float ControlRightInset;
    // Stacked, the control sits under the label this wide against the right edge; 0 spans the row.
    public float StackedControlWidth;

    private bool _hovered;
    private bool _interactable = true;

    // The hint's line count, kept against the text and width it was measured for: a bind ten times a
    // second must not reshape a hint that has not changed.
    private ListingItem? _linesFor;
    private string? _linesText;
    private float _linesWidth;
    private int _lines;
    private bool _linesSure;

    public void SetHovered(bool value)
    {
        if (_hovered == value)
            return;
        _hovered = value;
        Repaint();
    }

    // useDetailAsHint is off for rows whose Detail IS the value (the read-only status rows), on for the
    // ones where it is a second line under the label.
    public void Bind(ListingItem item, bool useDetailAsHint)
    {
        _interactable = item.Interactable;
        ListingStyle.SetText(LabelText, item.Label);
        ListingStyle.SetTextColor(LabelText, item.Interactable ? DashTheme.Text : DashTheme.TextMuted);

        bool hasHint = useDetailAsHint && !item.DetailText.IsEmpty;
        ListingStyle.SetActive(HintSlot, hasHint);
        int lines = 0;
        if (hasHint)
        {
            string hint = item.Detail;
            ListingStyle.SetText(HintText, hint);
            ListingStyle.SetTextColor(HintText, item.Interactable ? DashTheme.TextDim : DashTheme.TextMuted);
            lines = HintLines(item, hint);
        }
        else
        {
            _linesFor = null;
        }
        // The label sits centred in whatever is left above the hint lines, so a row with no hint centres
        // in the whole row and the two kinds still share a baseline grid.
        float band = Fit?.HintBand(lines) ?? (hasHint ? SettingsMetrics.HintHeight : 0f);
        SettingsUI.SetOffsets(HintRect, new float2(0f, 1f), new float2(0f, band + 1f));
        SettingsUI.SetOffsets(LabelRect, new float2(0f, band), float2.Zero);
        Arrange(lines);

        ListingStyle.SetInteractable(Click, item.Interactable);
        Repaint();
    }

    // The template measured this hint for the row's height. One that changed under the same item since (a
    // live hint following its answer) and now takes another line count asks for the list to be measured
    // again, or the lines past the row's height would be cut off.
    private int HintLines(ListingItem item, string hint)
    {
        if (Fit is not { Measured: true })
            return 1;
        float width = Fit.LabelWidth(LabelSplit, LabelRightInset);
        if (_linesSure && ReferenceEquals(_linesFor, item) && _linesWidth == width
            && string.Equals(_linesText, hint, StringComparison.Ordinal))
            return _lines;

        int lines = Fit.MeasureLines(HintText.Font.Target, hint, DashTheme.FontSmall, width, SettingsRowFit.HintLines);
        if (ReferenceEquals(_linesFor, item) && _linesWidth == width && lines != _lines)
            Fit.MarkStale();
        _linesFor = item;
        _linesText = hint;
        _linesWidth = width;
        _lines = lines;
        _linesSure = HintText.Font.Target?.Asset is { IsValid: true };
        return lines;
    }

    // Beside the label, or under it when the list is too narrow for both on one line.
    private void Arrange(int hintLines)
    {
        if (Fit is not { Stacked: true })
        {
            SettingsUI.SetAnchors(LabelsRect, float2.Zero, new float2(LabelSplit, 1f));
            SettingsUI.SetOffsets(LabelsRect, new float2(SettingsMetrics.RowInset, 0f), new float2(-LabelRightInset, 0f));
            SettingsUI.SetAnchors(ControlRect, new float2(LabelSplit, 0f), float2.One);
            SettingsUI.SetOffsets(ControlRect, new float2(ControlLeftInset, 0f), new float2(-ControlRightInset, 0f));
            return;
        }
        float inset = SettingsMetrics.RowInset;
        float labelBottom = SettingsRowFit.StackPad + Fit!.LabelBand(hintLines);
        float controlTop = labelBottom + SettingsRowFit.StackGap;
        SettingsUI.SetAnchors(LabelsRect, new float2(0f, 1f), float2.One);
        SettingsUI.SetOffsets(LabelsRect, new float2(inset, -labelBottom), new float2(-inset, -SettingsRowFit.StackPad));
        if (StackedControlWidth > 0f)
        {
            SettingsUI.SetAnchors(ControlRect, new float2(1f, 0f), float2.One);
            SettingsUI.SetOffsets(ControlRect, new float2(-(inset + StackedControlWidth), SettingsRowFit.StackPad),
                new float2(-inset, -controlTop));
        }
        else
        {
            SettingsUI.SetAnchors(ControlRect, float2.Zero, float2.One);
            SettingsUI.SetOffsets(ControlRect, new float2(inset, SettingsRowFit.StackPad), new float2(-inset, -controlTop));
        }
    }

    public void Repaint()
    {
        bool lit = Click != null && _hovered && _interactable;
        SettingsUI.SetPaint(Background, lit ? DashTheme.SurfaceHover : DashTheme.Surface, DashTheme.Outline);
    }
}

internal abstract class SettingsRowTemplate : ListingRowTemplate
{
    protected readonly SettingsFonts Fonts;

    protected SettingsRowTemplate(SettingsFonts fonts) => Fonts = fonts;

    public override float Height => SettingsMetrics.RowHeight;

    // Set by a list that can be narrow enough to stack its rows; without one every row keeps one line.
    public SettingsRowFit? Fit { get; init; }

    // Whether the item's Detail shows as a second line under the label, which a stacked row makes room for.
    protected virtual bool HintFromDetail => true;

    protected virtual float ControlHeight(ListingItem item) => SettingsMetrics.ControlBand;

    // For a row whose hint is worked out at bind time, so the height is measured for the hint it shows.
    protected virtual void PrepareHint(ListingItem item) { }

    public override float HeightFor(ListingItem item)
    {
        if (Fit == null)
            return Height;
        int lines = 0;
        if (HintFromDetail)
        {
            PrepareHint(item);
            if (!item.DetailText.IsEmpty)
                lines = Fit.MeasureLines(Fonts.Body, item.Detail, DashTheme.FontSmall,
                    Fit.LabelWidth(SettingsMetrics.LabelSplit, SettingsMetrics.LabelGutter), SettingsRowFit.HintLines);
        }
        return Fit.RowHeight(lines, ControlHeight(item));
    }

    // The view's own row background is the nine-slice sprite the rest of the dash was built from. These
    // rows paint a RoundedPanel instead so the corner radius and the hairline outline come from the
    // theme, and so hover can repaint without the view's rebind overwriting it every tick. -xlinka
    public override bool UsesRowBackground => false;

    // No layout controller. Columns are anchored fractions of the row, which a LayoutElement cannot
    // express, and a controller would fight the free-anchored placement the virtual list does anyway.
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    protected SettingsRowShell BuildShell(Slot row, bool clickable)
        => BuildShell(row, clickable, SettingsMetrics.LabelSplit, SettingsMetrics.LabelGutter,
            0f, SettingsMetrics.RowInset);

    protected SettingsRowShell BuildShell(Slot row, bool clickable, float labelSplit, float labelRightInset,
        float controlLeftInset, float controlRightInset)
    {
        // RoundedPanel, not Image: Button.OnAttach adopts an Image on its own slot into a color driver,
        // and a driven tint can no longer be written from Bind.
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var labels = SettingsUI.Child(row, "Labels", float2.Zero, new float2(labelSplit, 1f),
            new float2(SettingsMetrics.RowInset, 0f), new float2(-labelRightInset, 0f));

        var label = SettingsUI.Label(labels, "Label", Fonts.Body, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, float2.One, float2.Zero, float2.Zero, shrinkToFit: true);

        // With a fit the hint wraps at its full size, the size its row was measured at; shrinking it as
        // well would draw something other than what was measured. Without one the row has a single line.
        var hint = SettingsUI.Label(labels, "Hint", Fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 0f),
            new float2(0f, 1f), new float2(0f, SettingsMetrics.HintHeight + 1f), shrinkToFit: Fit == null);
        hint.WordWrap.Value = Fit != null;
        hint.Slot.ActiveSelf.Value = false;

        var control = SettingsUI.Child(row, "Control", new float2(labelSplit, 0f), float2.One,
            new float2(controlLeftInset, 0f), new float2(-controlRightInset, 0f));

        var shell = new SettingsRowShell
        {
            Background = background,
            LabelText = label,
            LabelRect = SettingsUI.Rect(label.Slot),
            HintText = hint,
            HintSlot = hint.Slot,
            HintRect = SettingsUI.Rect(hint.Slot),
            Control = control,
            LabelsRect = SettingsUI.Rect(labels),
            ControlRect = SettingsUI.Rect(control),
            Fit = Fit,
            LabelSplit = labelSplit,
            LabelRightInset = labelRightInset,
            ControlLeftInset = controlLeftInset,
            ControlRightInset = controlRightInset,
        };

        if (clickable)
        {
            var button = row.AttachComponent<Button>();
            button.HoverEntered += _ => shell.SetHovered(true);
            button.HoverExited += _ => shell.SetHovered(false);
            shell.Click = button;
        }
        return shell;
    }
}

// SECTION

internal sealed class SettingsSectionTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;

    public SettingsSectionTemplate(SettingsFonts fonts) => _fonts = fonts;

    public override float Height => SettingsMetrics.SectionHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var text = SettingsUI.Label(row, "Section", _fonts.Medium, DashTheme.FontLabel, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(1f, 0f),
            new float2(SettingsMetrics.RowInset, 6f), new float2(-SettingsMetrics.RowInset, 23f));
        SettingsUI.Divider(row, "Rule");
        return new SectionRow { Text = text };
    }

    private sealed class SectionRow : ListingRow
    {
        public required Text Text;

        private string _resolved = string.Empty;
        private string _upper = string.Empty;

        // Uppercasing allocates and this rebinds ten times a second, so cache against the resolved label.
        // A language switch still gets a fresh cap-up; a quiet tick costs one string compare. -xlinka
        public override void Bind(ListingItem item)
        {
            string resolved = item.Label;
            if (!string.Equals(resolved, _resolved, StringComparison.Ordinal))
            {
                _resolved = resolved;
                _upper = resolved.ToUpperInvariant();
            }
            ListingStyle.SetText(Text, _upper);
        }
    }
}

// TOGGLE

internal sealed class SettingsToggleTemplate : SettingsRowTemplate
{
    public SettingsToggleTemplate(SettingsFonts fonts) : base(fonts) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: true);
        var toggle = ToggleSwitch.Build(shell.Control, Fonts.Body);
        var listingRow = new ToggleRow { Shell = shell, Switch = toggle };

        // Reads the row's CURRENT item, never the one it was first built for: this instance is recycled
        // across the whole list.
        shell.Click!.Clicked += (_, _) =>
        {
            if (listingRow.Item is not ListingToggle item || !item.Interactable)
                return;
            item.Write(!item.Read());
            // The write can refuse or clamp; rebinding shows what the setting actually holds.
            listingRow.Bind(item);
        };
        return listingRow;
    }

    private sealed class ToggleRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required ToggleSwitch Switch;

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);
            Switch.SetValue(item is ListingToggle toggle && toggle.Read(), item.Interactable);
        }
    }
}

// SLIDER

internal sealed class SettingsSliderTemplate : SettingsRowTemplate
{
    public SettingsSliderTemplate(SettingsFonts fonts) : base(fonts) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);

        var value = SettingsUI.Label(shell.Control, "Value", Fonts.Body, DashTheme.FontBody, DashTheme.TextDim,
            TextHorizontalAlignment.Right, new float2(1f, 0f), new float2(1f, 1f),
            new float2(-SettingsMetrics.ValueWidth, 0f), float2.Zero);

        var host = SettingsUI.Band(shell.Control, "Bar", SettingsMetrics.SliderBand,
            0f, SettingsMetrics.ValueWidth + SettingsMetrics.ValueGap);

        builder.PushStyle();
        builder.ForegroundColor(DashTheme.Accent);
        builder.NestInto(host);
        var slider = builder.Slider(0f, 0f, 1f, null, DashTheme.Field);
        builder.NestOut();
        builder.PopStyle();

        // Slider() sizes itself for a horizontal layout cell; this row anchors instead, so pin it.
        var rect = SettingsUI.Rect(slider.Slot);
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;

        var track = slider.Slot.FindChild("Track", recursive: false);
        var fill = track?.FindChild("Fill", recursive: false)?.GetComponent<Image>();
        var handle = slider.Slot.FindChild("HandleArea", recursive: false)
            ?.FindChild("Handle", recursive: false)?.GetComponent<ArcSegment>();
        // The builder paints the handle in the same accent as the fill, which loses the handle against a
        // filled track. Reconfigure the driver that already owns the tint rather than stacking a second
        // one behind it, which would sit there inert.
        if (handle != null)
            slider.AddColorDriver(handle.Tint, DashTheme.OnAccent);

        var listingRow = new SliderRow { Shell = shell, Bar = slider, Value = value, Fill = fill };
        slider.ValueChanged += (_, raw) =>
        {
            if (listingRow.Item is not ListingSlider item || !item.Interactable)
                return;
            float snapped = item.Snap(raw);
            item.Write(snapped);
            float applied = item.Read();
            listingRow.ShowValue(item, applied);
            // The write can clamp or bucket; put the handle where the setting landed, not where the
            // finger did.
            if (applied != raw)
            {
                slider.Value.Value = applied;
                slider.UpdateHandleDrives();
            }
        };
        return listingRow;
    }

    private sealed class SliderRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required Slider Bar;
        public required Text Value;
        public Image? Fill;

        private ListingItem? _shownFor;
        private float _shown;
        private int _shownUnits;

        public void ShowValue(ListingSlider item, float value)
        {
            _shownFor = item;
            _shown = value;
            ListingStyle.SetText(Value, item.Describe(value));
        }

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);
            if (item is not ListingSlider slider)
            {
                ListingStyle.SetText(Value, item.Detail);
                return;
            }

            float current = slider.Read();
            ListingStyle.SetSlider(Bar, slider.Min, slider.Max, current);
            ListingStyle.SetInteractable(Bar, item.Interactable);
            ListingStyle.SetTextColor(Value, item.Interactable ? DashTheme.TextDim : DashTheme.TextMuted);
            if (Fill != null)
                SettingsUI.SetTint(Fill, item.Interactable ? DashTheme.Accent : DashTheme.TextMuted);

            // Formatting a value that has not moved is a string allocation per row per tick for text the
            // equality gate then throws away. Keyed on the item too: a recycled row can land on a
            // different setting holding the same number. And on the units, which rewrite a length's text
            // without moving its value. -xlinka
            if (!ReferenceEquals(_shownFor, item) || _shown != current || _shownUnits != EngineSettings.UnitsVersion)
            {
                _shownUnits = EngineSettings.UnitsVersion;
                ShowValue(slider, current);
            }
        }

        public override void Unbind() => _shownFor = null;
    }
}

// SEGMENTED CHOICE

internal sealed class SettingsChoiceTemplate : SettingsRowTemplate
{
    // Segments are built on demand and never destroyed, only hidden: a recycled row lands on items with
    // different option counts and rebuilding the strip each time would churn slots inside a live chunk
    // on every scroll step.
    private const int InitialSegments = 3;

    public SettingsChoiceTemplate(SettingsFonts fonts) : base(fonts) { }

    protected override float ControlHeight(ListingItem item)
        => SettingsSegments.BlockHeight(Fit, (item as ListingChoice)?.Options.Count ?? 0);

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var strip = SettingsSegments.Strip(shell.Control);
        var listingRow = new ChoiceRow { Shell = shell, Segments = new SettingsSegments(strip, Fonts), Fit = Fit };
        for (int i = 0; i < InitialSegments; i++)
            listingRow.Segments.Add(index => listingRow.Pick(index));
        return listingRow;
    }

    private sealed class ChoiceRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsSegments Segments;
        public SettingsRowFit? Fit;

        public void Pick(int index)
        {
            if (Item is not ListingChoice choice || !choice.Interactable)
                return;
            if (index < 0 || index >= choice.Options.Count)
                return;
            choice.Write(index);
            Bind(choice);
        }

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);
            var choice = item as ListingChoice;
            int count = choice?.Options.Count ?? 0;
            Segments.Grow(count, index => Pick(index));

            int selected = choice?.Read() ?? -1;
            Segments.Layout(Fit, count);
            for (int i = 0; i < count; i++)
                Segments.Paint(i, choice!.Options[i], i == selected, item.Interactable);
        }
    }
}

// A row of pills that divide their strip evenly. Shared by the enum choices and the locomotion picker
// so both pick up the same active/hover/disabled treatment.
internal sealed class SettingsSegments
{
    // The strip stops short of the control column's right edge: pills that ran the full width came out
    // as two enormous slabs on a two-option setting.
    private const float StripSpan = 0.66f;
    // A pill narrower than this cannot hold its word, so a strip with more options than fit at this width
    // wraps onto more lines instead of squeezing every pill.
    public const float PillMin = 72f;

    private readonly Slot _strip;
    private readonly SettingsFonts _fonts;
    private readonly List<SettingsChip> _chips = new();

    public SettingsSegments(Slot strip, SettingsFonts fonts)
    {
        _strip = strip;
        _fonts = fonts;
    }

    public static Slot Strip(Slot control)
        => SettingsUI.Child(control, "Segments", new float2(0f, 0.5f), new float2(StripSpan, 0.5f),
            new float2(0f, -SettingsMetrics.ControlBand * 0.5f), new float2(0f, SettingsMetrics.ControlBand * 0.5f));

    public void Add(Action<int> pick)
    {
        int index = _chips.Count;
        var host = SettingsUI.Child(_strip, "Segment", float2.Zero, float2.One, float2.Zero, float2.Zero);
        var chip = SettingsChip.Build(host, _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        host.ActiveSelf.Value = false;
        chip.Button.Clicked += (_, _) => pick(index);
        _chips.Add(chip);
    }

    public void Grow(int count, Action<int> pick)
    {
        while (_chips.Count < count)
            Add(pick);
    }

    // A short strip keeps to its share of the column; four options or more, or a stacked row, take all of it.
    public static float SpanFor(SettingsRowFit fit, int count, float shortSpan = StripSpan)
        => fit.Stacked || count > 3 ? 1f : shortSpan;

    // Pills per line in a strip this wide, evened out over the lines they need.
    public static int PerLine(SettingsRowFit? fit, int count, float shortSpan = StripSpan)
    {
        if (fit == null || !fit.Measured || count <= 1)
            return System.Math.Max(1, count);
        float width = fit.ControlWidth * SpanFor(fit, count, shortSpan);
        int fits = System.Math.Clamp((int)MathF.Floor((width + SettingsMetrics.SegmentGap) / (PillMin + SettingsMetrics.SegmentGap)), 1, count);
        int lines = (count + fits - 1) / fits;
        return (count + lines - 1) / lines;
    }

    public static int Lines(int count, int perLine) => System.Math.Max(1, (count + perLine - 1) / System.Math.Max(1, perLine));

    public static float BlockHeight(int lines)
        => lines * SettingsMetrics.ControlBand + (lines - 1) * SettingsMetrics.SegmentGap;

    public static float BlockHeight(SettingsRowFit? fit, int count, float shortSpan = StripSpan)
        => BlockHeight(Lines(count, PerLine(fit, count, shortSpan)));

    public void Layout(SettingsRowFit? fit, int count)
    {
        if (fit == null)
            Layout(count);
        else
            Arrange(SettingsUI.Rect(_strip), _chips, fit, count, StripSpan);
    }

    // The strip as a block of lines centred in the control column, every pill one column of the line
    // grid so a short last line still lines up under the ones above it.
    public static void Arrange(RectTransform strip, List<SettingsChip> chips, SettingsRowFit fit, int count, float shortSpan)
    {
        int perLine = PerLine(fit, count, shortSpan);
        float half = BlockHeight(Lines(count, perLine)) * 0.5f;
        SettingsUI.SetAnchors(strip, new float2(0f, 0.5f), new float2(SpanFor(fit, count, shortSpan), 0.5f));
        SettingsUI.SetOffsets(strip, new float2(0f, -half), new float2(0f, half));
        for (int i = 0; i < chips.Count; i++)
        {
            bool used = i < count;
            ListingStyle.SetActive(chips[i].Slot, used);
            if (!used)
                continue;
            int column = i % perLine;
            float top = (i / perLine) * (SettingsMetrics.ControlBand + SettingsMetrics.SegmentGap);
            var rect = SettingsUI.Rect(chips[i].Slot);
            SettingsUI.SetAnchors(rect, new float2(column / (float)perLine, 1f), new float2((column + 1) / (float)perLine, 1f));
            SettingsUI.SetOffsets(rect, new float2(0f, -(top + SettingsMetrics.ControlBand)), new float2(-SettingsMetrics.SegmentGap, -top));
        }
    }

    // Fractional anchors so any option count fills the strip exactly; a fixed pill width either
    // overflows the column or leaves a gap that changes with the count.
    public void Layout(int count)
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            bool used = i < count;
            ListingStyle.SetActive(_chips[i].Slot, used);
            if (!used)
                continue;
            var rect = SettingsUI.Rect(_chips[i].Slot);
            float from = i / (float)count;
            float to = (i + 1) / (float)count;
            SettingsUI.SetAnchors(rect, new float2(from, 0f), new float2(to, 1f));
            SettingsUI.SetOffsets(rect, float2.Zero, new float2(-SettingsMetrics.SegmentGap, 0f));
        }
    }

    public void Paint(int index, string label, bool active, bool interactable)
    {
        if (index < 0 || index >= _chips.Count)
            return;
        var chip = _chips[index];
        ListingStyle.SetText(chip.Text, label);
        if (!interactable)
            chip.SetPaint(DashTheme.Field, DashTheme.Field, DashTheme.Outline, DashTheme.TextMuted);
        else if (active)
            chip.SetPaint(DashTheme.Accent, DashTheme.AccentHover, DashTheme.Accent, DashTheme.OnAccent);
        else
            chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
        chip.SetInteractable(interactable);
    }
}

// ACTION

internal sealed class SettingsActionTemplate : SettingsRowTemplate
{
    public SettingsActionTemplate(SettingsFonts fonts) : base(fonts) { }

    protected override bool HintFromDetail => false;

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        // No label share here: an action row is a sentence and a button, so the sentence gets the row.
        var shell = BuildShell(row, clickable: false, labelSplit: 1f,
            labelRightInset: SettingsMetrics.ActionWidth + SettingsMetrics.ValueGap + SettingsMetrics.RowInset,
            controlLeftInset: -(SettingsMetrics.ActionWidth + SettingsMetrics.RowInset),
            controlRightInset: SettingsMetrics.RowInset);
        shell.StackedControlWidth = SettingsMetrics.ActionWidth;

        var host = SettingsUI.Band(shell.Control, "Button", SettingsMetrics.ControlBand, 0f, 0f);
        var chip = SettingsChip.Build(host, Fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);

        var listingRow = new ActionRow { Shell = shell, Chip = chip };
        chip.Button.Clicked += (_, _) =>
        {
            if (listingRow.Item is ListingAction action && action.Interactable)
                action.Invoke();
        };
        return listingRow;
    }

    private sealed class ActionRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsChip Chip;

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: false);
            var action = item as ListingAction;
            ListingStyle.SetText(Chip.Text, action != null ? action.ButtonLabel.Resolve() : "Apply");
            // Destructive reads in the negative color, not as a red slab: the row is neutral chrome and
            // the word is the warning.
            color text = !item.Interactable
                ? DashTheme.TextMuted
                : action != null && action.Destructive ? DashTheme.Negative : DashTheme.Text;
            Chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, text);
            Chip.SetInteractable(item.Interactable);
        }
    }
}

// READ-ONLY LABEL

internal sealed class SettingsLabelTemplate : SettingsRowTemplate
{
    public SettingsLabelTemplate(SettingsFonts fonts) : base(fonts) { }

    protected override bool HintFromDetail => false;

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var value = SettingsUI.Label(shell.Control, "Value", Fonts.Body, DashTheme.FontBody, DashTheme.TextDim,
            TextHorizontalAlignment.Right, float2.Zero, float2.One, float2.Zero, float2.Zero, shrinkToFit: true);
        return new LabelRow { Shell = shell, Value = value };
    }

    private sealed class LabelRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required Text Value;

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: false);
            ListingStyle.SetText(Value, item is ListingLabel label ? label.Value : item.Detail);
            ListingStyle.SetTextColor(Value, item.Interactable ? DashTheme.TextDim : DashTheme.TextMuted);
        }
    }
}

// SIDEBAR NAV

internal sealed class SettingsNavTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;
    private readonly Action<ListingItem?> _pick;

    public SettingsNavTemplate(SettingsFonts fonts, Action<ListingItem?> pick)
    {
        _fonts = fonts;
        _pick = pick;
    }

    public override float Height => SettingsMetrics.NavHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        // No box in the idle state: the rail is a list of words, and only the current one carries a
        // wash. A border on every item is what made the old sidebar read as five stacked buttons.
        var panel = SettingsUI.Panel(row, color.Transparent, color.Transparent, DashTheme.RadiusControl);
        var button = row.AttachComponent<Button>();
        var text = SettingsUI.Label(row, "Label", _fonts.Medium, DashTheme.FontBody, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, float2.One,
            new float2(SettingsMetrics.RowInset, 0f), new float2(-SettingsMetrics.RowInset, 0f));

        var listingRow = new NavRow { Panel = panel, Text = text };
        button.HoverEntered += _ => listingRow.SetHovered(true);
        button.HoverExited += _ => listingRow.SetHovered(false);
        // The sidebar SELECTS, it does not navigate: pushing the key onto the sidebar's own path would
        // walk that list into an empty category. The entry view is the one that moves.
        button.Clicked += (_, _) => _pick(listingRow.Item);
        return listingRow;
    }

    private sealed class NavRow : ListingRow
    {
        public required RoundedPanel Panel;
        public required Text Text;

        private bool _hovered;

        public void SetHovered(bool value)
        {
            if (_hovered == value)
                return;
            _hovered = value;
            Repaint();
        }

        public override void Bind(ListingItem item)
        {
            ListingStyle.SetText(Text, item.Label);
            Repaint();
        }

        public override void OnSelectionChanged() => Repaint();

        private void Repaint()
        {
            color fill = Selected ? DashTheme.AccentSoft : _hovered ? DashTheme.SurfaceHover : color.Transparent;
            SettingsUI.SetPaint(Panel, fill, color.Transparent);
            ListingStyle.SetTextColor(Text, Selected ? DashTheme.Accent : DashTheme.TextDim);
        }
    }
}

// BINDINGS
//
// Six cells across, so this row keeps its own column table instead of the shared label share. Keyboard
// and mouse share one cell because they share one desk. A narrower list scales the cells, then puts the
// label above them, then gives Clear and Reset a line of their own.

internal sealed class SettingsBindingTemplate : SettingsRowTemplate
{
    private static readonly InputDeviceKind[] DesktopDevices = { InputDeviceKind.Keyboard, InputDeviceKind.Mouse };
    private static readonly InputDeviceKind[] PadDevices = { InputDeviceKind.Gamepad };
    private static readonly InputDeviceKind[] VRDevices = { InputDeviceKind.VRController };

    private readonly SettingsScreen _screen;

    // Desktop, gamepad, VR, Clear, Reset at full size; the cells scale from these.
    private static readonly float[] CellWidths =
    {
        SettingsMetrics.BindDesktopWidth, SettingsMetrics.BindPadWidth, SettingsMetrics.BindVRWidth,
        SettingsMetrics.BindClearWidth, SettingsMetrics.BindResetWidth,
    };
    // How far the cells may shrink before the row changes shape.
    private const float MinScale = 0.7f;

    public SettingsBindingTemplate(SettingsFonts fonts, SettingsScreen screen) : base(fonts) => _screen = screen;

    public override float HeightFor(ListingItem item) => HeightOf(ShapeFor(Fit));

    private static float Sum(int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++)
            sum += CellWidths[i];
        return sum;
    }

    // 0: one line. 1: the label above all five cells. 2: the label, the three bindings, then Clear and Reset.
    private static int ShapeFor(SettingsRowFit? fit)
    {
        if (fit == null || fit.Width <= 0f)
            return 0;
        float line = fit.Width - SettingsMetrics.RowInset * 2f - DashTheme.Gap * 4f;
        if (line - SettingsMetrics.BindLabelWidth - DashTheme.Gap >= Sum(5) * MinScale)
            return 0;
        return line >= Sum(5) * MinScale ? 1 : 2;
    }

    private static float CellTop => SettingsRowFit.StackPad + SettingsRowFit.StackLabel + SettingsRowFit.StackGap;

    private static float HeightOf(int shape) => shape switch
    {
        0 => SettingsMetrics.RowHeight,
        1 => CellTop + SettingsMetrics.ControlBand + SettingsRowFit.StackPad,
        _ => CellTop + SettingsMetrics.ControlBand * 2f + SettingsMetrics.SegmentGap + SettingsRowFit.StackPad,
    };

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var label = SettingsUI.Label(row, "Label", Fonts.Body, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(0f, 1f), float2.Zero, float2.Zero, shrinkToFit: true);

        var listingRow = new BindingRow { Label = label, Background = background, Fit = Fit };

        listingRow.Cells.Add(AddCell(row, listingRow, DesktopDevices));
        listingRow.Cells.Add(AddCell(row, listingRow, PadDevices));
        listingRow.Cells.Add(AddCell(row, listingRow, VRDevices));

        listingRow.Clear = AddButton(row, () =>
        {
            if (listingRow.Item?.Tag is InputAction action)
                _screen.ClearAllBindings(action);
        });
        listingRow.Reset = AddButton(row, () =>
        {
            if (listingRow.Item?.Tag is InputAction action)
                _screen.ResetBinding(action);
        });
        return listingRow;
    }

    private SettingsChip AddCell(Slot row, BindingRow listingRow, InputDeviceKind[] devices)
    {
        var host = SettingsUI.Child(row, "Cell", new float2(0f, 1f), new float2(0f, 1f), float2.Zero, float2.Zero);
        var chip = SettingsChip.Build(host, Fonts.Body, DashTheme.FontSmall, DashTheme.RadiusChip);
        chip.Button.Clicked += (_, _) =>
        {
            if (listingRow.Item?.Tag is InputAction action)
                _screen.BeginRebind(action, devices);
        };
        return chip;
    }

    private SettingsChip AddButton(Slot row, Action click)
    {
        var host = SettingsUI.Child(row, "Button", new float2(0f, 1f), new float2(0f, 1f), float2.Zero, float2.Zero);
        var chip = SettingsChip.Build(host, Fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusChip);
        chip.Button.Clicked += (_, _) => click();
        return chip;
    }

    private sealed class BindingRow : ListingRow
    {
        public required Text Label;
        public required RoundedPanel Background;
        public SettingsChip? Clear;
        public SettingsChip? Reset;
        public SettingsRowFit? Fit;
        public readonly List<SettingsChip> Cells = new();

        private int _shape = -1;
        private float _width = -1f;

        // Every part hangs from the row's top-left corner, placed in row units for the width the fit last
        // measured; with no fit the row keeps its full-size table.
        private void Arrange()
        {
            int shape = ShapeFor(Fit);
            float width = Fit?.Width ?? 0f;
            if (shape == _shape && width == _width)
                return;
            _shape = shape;
            _width = width;

            float inset = SettingsMetrics.RowInset;
            float gap = DashTheme.Gap;
            if (shape == 0)
            {
                Place(Label.Slot, inset, 0f, SettingsMetrics.BindLabelWidth, SettingsMetrics.RowHeight);
                float x = inset + SettingsMetrics.BindLabelWidth + gap;
                float scale = width > 0f ? (width - inset - x - gap * 4f) / Sum(5) : 1f;
                for (int i = 0; i < 5; i++)
                {
                    float w = CellWidths[i] * scale;
                    Place(PartAt(i), x, gap, w, SettingsMetrics.RowHeight - gap * 2f);
                    x += w + gap;
                }
                return;
            }

            float line = width - inset * 2f;
            Place(Label.Slot, inset, SettingsRowFit.StackPad, line, SettingsRowFit.StackLabel);
            int across = shape == 1 ? 5 : 3;
            float rowScale = (line - gap * (across - 1)) / Sum(across);
            float cx = inset;
            for (int i = 0; i < across; i++)
            {
                float w = CellWidths[i] * rowScale;
                Place(PartAt(i), cx, CellTop, w, SettingsMetrics.ControlBand);
                cx += w + gap;
            }
            if (shape == 2)
            {
                float top = CellTop + SettingsMetrics.ControlBand + SettingsMetrics.SegmentGap;
                float right = width - inset;
                Place(PartAt(4), right - SettingsMetrics.BindResetWidth, top, SettingsMetrics.BindResetWidth, SettingsMetrics.ControlBand);
                Place(PartAt(3), right - SettingsMetrics.BindResetWidth - gap - SettingsMetrics.BindClearWidth, top,
                    SettingsMetrics.BindClearWidth, SettingsMetrics.ControlBand);
            }
        }

        private Slot? PartAt(int index) => index switch
        {
            < 3 => index < Cells.Count ? Cells[index].Slot : null,
            3 => Clear?.Slot,
            _ => Reset?.Slot,
        };

        private static void Place(Slot? slot, float x, float top, float width, float height)
        {
            if (slot == null)
                return;
            var rect = SettingsUI.Rect(slot);
            SettingsUI.SetAnchors(rect, new float2(0f, 1f), new float2(0f, 1f));
            SettingsUI.SetOffsets(rect, new float2(x, -(top + height)), new float2(x + width, -top));
        }

        public override void Bind(ListingItem item)
        {
            Arrange();
            ListingStyle.SetText(Label, item.Label);
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            if (Clear != null)
            {
                ListingStyle.SetText(Clear.Text, "Clear");
                Clear.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
            }
            if (Reset != null)
            {
                ListingStyle.SetText(Reset.Text, "Reset");
                Reset.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
            }

            if (item.Tag is not InputAction action)
                return;

            var map = Engine.Current?.InputInterface?.Actions;
            bool listening = map?.CaptureStatus == InputBindingMap.CaptureState.Listening;

            for (int i = 0; i < Cells.Count; i++)
            {
                var cell = Cells[i];
                var devices = CellDevices(i);
                bool waiting = listening
                    && ReferenceEquals(map!.CaptureTarget, action)
                    && SameDevices(map.CaptureDevices, devices);
                if (waiting)
                {
                    ListingStyle.SetText(cell.Text, map!.CapturePrompt);
                    cell.SetPaint(DashTheme.AccentSoft, DashTheme.AccentSoft, DashTheme.Accent, DashTheme.Accent);
                    continue;
                }
                ListingStyle.SetText(cell.Text, action.DescribeBindings(devices));
                bool bound = action.HasBindingFor(devices);
                cell.SetPaint(DashTheme.Field, DashTheme.SurfaceHover, DashTheme.Outline,
                    bound ? DashTheme.Text : DashTheme.TextMuted);
            }
        }

        private static InputDeviceKind[] CellDevices(int index) => index switch
        {
            0 => DesktopDevices,
            1 => PadDevices,
            _ => VRDevices,
        };

        private static bool SameDevices(IReadOnlyList<InputDeviceKind> a, InputDeviceKind[] b)
        {
            if (a.Count != b.Length)
                return false;
            for (int i = 0; i < b.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }
}

// LOCOMOTION
//
// Segmented row over the local user's registered locomotion modules, current one highlighted. Selecting
// activates it immediately (the same call the radial context menu makes) and remembers it as the spawn
// preference. A module the world's permission gate currently denies stays visible but non-interactive
// rather than disappearing, matching how the radial menu never hides one.
//
// Built lazily against whatever the controller holds at bind time, so a dashboard opened before the
// avatar finished spawning fills itself in on the next refresh instead of staying empty. -xlinka

internal sealed class SettingsLocomotionTemplate : SettingsRowTemplate
{
    private readonly SettingsScreen _screen;

    // How many modules the height was last measured for: modules arrive with the avatar, after the list
    // was first measured, and a row that then needs another line asks to be measured again.
    private int _measuredFor;

    public SettingsLocomotionTemplate(SettingsFonts fonts, SettingsScreen screen) : base(fonts) => _screen = screen;

    protected override float ControlHeight(ListingItem item)
    {
        _measuredFor = Pickable(_screen, null);
        return SettingsSegments.BlockHeight(Fit, _measuredFor);
    }

    // The modules a user can pick, the radial menu's own filter: the "no locomotion" fallback is an
    // implementation detail, not something a user picks. Counts them, and lists them when handed a list.
    private static int Pickable(SettingsScreen screen, List<LocomotionModule>? into)
    {
        into?.Clear();
        var locomotion = screen.GetLocalLocomotionController();
        if (locomotion == null)
            return 0;
        int count = 0;
        foreach (var module in locomotion.Modules)
        {
            if (module == null || module.IsDestroyed)
                continue;
            if (string.IsNullOrEmpty(module.DisplayName) || module.DisplayName == "None")
                continue;
            into?.Add(module);
            count++;
        }
        return count;
    }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var strip = SettingsSegments.Strip(shell.Control);
        return new LocomotionRow
        {
            Shell = shell,
            Segments = new SettingsSegments(strip, Fonts),
            Screen = _screen,
            Template = this,
        };
    }

    private sealed class LocomotionRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsSegments Segments;
        public required SettingsScreen Screen;
        public required SettingsLocomotionTemplate Template;

        private readonly List<LocomotionModule> _modules = new();

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);

            Pickable(Screen, _modules);
            var locomotion = Screen.GetLocalLocomotionController();
            var fit = Template.Fit;
            if (fit != null && SettingsSegments.BlockHeight(fit, _modules.Count) != SettingsSegments.BlockHeight(fit, Template._measuredFor))
                fit.MarkStale();

            Segments.Grow(_modules.Count, Pick);
            Segments.Layout(fit, _modules.Count);
            for (int i = 0; i < _modules.Count; i++)
            {
                var module = _modules[i];
                bool usable = locomotion != null && locomotion.IsModuleUsable(module);
                bool active = usable && locomotion != null && ReferenceEquals(locomotion.ActiveModule, module);
                Segments.Paint(i, module.DisplayName, active, usable);
            }
        }

        private void Pick(int index)
        {
            if (index >= 0 && index < _modules.Count)
                Screen.SelectLocomotionModule(_modules[index]);
        }
    }
}
