// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Input;
using Lumora.Core.Input.Actions;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

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
    public required Slot Control;
    public Button? Click;

    private bool _hovered;
    private bool _interactable = true;

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
        if (hasHint)
        {
            ListingStyle.SetText(HintText, item.Detail);
            ListingStyle.SetTextColor(HintText, item.Interactable ? DashTheme.TextDim : DashTheme.TextMuted);
        }
        // The label sits centred in whatever is left above the hint line, so a row with no hint centres
        // in the whole row and the two kinds still share a baseline grid.
        SettingsUI.SetOffsets(LabelRect, new float2(0f, hasHint ? SettingsMetrics.HintHeight : 0f), float2.Zero);

        ListingStyle.SetInteractable(Click, item.Interactable);
        Repaint();
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

        var hint = SettingsUI.Label(labels, "Hint", Fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 0f),
            new float2(0f, 1f), new float2(0f, SettingsMetrics.HintHeight + 1f), shrinkToFit: true);
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
            Control = control,
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
            // different setting holding the same number. -xlinka
            if (!ReferenceEquals(_shownFor, item) || _shown != current)
                ShowValue(slider, current);
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

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var strip = SettingsSegments.Strip(shell.Control);
        var listingRow = new ChoiceRow { Shell = shell, Segments = new SettingsSegments(strip, Fonts) };
        for (int i = 0; i < InitialSegments; i++)
            listingRow.Segments.Add(index => listingRow.Pick(index));
        return listingRow;
    }

    private sealed class ChoiceRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsSegments Segments;

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
            Segments.Layout(count);
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

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        // No label share here: an action row is a sentence and a button, so the sentence gets the row.
        var shell = BuildShell(row, clickable: false, labelSplit: 1f,
            labelRightInset: SettingsMetrics.ActionWidth + SettingsMetrics.ValueGap + SettingsMetrics.RowInset,
            controlLeftInset: -(SettingsMetrics.ActionWidth + SettingsMetrics.RowInset),
            controlRightInset: SettingsMetrics.RowInset);

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
// and mouse share one cell because they share one desk.

internal sealed class SettingsBindingTemplate : SettingsRowTemplate
{
    private static readonly InputDeviceKind[] DesktopDevices = { InputDeviceKind.Keyboard, InputDeviceKind.Mouse };
    private static readonly InputDeviceKind[] PadDevices = { InputDeviceKind.Gamepad };
    private static readonly InputDeviceKind[] VRDevices = { InputDeviceKind.VRController };

    private readonly SettingsScreen _screen;

    public SettingsBindingTemplate(SettingsFonts fonts, SettingsScreen screen) : base(fonts) => _screen = screen;

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        float x = SettingsMetrics.RowInset;
        var label = SettingsUI.Label(row, "Label", Fonts.Body, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, new float2(0f, 1f),
            new float2(x, 0f), new float2(x + SettingsMetrics.BindLabelWidth, 0f), shrinkToFit: true);
        x += SettingsMetrics.BindLabelWidth + DashTheme.Gap;

        var listingRow = new BindingRow { Label = label, Background = background };

        listingRow.Cells.Add(AddCell(row, listingRow, DesktopDevices, SettingsMetrics.BindDesktopWidth, ref x));
        listingRow.Cells.Add(AddCell(row, listingRow, PadDevices, SettingsMetrics.BindPadWidth, ref x));
        listingRow.Cells.Add(AddCell(row, listingRow, VRDevices, SettingsMetrics.BindVRWidth, ref x));

        listingRow.Clear = AddButton(row, SettingsMetrics.BindClearWidth, ref x, () =>
        {
            if (listingRow.Item?.Tag is InputAction action)
                _screen.ClearAllBindings(action);
        });
        listingRow.Reset = AddButton(row, SettingsMetrics.BindResetWidth, ref x, () =>
        {
            if (listingRow.Item?.Tag is InputAction action)
                _screen.ResetBinding(action);
        });
        return listingRow;
    }

    private SettingsChip AddCell(Slot row, BindingRow listingRow, InputDeviceKind[] devices, float width, ref float x)
    {
        var host = SettingsUI.Column(row, "Cell", x, width, DashTheme.Gap);
        x += width + DashTheme.Gap;
        var chip = SettingsChip.Build(host, Fonts.Body, DashTheme.FontSmall, DashTheme.RadiusChip);
        chip.Button.Clicked += (_, _) =>
        {
            if (listingRow.Item?.Tag is InputAction action)
                _screen.BeginRebind(action, devices);
        };
        return chip;
    }

    private SettingsChip AddButton(Slot row, float width, ref float x, Action click)
    {
        var host = SettingsUI.Column(row, "Button", x, width, DashTheme.Gap);
        x += width + DashTheme.Gap;
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
        public readonly List<SettingsChip> Cells = new();

        public override void Bind(ListingItem item)
        {
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
                    ListingStyle.SetText(cell.Text, "Press...");
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

    public SettingsLocomotionTemplate(SettingsFonts fonts, SettingsScreen screen) : base(fonts) => _screen = screen;

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var strip = SettingsSegments.Strip(shell.Control);
        return new LocomotionRow
        {
            Shell = shell,
            Segments = new SettingsSegments(strip, Fonts),
            Screen = _screen,
        };
    }

    private sealed class LocomotionRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsSegments Segments;
        public required SettingsScreen Screen;

        private readonly List<LocomotionModule> _modules = new();

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);

            _modules.Clear();
            var locomotion = Screen.GetLocalLocomotionController();
            if (locomotion != null)
            {
                foreach (var module in locomotion.Modules)
                {
                    if (module == null || module.IsDestroyed)
                        continue;
                    // Matches the radial menu's own filter: the "no locomotion" fallback is an
                    // implementation detail, not something a user picks.
                    if (string.IsNullOrEmpty(module.DisplayName) || module.DisplayName == "None")
                        continue;
                    _modules.Add(module);
                }
            }

            Segments.Grow(_modules.Count, Pick);
            Segments.Layout(_modules.Count);
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
