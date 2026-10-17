// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The camera's widgets. Each one works this machine's camera director, which sits in userspace beside the
// dash, so a press goes straight to it. What they show is repainted when the director reports a change, and
// on the slow poll for what it does not report: which world has focus, whether a headset is on, what the
// world's rules allow.
//
// Every control is registered here once with how it reads, how it writes and when it is live, so one
// repaint covers them all and edit mode takes every one of them dead in one place. -xlinka
public abstract class DirectorWidgetPreset : HomeWidgetPreset
{
    protected const float Inset = 10f;
    protected const float LineHeight = 30f;
    protected const float LinePitch = 34f;
    protected const float ValueWidth = 52f;
    protected const float FieldWidth = 66f;
    protected const float CellGap = 6f;

    private readonly List<Bound> _bound = new();
    private CameraDirector? _director;
    private volatile bool _changed;
    private bool _reshaped;
    private bool _activeHooked;
    private bool? _onCameraPanel;

    protected static CameraDirector? Director => CameraDirector.Local is { IsDestroyed: false } director ? director : null;

    protected static bool InVR => Engine.Current?.InputInterface?.IsVRActive ?? false;

    // Ask for a repaint on the next update rather than waiting for the poll.
    protected void MarkChanged() => _changed = true;

    // Edit mode is for picking widgets up off a grid. The camera panel is not one, so its controls stay
    // live whatever the flag says.
    protected bool Editing
    {
        get
        {
            _onCameraPanel ??= CameraPanel.Holding(Slot) != null;
            return WidgetPanel.EditMode && !_onCameraPanel.Value;
        }
    }

    // Paints every registered control. Overrides paint their own parts and call down.
    protected virtual void Refresh()
    {
        var director = Director;
        bool editing = Editing;
        for (int i = 0; i < _bound.Count; i++)
            _bound[i].Paint(director, editing);
    }

    // Per frame while the widget is up, for what cannot wait for the poll.
    protected virtual void Tick(float delta)
    {
    }

    // The last thing a Build does: hook the director and paint once.
    protected void Settle()
    {
        if (!_activeHooked)
        {
            _activeHooked = true;
            Slot.ActiveChanged += OnSlotActiveChanged;
        }
        RefreshNow();
    }

    protected override void Poll() => RefreshNow();

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (IsDestroyed || !Slot.IsActive)
            return;
        if (_changed)
        {
            _changed = false;
            RefreshNow();
        }
        Tick(delta);
    }

    public override void OnDestroy()
    {
        if (_director != null)
            _director.StateChanged -= OnDirectorChanged;
        _director = null;
        if (_activeHooked)
            Slot.ActiveChanged -= OnSlotActiveChanged;
        base.OnDestroy();
    }

    private void RefreshNow()
    {
        Watch();
        Refresh();
        if (!_reshaped)
            return;
        _reshaped = false;
        Repaint();
    }

    // Follows CameraDirector.Local, which changes if the dash it sits on is torn down and built again.
    private void Watch()
    {
        var director = Director;
        if (ReferenceEquals(director, _director))
            return;
        if (_director != null)
            _director.StateChanged -= OnDirectorChanged;
        _director = director;
        if (_director != null)
            _director.StateChanged += OnDirectorChanged;
    }

    private void OnDirectorChanged() => _changed = true;

    // Nothing repaints while the screen is hidden, so coming back into view is a change of its own.
    private void OnSlotActiveChanged(Slot slot)
    {
        if (!IsDestroyed && slot.IsActive)
            _changed = true;
    }

    // Shows or hides a part; a part that appears or goes away needs the canvas to lay out again.
    protected void Show(Slot? slot, bool active)
    {
        if (slot == null || slot.IsDestroyed || slot.ActiveSelf.Value == active)
            return;
        slot.ActiveSelf.Value = active;
        _reshaped = true;
    }

    // BUILDING

    protected Text Caption(Slot parent, string name, string text, float top)
    {
        var row = Row(parent, name, top, 18f, 0f, 0f);
        return Label(row, "Text", text.ToUpperInvariant(), DashTheme.FontLabel, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, float2.Zero, float2.One, float2.Zero, float2.Zero, SemiboldFont);
    }

    // A label down the left share of a row that shrinks before it runs into the control beside it.
    protected Text RowLabel(Slot row, string text, float share, float right = 6f)
    {
        var label = Label(row, "Label", text, DashTheme.FontSmall, DashTheme.Text, TextHorizontalAlignment.Left,
            float2.Zero, new float2(share, 1f), float2.Zero, new float2(-right, 0f));
        label.AutoSizeHorizontal.Value = true;
        label.AutoSizeMin.Value = 10f;
        return label;
    }

    // One cell of a row split into equal parts, with a gap after every cell but the last.
    protected static Slot Cell(Slot row, string name, int index, int count, float from = 0f, float to = 1f)
    {
        float span = (to - from) / count;
        float gap = index < count - 1 ? CellGap : 0f;
        return SettingsUI.Child(row, name, new float2(from + span * index, 0f), new float2(from + span * (index + 1), 1f),
            float2.Zero, new float2(-gap, 0f));
    }

    // A pill that works the director. live says when it can be pressed; lit, when it shows as on. A chip
    // painted by its widget (a colour of its own, a blink) passes painted and is only gated here.
    private protected SettingsChip Chip(Slot host, string label, Action<CameraDirector> onClick,
        Func<CameraDirector, bool>? live = null, Func<CameraDirector, bool>? lit = null, bool painted = false)
    {
        var chip = BuildChip(host, label, () =>
        {
            if (Director is { } director)
                onClick(director);
        });
        _bound.Add(new ChipBound
        {
            Chip = chip,
            Live = live,
            Lit = lit,
            Painted = painted,
        });
        return chip;
    }

    // A pill for something the director does not own (a tab, another program, a panel).
    private protected SettingsChip FreeChip(Slot host, string label, Action onClick, Func<bool>? live = null, bool painted = false)
    {
        var chip = BuildChip(host, label, onClick);
        _bound.Add(new ChipBound
        {
            Chip = chip,
            FreeLive = live ?? (() => true),
            Painted = painted,
        });
        return chip;
    }

    private SettingsChip BuildChip(Slot host, string label, Action onClick)
    {
        var chip = SettingsChip.Build(host, SemiboldFont, DashTheme.FontSmall, DashTheme.RadiusControl);
        ListingStyle.SetText(chip.Text, label);
        chip.Button.Clicked += (_, _) =>
        {
            onClick();
            _changed = true;
        };
        PaintChip(chip, false, true);
        return chip;
    }

    private protected static void PaintChip(SettingsChip chip, bool lit, bool live, color? litFill = null)
    {
        if (!live)
        {
            chip.SetPaint(DashTheme.Field, DashTheme.Field, DashTheme.Outline, DashTheme.TextMuted);
            return;
        }
        if (!lit)
        {
            chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.Text);
            return;
        }
        var fill = litFill ?? DashTheme.Accent;
        var hover = litFill.HasValue ? Shade(fill, 0.08f) : DashTheme.AccentHover;
        chip.SetPaint(fill, hover, fill, WidgetScreen.OnFill(fill));
    }

    // Same step the accent ramp takes, for a chip lit in a colour of its own. Linear values.
    protected static color Shade(in color value, float amount)
        => new color(Clamp01(value.r + amount), Clamp01(value.g + amount), Clamp01(value.b + amount), value.a);

    protected static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    // The dash switch in host, at its left edge; the whole host is the click target.
    private protected ToggleSwitch Switch(Slot host, Func<CameraDirector, bool> read, Action<CameraDirector, bool> write,
        Func<CameraDirector, bool>? live = null, bool showState = true)
    {
        var toggle = ToggleSwitch.Build(host, BodyFont);
        if (!showState)
            SetActive(host.FindChild("State", recursive: false), false);
        var button = host.AttachComponent<Button>();
        button.Clicked += (_, _) =>
        {
            var director = Director;
            if (director == null)
                return;
            write(director, !read(director));
            _changed = true;
        };
        _bound.Add(new SwitchBound
        {
            Switch = toggle,
            Button = button,
            Read = read,
            Live = live,
        });
        return toggle;
    }

    // A label and a switch, the row itself pressing it.
    protected Slot SwitchRow(Slot parent, string name, string label, float top, Func<CameraDirector, bool> read,
        Action<CameraDirector, bool> write, Func<CameraDirector, bool>? live = null, bool showState = true)
    {
        var row = Row(parent, name, top, LineHeight, 0f, 0f);
        float width = showState ? ToggleSwitch.Width : ToggleSwitch.TrackWidth;
        var text = Label(row, "Label", label, DashTheme.FontSmall, DashTheme.Text, TextHorizontalAlignment.Left,
            float2.Zero, float2.One, float2.Zero, new float2(-(width + 8f), 0f));
        text.AutoSizeHorizontal.Value = true;
        text.AutoSizeMin.Value = 10f;
        Switch(row, read, write, live, showState);
        ShiftRight(row, width);
        return row;
    }

    // ToggleSwitch anchors its parts to its host's left edge; a row wants them at its right, and keeps the
    // whole row as the click target.
    private static void ShiftRight(Slot row, float width)
    {
        foreach (var name in new[] { "Track", "State" })
        {
            var part = row.FindChild(name, recursive: false)?.GetComponent<RectTransform>();
            if (part == null)
                continue;
            var min = part.AnchorMin.Value;
            var max = part.AnchorMax.Value;
            part.AnchorMin.Value = new float2(1f, min.y);
            part.AnchorMax.Value = new float2(1f, max.y);
            part.OffsetMin.Value = new float2(part.OffsetMin.Value.x - width, part.OffsetMin.Value.y);
            part.OffsetMax.Value = new float2(part.OffsetMax.Value.x - width, part.OffsetMax.Value.y);
        }
    }

    // A bar across host, filling it. Pressing and dragging writes; the poll does not move it under a hand.
    private protected Slider Bar(Slot host, float min, float max, Func<CameraDirector, float> read,
        Action<CameraDirector, float> write, Func<CameraDirector, bool>? live = null, Text? value = null,
        Func<float, string>? format = null)
    {
        var builder = new UIBuilder(host);
        builder.PushStyle();
        builder.ForegroundColor(DashTheme.Accent);
        var slider = builder.Slider(min, min, max, null, DashTheme.Field);
        builder.PopStyle();
        // Slider() sizes itself for a layout cell; these rows anchor instead, so it is pinned to its host.
        FillRect(SettingsUI.Rect(slider.Slot));
        var handle = slider.Slot.FindChild("HandleArea", recursive: false)
            ?.FindChild("Handle", recursive: false)?.GetComponent<ArcSegment>();
        if (handle != null)
            slider.AddColorDriver(handle.Tint, DashTheme.OnAccent);
        slider.ValueChanged += (_, v) =>
        {
            var director = Director;
            if (director == null)
                return;
            write(director, v);
            if (value != null && format != null)
                ListingStyle.SetText(value, format(v));
            _changed = true;
        };
        _bound.Add(new SliderBound
        {
            Slider = slider,
            Min = min,
            Max = max,
            Read = read,
            Live = live,
            Value = value,
            Format = format,
        });
        return slider;
    }

    // Label, bar, and the value it holds at the right.
    protected Slider SliderRow(Slot parent, string name, string label, float top, float min, float max,
        Func<CameraDirector, float> read, Action<CameraDirector, float> write, Func<float, string> format,
        Func<CameraDirector, bool>? live = null, float share = 0.38f)
    {
        var row = Row(parent, name, top, LineHeight, 0f, 0f);
        RowLabel(row, label, share);
        var value = Label(row, "Value", string.Empty, DashTheme.FontSmall, DashTheme.TextDim, TextHorizontalAlignment.Right,
            new float2(1f, 0f), float2.One, new float2(-ValueWidth, 0f), float2.Zero);
        var host = SettingsUI.Child(row, "Bar", new float2(share, 0f), float2.One, float2.Zero, new float2(-(ValueWidth + 4f), 0f));
        return Bar(host, min, max, read, write, live, value, format);
    }

    // A typed value. It commits on Enter and when the field lets go of the keyboard some other way: Enter
    // alone would lose whatever was typed before a click somewhere else. Escape drops the edit.
    protected TextInput Field(Slot host, string placeholder, Func<CameraDirector, string> read,
        Action<CameraDirector, string> write, Func<CameraDirector, bool>? live = null, bool mask = false)
    {
        var builder = new UIBuilder(host);
        builder.Font(BodyFont)
            .FontSize(DashTheme.FontSmall)
            .TextColor(DashTheme.Text)
            .BackgroundColor(DashTheme.Field)
            .ForegroundColor(DashTheme.Accent)
            .CornerRadius(DashTheme.RadiusChip);
        var input = builder.TextInput(string.Empty, placeholder);
        FillRect(SettingsUI.Rect(input.Slot));
        input.Mask.Value = mask;
        void Commit(TextInput _, string text)
        {
            var director = Director;
            if (director == null)
                return;
            write(director, text ?? string.Empty);
            _changed = true;
        }
        input.SubmitOnBlur.Value = true;
        input.EditingFinished += Commit;
        _bound.Add(new FieldBound
        {
            Input = input,
            Read = read,
            Live = live,
        });
        return input;
    }

    // A number field. It takes math against the value now ("*2", "+10"); a decimal comma reads as a point
    // where no function needs it. What does not work out is dropped, says why for a moment, and the field
    // shows the value again. Text still reading as the value shown writes nothing, so the display's rounding
    // never lands on the director.
    protected TextInput NumberField(Slot host, Func<CameraDirector, float> read, Action<CameraDirector, float> write,
        string format, Func<CameraDirector, bool>? live = null)
    {
        TextInput? input = null;
        input = Field(host, string.Empty,
            director => read(director).ToString(format, CultureInfo.InvariantCulture),
            (director, text) =>
            {
                float now = read(director);
                if (string.Equals(text.Trim(), now.ToString(format, CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    return;
                string typed = text.IndexOf('(') < 0 ? text.Replace(',', '.') : text;
                var result = FieldMath.Evaluate(typed, now);
                if (result.Ok && float.IsFinite((float)result.Value))
                    write(director, (float)result.Value);
                else if (result.Error != FieldMathError.Empty && input != null)
                    FieldHint.Show(input, text, result.Ok ? new FieldMath.Result(0d, FieldMathError.NotFinite).Hint : result.Hint, MarkChanged);
            },
            live);
        input.Tip.Value = FieldMath.Tip;
        return input;
    }

    // Label, number, and a bar when the range has one.
    protected TextInput NumberRow(Slot parent, string name, string label, float top, Func<CameraDirector, float> read,
        Action<CameraDirector, float> write, string format, float barMin = 0f, float barMax = 0f,
        Func<CameraDirector, bool>? live = null, float share = 0.32f)
    {
        var row = Row(parent, name, top, LineHeight, 0f, 0f);
        RowLabel(row, label, share);
        var fieldHost = SettingsUI.Child(row, "Number", new float2(share, 0f), new float2(share, 1f),
            new float2(0f, 2f), new float2(FieldWidth, -2f));
        var input = NumberField(fieldHost, read, write, format, live);
        if (barMax > barMin)
        {
            var barHost = SettingsUI.Child(row, "Bar", new float2(share, 0f), float2.One, new float2(FieldWidth + 8f, 0f), float2.Zero);
            Bar(barHost, barMin, barMax, director => System.Math.Clamp(read(director), barMin, barMax), write, live);
        }
        return input;
    }

    // REGISTERED CONTROLS

    private abstract class Bound
    {
        public Func<CameraDirector, bool>? Live;

        protected bool IsLive(CameraDirector? director)
            => director != null && (Live?.Invoke(director) ?? true);

        public abstract void Paint(CameraDirector? director, bool editing);
    }

    private sealed class ChipBound : Bound
    {
        public SettingsChip Chip = null!;
        public Func<CameraDirector, bool>? Lit;
        public Func<bool>? FreeLive;
        public bool Painted;

        public override void Paint(CameraDirector? director, bool editing)
        {
            bool live = FreeLive != null ? FreeLive() : IsLive(director);
            if (!Painted)
                PaintChip(Chip, director != null && (Lit?.Invoke(director) ?? false), live);
            Chip.SetInteractable(live && !editing);
        }
    }

    private sealed class SwitchBound : Bound
    {
        public ToggleSwitch Switch = null!;
        public Button Button = null!;
        public Func<CameraDirector, bool> Read = null!;

        public override void Paint(CameraDirector? director, bool editing)
        {
            bool live = IsLive(director);
            Switch.SetValue(director != null && Read(director), live);
            ListingStyle.SetInteractable(Button, live && !editing);
        }
    }

    private sealed class SliderBound : Bound
    {
        public Slider Slider = null!;
        public float Min;
        public float Max;
        public Func<CameraDirector, float> Read = null!;
        public Text? Value;
        public Func<float, string>? Format;

        public override void Paint(CameraDirector? director, bool editing)
        {
            bool live = IsLive(director);
            if (director != null && !Slider.IsPressedNow)
            {
                float value = Read(director);
                ListingStyle.SetSlider(Slider, Min, Max, System.Math.Clamp(value, Min, Max));
                if (Value != null && Format != null)
                    ListingStyle.SetText(Value, Format(value));
            }
            ListingStyle.SetInteractable(Slider, live && !editing);
        }
    }

    private sealed class FieldBound : Bound
    {
        public TextInput Input = null!;
        public Func<CameraDirector, string> Read = null!;

        public override void Paint(CameraDirector? director, bool editing)
        {
            bool live = IsLive(director);
            // A field being typed in keeps what is typed, and one saying why it refused keeps that.
            if (director != null && !Input.IsFocused && !FieldHint.IsShowing(Input))
            {
                string value = Read(director) ?? string.Empty;
                if (!string.Equals(Input.Text.Value, value, StringComparison.Ordinal))
                    Input.Text.Value = value;
            }
            ListingStyle.SetInteractable(Input, live && !editing);
        }
    }
}
