// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// Spawned color picker for a color/colorHDR member. Four sections down the panel: PREVIEW (the value
// you started with beside the value you have now), PICKER (saturation/value square with the hue and
// alpha strips), CHANNELS (R/G/B/A sliders with byte fields, plus hex), SWATCHES (a pinned palette
// and a recent trail), then Cancel/Save.
//
// Every drag writes the field LIVE so the world previews the color as you move. Save commits the
// whole session as ONE undo step, Cancel restores the value the panel opened on, and closing with
// the X commits like Save because the live edits already landed. -xlinka
[ComponentCategory("Utility/Inspectors")]
public class ColorPickerPanel : Component, IInspectorActionHandler
{
    public readonly SyncRef<IWorldElement> TargetMember;
    public readonly Sync<string> MemberPath;
    // Swatch grid mode: apply on click, or delete on click.
    public readonly Sync<bool> RemoveMode;

    private readonly SyncRef<Image> _beforeSwatch;
    private readonly SyncRef<Image> _afterSwatch;
    private readonly SyncRef<Text> _beforeHexLabel;
    private readonly SyncRef<Text> _afterHexLabel;
    private readonly SyncRef<Pad2D> _svSquare;
    private readonly SyncRef<Pad2D> _hueStrip;
    private readonly SyncRef<Pad2D> _alphaStrip;
    private readonly SyncRef<ColorGradientMaterial> _svMaterial;
    private readonly SyncRef<ColorGradientMaterial> _alphaMaterial;
    private readonly SyncRef<TextInput> _hexInput;
    private readonly SyncRef<Slider> _sliderR;
    private readonly SyncRef<Slider> _sliderG;
    private readonly SyncRef<Slider> _sliderB;
    private readonly SyncRef<Slider> _sliderA;
    private readonly SyncRef<TextInput> _inputR;
    private readonly SyncRef<TextInput> _inputG;
    private readonly SyncRef<TextInput> _inputB;
    private readonly SyncRef<TextInput> _inputA;
    private readonly SyncRef<Slot> _swatchHost;
    private readonly SyncRef<Text> _modeLabel;
    private readonly SyncRef<Text> _modeHint;
    private readonly SyncRef<Button> _modeButton;
    private readonly SyncRef<CheckerTextureProvider> _checker;
    private readonly SyncRef<Text> _pickLabel;
    private readonly SyncRef<Button> _pickButton;

    private const float FrameInset = 2f;
    private const float ChipSize = 40f;
    private const float ChipGap = 8f;
    private const int ChipsPerRow = 6;
    private const float SwatchAreaHeight = 150f;
    private const float StripWidth = 40f;
    private const float PickerHeight = 300f;
    private const float PreviewHeight = 64f;
    private const float CaptionHeight = 20f;
    private const float PickButtonWidth = 110f;
    private const string PickIdleLabel = "Pick";
    private const string PickArmedLabel = "Point + click";

    private static readonly color FrameColor = new color(0.52f, 0.46f, 0.82f, 0.55f);
    private static readonly color TrackColor = new color(0.16f, 0.15f, 0.24f, 0.96f);
    private static readonly color ButtonFill = new color(0.22f, 0.20f, 0.34f, 1f);
    private static readonly color CheckerLight = new color(0.20f, 0.20f, 0.24f, 1f);
    private static readonly color CheckerDark = new color(0.12f, 0.12f, 0.15f, 1f);

    // Hue/sat persist through degenerate RGB (black/grey has no hue of its own), so dragging V to 0
    // and back does not snap hue to red.
    private float _hue;
    private float _sat = 1f;
    private float _val = 1f;

    private color _original = color.White;
    private object? _undoBefore;
    private bool _undoCaptured;
    private bool _undoRecorded;
    private bool _cancelled;
    private bool _swatchesDirty;
    // Eyedropper arm state. Deliberately NOT synced: it is one user's pointer mode, and a second user
    // watching this panel must not have their own primary press eaten by it. -xlinka
    private bool _sampling;

    // The one panel currently eating world presses, this client only. Arming a second picker steals it
    // from the first so there is never a pair of them fighting over the same click. -xlinka
    public static ColorPickerPanel? ActiveSampler { get; private set; }

    public ColorPickerPanel()
    {
        TargetMember = new SyncRef<IWorldElement>(this);
        MemberPath = new Sync<string>(this, "");
        RemoveMode = new Sync<bool>(this, false);
        _beforeSwatch = new SyncRef<Image>(this);
        _afterSwatch = new SyncRef<Image>(this);
        _beforeHexLabel = new SyncRef<Text>(this);
        _afterHexLabel = new SyncRef<Text>(this);
        _svSquare = new SyncRef<Pad2D>(this);
        _hueStrip = new SyncRef<Pad2D>(this);
        _alphaStrip = new SyncRef<Pad2D>(this);
        _svMaterial = new SyncRef<ColorGradientMaterial>(this);
        _alphaMaterial = new SyncRef<ColorGradientMaterial>(this);
        _hexInput = new SyncRef<TextInput>(this);
        _sliderR = new SyncRef<Slider>(this);
        _sliderG = new SyncRef<Slider>(this);
        _sliderB = new SyncRef<Slider>(this);
        _sliderA = new SyncRef<Slider>(this);
        _inputR = new SyncRef<TextInput>(this);
        _inputG = new SyncRef<TextInput>(this);
        _inputB = new SyncRef<TextInput>(this);
        _inputA = new SyncRef<TextInput>(this);
        _swatchHost = new SyncRef<Slot>(this);
        _modeLabel = new SyncRef<Text>(this);
        _modeHint = new SyncRef<Text>(this);
        _modeButton = new SyncRef<Button>(this);
        _checker = new SyncRef<CheckerTextureProvider>(this);
        _pickLabel = new SyncRef<Text>(this);
        _pickButton = new SyncRef<Button>(this);
    }

    private IField? Field => TargetMember.Target as IField;

    private StructMemberAccessor? Accessor
    {
        get
        {
            var field = Field;
            return field == null ? null : StructMemberAccessor.Get(field.ValueType, MemberPath.Value ?? "");
        }
    }

    public static ColorPickerPanel Spawn(World world, IField field, string path, float3 position)
    {
        var panelSlot = world.RootSlot.AddSlot("Color Picker");
        panelSlot.Persistent.Value = false;
        panelSlot.Tag.Value = "Developer";
        panelSlot.GlobalPosition = position;

        var head = world.LocalUser?.Root?.HeadSlot;
        var toViewer = head != null ? head.GlobalPosition - position : float3.Zero;
        toViewer.y = 0f;
        if (toViewer.LengthSquared > 1e-6f)
            panelSlot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z));
        panelSlot.LocalScale.Value = float3.One * 0.0005f;

        var panel = panelSlot.AttachComponent<ColorPickerPanel>();
        panel.TargetMember.Target = field;
        panel.MemberPath.Value = path ?? "";
        return panel;
    }

    public override void OnAttach()
    {
        base.OnAttach();

        var theme = Slot.GetOrAttachComponent<UITheme>();
        // Same dark set as the scene and material inspectors so the panels read as one tool family.
        theme.PanelBackground.Value = new color(0.075f, 0.07f, 0.115f, 1f);
        theme.Header.Value = new color(0.11f, 0.10f, 0.17f, 1f);
        theme.ButtonFill.Value = ButtonFill;
        theme.Accent.Value = InspectorUI.AccentColor;
        theme.Separator.Value = new color(0.52f, 0.46f, 0.82f, 0.6f);
        theme.Border.Value = new color(0.52f, 0.46f, 0.82f, 0.45f);

        // One checkerboard for every alpha surface on the panel (both preview halves and every chip).
        var checker = Slot.GetOrAttachComponent<CheckerTextureProvider>();
        checker.ColorA.Value = CheckerLight;
        checker.ColorB.Value = CheckerDark;
        checker.CellSize.Value = 6;
        _checker.Target = checker;

        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Color";
        // Sized to the content: the picker block wants a real square, the channel rows want room for a
        // byte field beside the slider, and the swatch grid wants six 40px chips across. -xlinka
        shell.Size.Value = new float2(440f, 1100f);
        theme.ApplyTo(shell);

        _original = ReadColor() ?? color.White;

        shell.RebuildContent(BuildLayout);
        RefreshFromField();
        RebuildSwatches();
    }

    public override void OnUpdate(float delta)
    {
        // Close the husk when the edited member dies with its component.
        if (World?.IsAuthority == true && TargetMember.RawTarget is { IsDestroyed: true })
            Slot.Destroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        if (!_swatchesDirty)
            return;
        _swatchesDirty = false;
        RebuildSwatches();
    }

    // Action strings: save/cancel close the panel; pick arms the eyedropper; swatchadd pins the current
    // color; swatchmode flips the grid between apply and delete; swatch:N and recent:N act on one chip
    // by index.
    public void HandleInspectorAction(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return;

        if (argument == "pick")
        {
            SetSampling(!_sampling);
            return;
        }
        if (argument == "save")
        {
            RecordUndoNow();
            Slot.Destroy();
            return;
        }
        if (argument == "cancel")
        {
            if (_undoCaptured && Field is { IsDestroyed: false } field)
                field.BoxedValue = _undoBefore!;
            _cancelled = true;
            Slot.Destroy();
            return;
        }
        if (argument == "swatchadd")
        {
            var current = ReadColor();
            if (current == null)
                return;
            // Pinning counts as using the color, so it heads the recent trail too.
            ColorSwatchStore.AddSaved(current.Value);
            ColorSwatchStore.PushRecent(current.Value);
            MarkSwatchesDirty();
            return;
        }
        if (argument == "swatchmode")
        {
            RemoveMode.Value = !RemoveMode.Value;
            MarkSwatchesDirty();
            return;
        }

        // The chip rows are rebuilt whenever the lists change, so an index is always read against the
        // list the chip was drawn from - a stale one just falls off the end and does nothing.
        if (TryParseIndex(argument, "swatch:", out int savedIndex))
        {
            if (RemoveMode.Value)
            {
                ColorSwatchStore.RemoveSaved(savedIndex);
                MarkSwatchesDirty();
                return;
            }
            var saved = ColorSwatchStore.Saved;
            if (savedIndex < saved.Count)
                ApplySwatch(saved[savedIndex]);
            return;
        }
        if (TryParseIndex(argument, "recent:", out int recentIndex))
        {
            var recent = ColorSwatchStore.Recent;
            if (recentIndex < recent.Count)
                ApplySwatch(recent[recentIndex]);
        }
    }

    private static bool TryParseIndex(string argument, string prefix, out int index)
    {
        index = -1;
        if (!argument.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(argument.AsSpan(prefix.Length), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out index) && index >= 0;
    }

    // Rebuilding the grid destroys the very button that is being pressed, so it waits for the change
    // pass instead of tearing the chip out from under the press. -xlinka
    private void MarkSwatchesDirty()
    {
        _swatchesDirty = true;
        MarkChangeDirty();
    }

    private void ApplySwatch(color value)
    {
        RgbToHsv(value, ref _hue, ref _sat, out _val);
        WriteColor(value);
        SyncControls(value);
        ColorSwatchStore.PushRecent(value);
        MarkSwatchesDirty();
    }

    // EYEDROPPER

    public bool IsSampling => _sampling;

    // Arming steals the mode off whatever panel had it: one pointer, one eyedropper. The state lives
    // in a plain field and a static, never a sync member - the hand tool that consumes the press is
    // the LOCAL user's, so the arm is local too. -xlinka
    public void SetSampling(bool sampling)
    {
        if (sampling && (IsDestroyed || Slot == null || Slot.IsDestroyed))
            return;
        if (_sampling == sampling)
            return;

        if (sampling)
        {
            var previous = ActiveSampler;
            if (previous != null && !ReferenceEquals(previous, this))
                previous.SetSampling(false);
            ActiveSampler = this;
        }
        else if (ReferenceEquals(ActiveSampler, this))
        {
            ActiveSampler = null;
        }

        _sampling = sampling;
        RefreshPickButton();
    }

    public void DisarmSampling() => SetSampling(false);

    // The color the world handed back, committed through the same live-write path as every slider drag,
    // so it lands in the SAME undo session: one Save, one record, whatever mix of drags, hex commits,
    // swatches and picks got you there. -xlinka
    public void ApplySampledColor(colorHDR sampled)
    {
        if (IsDestroyed)
            return;

        ApplySwatch(ToPickerColor(sampled));
        SetSampling(false);
    }

    // Every control on this panel is LDR - sliders 0..1, byte fields 0..255, hex - so an overbright
    // sample has to saturate on the way in or the field ends up holding a value the picker cannot show
    // you, let alone edit. Saturate rather than tone-map: a picked color is meant to match what you
    // pointed at, and a clamp keeps the hue. -xlinka
    private static color ToPickerColor(colorHDR sampled)
    {
        return new color(
            System.Math.Clamp(sampled.r, 0f, 1f),
            System.Math.Clamp(sampled.g, 0f, 1f),
            System.Math.Clamp(sampled.b, 0f, 1f),
            System.Math.Clamp(sampled.a, 0f, 1f));
    }

    private void RefreshPickButton()
    {
        var label = _pickLabel.Target;
        if (label != null && !label.IsDestroyed)
            label.Content.Value = _sampling ? PickArmedLabel : PickIdleLabel;

        var button = _pickButton.Target;
        if (button != null && !button.IsDestroyed)
        {
            InspectorUI.ApplyStateTint(button.Slot.GetComponent<Image>()?.Tint,
                _sampling ? InspectorUI.AccentColor : ButtonFill);
        }
    }

    // LAYOUT

    private void BuildLayout(UIBuilder ui)
    {
        var page = ui.Current;
        InspectorUI.ApplyTheme(ui, Slot);

        var vLayout = page.AttachComponent<VerticalLayout>();
        vLayout.Spacing.Value = 8f;
        vLayout.PaddingLeft.Value = 10f;
        vLayout.PaddingRight.Value = 10f;
        vLayout.PaddingTop.Value = 10f;
        vLayout.PaddingBottom.Value = 10f;
        vLayout.ForceExpandWidth.Value = true;
        vLayout.ForceExpandHeight.Value = false;

        BuildPreviewSection(page);
        BuildPickerSection(page);
        BuildChannelsSection(page);
        BuildSwatchSection(page);
        BuildFooter(page);
    }

    // Before on the left, after on the right, both over the checker so alpha is honest, with the two
    // hex strings under their own halves.
    private void BuildPreviewSection(Slot page)
    {
        // Eyedropper toggle rides the section header rather than costing the panel another row: the
        // label already eats the flexible width, so the button lands hard right against the rule.
        var header = InspectorUI.SectionHeader(page, "PREVIEW", Slot);
        var headerUi = new UIBuilder(header);
        InspectorUI.ApplyTheme(headerUi, Slot);
        var pickButton = InspectorUI.RelayButton(headerUi, this, "pick", PickIdleLabel, PickButtonWidth);
        _pickButton.Target = pickButton;
        _pickLabel.Target = pickButton.Slot.GetComponentInChildren<Text>();
        RefreshPickButton();

        Row(page, "Preview", PreviewHeight, out var previewUi);
        var frame = Framed(previewUi, "PreviewFrame", 0f);
        var halves = frame.AttachComponent<HorizontalLayout>();
        halves.Spacing.Value = FrameInset;
        halves.ForceExpandWidth.Value = true;
        halves.ForceExpandHeight.Value = true;

        var halvesUi = new UIBuilder(frame);
        InspectorUI.ApplyTheme(halvesUi, Slot);
        _beforeSwatch.Target = BuildPreviewHalf(halvesUi, "Before", _original);
        _afterSwatch.Target = BuildPreviewHalf(halvesUi, "After", _original);

        Row(page, "PreviewHex", CaptionHeight, out var hexUi);
        _beforeHexLabel.Target = BuildCenteredLabel(hexUi, ColorSwatchStore.FormatHexShort(_original));
        _afterHexLabel.Target = BuildCenteredLabel(hexUi, ColorSwatchStore.FormatHexShort(_original));
    }

    private Image BuildPreviewHalf(UIBuilder ui, string name, color value)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var half = ui.Next(name);
        var checker = half.AttachComponent<RawImage>();
        checker.Texture.Target = _checker.Target;
        checker.Tint.Value = color.White;
        ui.PopStyle();

        var tint = half.AddSlot("Tint");
        InspectorUI.FillParent(tint.AttachComponent<RectTransform>());
        var image = tint.AttachComponent<Image>();
        image.Tint.Value = value;
        return image;
    }

    private void BuildPickerSection(Slot page)
    {
        InspectorUI.SectionHeader(page, "PICKER", Slot);

        Row(page, "Picker", PickerHeight, out var pickerUi);

        var square = BuildPad(Framed(pickerUi, "SquareFrame", 0f), new float2(_sat, _val), OnSquareChanged);
        _svMaterial.Target = AttachGradient(square, ColorGradientMode.SaturationValue);
        _svSquare.Target = square;

        var hue = BuildPad(Framed(pickerUi, "HueFrame", StripWidth), new float2(0.5f, _hue / 360f), OnHueStripChanged);
        AttachGradient(hue, ColorGradientMode.HueStrip).Vertical.Value = true;
        _hueStrip.Target = hue;

        var alpha = BuildPad(Framed(pickerUi, "AlphaFrame", StripWidth), new float2(0.5f, 1f), OnAlphaStripChanged);
        var alphaMaterial = AttachGradient(alpha, ColorGradientMode.AlphaRamp);
        alphaMaterial.Vertical.Value = true;
        alphaMaterial.CheckerPx.Value = 8f;
        _alphaMaterial.Target = alphaMaterial;
        _alphaStrip.Target = alpha;
    }

    private void BuildChannelsSection(Slot page)
    {
        InspectorUI.SectionHeader(page, "CHANNELS", Slot);

        _sliderR.Target = BuildChannelRow(page, "R", InspectorUI.AxisXColor, OnRedChanged, OnRedSubmitted, out var inputR);
        _inputR.Target = inputR;
        _sliderG.Target = BuildChannelRow(page, "G", InspectorUI.AxisYColor, OnGreenChanged, OnGreenSubmitted, out var inputG);
        _inputG.Target = inputG;
        _sliderB.Target = BuildChannelRow(page, "B", InspectorUI.AxisZColor, OnBlueChanged, OnBlueSubmitted, out var inputB);
        _inputB.Target = inputB;
        _sliderA.Target = BuildChannelRow(page, "A", InspectorUI.MutedColor, OnAlphaChanged, OnAlphaSubmitted, out var inputA);
        _inputA.Target = inputA;

        Row(page, "Hex", InspectorUI.RowHeight, out var hexUi);
        BuildLabelChip(hexUi, "#", InspectorUI.MutedColor);
        hexUi.PushStyle();
        hexUi.FlexibleWidth(1f);
        var hexInput = InspectorUI.CreateTextInput(hexUi, "HexInput");
        hexInput.SetSubmitAction(OnHexSubmitted);
        _hexInput.Target = hexInput;
        hexUi.PopStyle();
    }

    private void BuildSwatchSection(Slot page)
    {
        InspectorUI.SectionHeader(page, "SWATCHES", Slot);

        Row(page, "SwatchMode", InspectorUI.RowHeight, out var modeUi);
        modeUi.PushStyle();
        modeUi.FlexibleWidth(1f);
        var hint = modeUi.Text("", InspectorUI.FontSize - 2f, InspectorUI.MutedColor);
        InspectorUI.FillParent(hint.RectTransform!);
        hint.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        hint.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        _modeHint.Target = hint;
        modeUi.PopStyle();

        var modeButton = InspectorUI.RelayButton(modeUi, this, "swatchmode", "Remove", 96f);
        _modeButton.Target = modeButton;
        _modeLabel.Target = modeButton.Slot.GetComponentInChildren<Text>();

        // The grid scrolls on its own so a big palette never pushes Save off the bottom of the panel.
        var host = page.AddSlot("Swatches");
        host.AttachComponent<RectTransform>();
        var hostLE = host.AttachComponent<LayoutElement>();
        hostLE.MinHeight.Value = SwatchAreaHeight;
        hostLE.PreferredHeight.Value = SwatchAreaHeight;
        hostLE.FlexibleHeight.Value = 0f;

        var scrollUi = new UIBuilder(host);
        InspectorUI.ApplyTheme(scrollUi, Slot);
        var scroll = scrollUi.ScrollRect(out var content, null, InspectorUI.PaneColor);
        InspectorUI.FillParent(scroll.Slot.GetComponent<RectTransform>()!);

        var contentLayout = content.Slot.AttachComponent<VerticalLayout>();
        contentLayout.Spacing.Value = 6f;
        contentLayout.PaddingLeft.Value = 6f;
        contentLayout.PaddingRight.Value = 6f;
        contentLayout.PaddingTop.Value = 6f;
        contentLayout.PaddingBottom.Value = 6f;
        contentLayout.ForceExpandWidth.Value = true;
        contentLayout.ForceExpandHeight.Value = false;
        _swatchHost.Target = content.Slot;
    }

    private void BuildFooter(Slot page)
    {
        Row(page, "Actions", 38f, out var actionsUi);

        actionsUi.PushStyle();
        actionsUi.BackgroundColor(ButtonFill);
        actionsUi.TextColor(InspectorUI.MutedColor);
        InspectorUI.RelayButton(actionsUi, this, "cancel", "Cancel", 0f);
        actionsUi.PopStyle();

        actionsUi.PushStyle();
        actionsUi.BackgroundColor(InspectorUI.AccentColor);
        actionsUi.TextColor(color.White);
        InspectorUI.RelayButton(actionsUi, this, "save", "Save", 0f);
        actionsUi.PopStyle();
    }

    // SWATCH GRID

    private void RebuildSwatches()
    {
        bool remove = RemoveMode.Value;

        var label = _modeLabel.Target;
        if (label != null && !label.IsDestroyed)
            label.Content.Value = remove ? "Done" : "Remove";

        var hint = _modeHint.Target;
        if (hint != null && !hint.IsDestroyed)
            hint.Content.Value = remove ? "tap a saved chip to delete" : "tap a chip to apply";

        var modeButton = _modeButton.Target;
        if (modeButton != null && !modeButton.IsDestroyed)
        {
            InspectorUI.ApplyStateTint(modeButton.Slot.GetComponent<Image>()?.Tint,
                remove ? InspectorUI.DangerColor : ButtonFill);
        }

        var host = _swatchHost.Target;
        if (host == null || host.IsDestroyed)
            return;
        host.DestroyChildren();

        BuildCaption(host, "Saved");
        BuildChipGrid(host, ColorSwatchStore.Saved, "swatch", remove, addChip: true);
        BuildCaption(host, "Recent");
        BuildChipGrid(host, ColorSwatchStore.Recent, "recent", false, addChip: false);

        // Row count changed under the sizer's latch; without this the scroll keeps the old pin.
        host.GetComponent<ScrollContentSizer>()?.Invalidate();
    }

    private void BuildChipGrid(Slot host, IReadOnlyList<color> colors, string prefix, bool removeMode, bool addChip)
    {
        int total = colors.Count + (addChip ? 1 : 0);
        if (total == 0)
            return;

        int rows = (total + ChipsPerRow - 1) / ChipsPerRow;
        for (int row = 0; row < rows; row++)
        {
            var rowSlot = host.AddSlot("ChipRow");
            rowSlot.AttachComponent<RectTransform>();
            var le = rowSlot.AttachComponent<LayoutElement>();
            le.MinHeight.Value = ChipSize;
            le.PreferredHeight.Value = ChipSize;
            le.FlexibleHeight.Value = 0f;
            var layout = rowSlot.AttachComponent<HorizontalLayout>();
            layout.Spacing.Value = ChipGap;
            layout.ForceExpandWidth.Value = false;
            layout.ForceExpandHeight.Value = true;
            layout.MainAlignment.Value = MainAxisAlignment.Start;
            rowSlot.AttachComponent<GraphicChunkRoot>();

            var ui = new UIBuilder(rowSlot);
            InspectorUI.ApplyTheme(ui, Slot);
            for (int column = 0; column < ChipsPerRow; column++)
            {
                int index = row * ChipsPerRow + column;
                if (index >= total)
                    break;
                if (addChip && index == colors.Count)
                    BuildAddChip(ui);
                else
                    BuildColorChip(ui, colors[index], prefix + ":" + index.ToString(CultureInfo.InvariantCulture), removeMode);
            }
        }
    }

    private void BuildColorChip(UIBuilder ui, color value, string argument, bool removeMode)
    {
        var chip = BeginChip(ui, removeMode ? InspectorUI.DangerColor : FrameColor, argument);

        var inner = InsetChild(chip, "Inner");
        var checker = inner.AttachComponent<RawImage>();
        checker.Texture.Target = _checker.Target;
        checker.Tint.Value = color.White;

        var tint = inner.AddSlot("Tint");
        InspectorUI.FillParent(tint.AttachComponent<RectTransform>());
        tint.AttachComponent<Image>().Tint.Value = value;

        if (!removeMode)
            return;

        var markUi = new UIBuilder(inner);
        InspectorUI.ApplyTheme(markUi, Slot);
        var mark = markUi.Text("X", InspectorUI.FontSize + 2f, color.White);
        InspectorUI.FillParent(mark.RectTransform!);
        mark.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        mark.VerticalAlignment.Value = TextVerticalAlignment.Middle;
    }

    private void BuildAddChip(UIBuilder ui)
    {
        var chip = BeginChip(ui, ButtonFill, "swatchadd");
        var inner = InsetChild(chip, "Inner");
        var fill = inner.AttachComponent<Image>();
        fill.Tint.Value = InspectorUI.RowColor;

        var markUi = new UIBuilder(inner);
        InspectorUI.ApplyTheme(markUi, Slot);
        var mark = markUi.Text("+", InspectorUI.FontSize + 4f, InspectorUI.TextColor);
        InspectorUI.FillParent(mark.RectTransform!);
        mark.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        mark.VerticalAlignment.Value = TextVerticalAlignment.Middle;
    }

    // The chip's own graphic is its border, so the button's interaction driver tints the frame on
    // hover and the color inside stays exactly the color it claims to be. -xlinka
    private Slot BeginChip(UIBuilder ui, color border, string argument)
    {
        ui.PushStyle();
        ui.MinWidth(ChipSize);
        ui.PreferredWidth(ChipSize);
        ui.FlexibleWidth(0f);
        ui.MinHeight(ChipSize);
        ui.PreferredHeight(ChipSize);
        ui.FlexibleHeight(0f);
        var chip = ui.Next("Chip");
        ui.PopStyle();

        var frame = chip.AttachComponent<Image>();
        frame.Tint.Value = border;
        var button = chip.AttachComponent<Button>();
        var relay = chip.AttachComponent<InspectorButtonRelay>();
        relay.Argument.Value = argument;
        relay.Handler.Target = this;
        button.SetAction(relay.OnPressed);
        return chip;
    }

    private void BuildCaption(Slot host, string label)
    {
        var row = host.AddSlot("Caption");
        row.AttachComponent<RectTransform>();
        var le = row.AttachComponent<LayoutElement>();
        le.MinHeight.Value = CaptionHeight;
        le.PreferredHeight.Value = CaptionHeight;
        le.FlexibleHeight.Value = 0f;

        var ui = new UIBuilder(row);
        InspectorUI.ApplyTheme(ui, Slot);
        var text = ui.Text(label, InspectorUI.FontSize - 2f, InspectorUI.CyanColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
    }

    // SHARED WIDGET BUILDERS

    // House row with the panel's gutter instead of the tighter member-row spacing.
    private Slot Row(Slot parent, string name, float height, out UIBuilder rowUi)
    {
        var row = InspectorUI.FixedRow(parent, name, height, out rowUi, Slot);
        var layout = row.GetComponent<HorizontalLayout>();
        if (layout != null)
            layout.Spacing.Value = ChipGap;
        return row;
    }

    // A framed surface: the outer slot is the rule, the returned child is the inset area to fill.
    // Bare gradient strips floating on the pane background is what read as unfinished. -xlinka
    private static Slot Framed(UIBuilder ui, string name, float width)
    {
        ui.PushStyle();
        if (width > 0f)
        {
            ui.MinWidth(width);
            ui.PreferredWidth(width);
            ui.FlexibleWidth(0f);
        }
        else
        {
            ui.FlexibleWidth(1f);
        }
        var frame = ui.Next(name);
        ui.PopStyle();

        var border = frame.AttachComponent<Image>();
        border.Tint.Value = FrameColor;
        return InsetChild(frame, "Inner");
    }

    private static Slot InsetChild(Slot parent, string name)
    {
        var inner = parent.AddSlot(name);
        var rect = inner.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = new float2(FrameInset, FrameInset);
        rect.OffsetMax.Value = new float2(-FrameInset, -FrameInset);
        return inner;
    }

    private Pad2D BuildPad(Slot host, float2 initial, Action<Pad2D, float2> action)
    {
        var ui = new UIBuilder(host);
        InspectorUI.ApplyTheme(ui, Slot);
        var pad = ui.Pad2D(initial, action);
        InspectorUI.FillParent(pad.Slot.GetComponent<RectTransform>()!);
        StyleHandleRing(pad);
        return pad;
    }

    // The builder's flat dot vanishes into whatever color it is sitting on, which on a full
    // saturation/value square is every color there is. A light ring with a dark outline reads on all
    // of them. -xlinka
    private static void StyleHandleRing(Pad2D pad)
    {
        var handle = pad.Slot.FindChild("Handle", recursive: false);
        if (handle == null)
            return;

        var rect = handle.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.OffsetMin.Value = new float2(-10f, -10f);
            rect.OffsetMax.Value = new float2(10f, 10f);
        }

        var dot = handle.GetComponent<Image>();
        if (dot != null)
        {
            // Drop the interaction driver first: it is what would keep writing the dead dot's tint.
            // Snapshot before destroying - GetComponents walks the live component list.
            var stale = new List<ColorDriver>();
            foreach (var driver in pad.Slot.GetComponents<ColorDriver>())
            {
                if (ReferenceEquals(driver.Target.Target, dot.Tint))
                    stale.Add(driver);
            }
            foreach (var driver in stale)
                driver.Destroy();
            dot.Destroy();
        }

        var ring = handle.AttachComponent<ArcSegment>();
        ring.AngleStart.Value = 0f;
        ring.ArcLength.Value = 360f;
        ring.InnerRadius.Value = 6f;
        ring.OuterRadius.Value = 9.5f;
        ring.Tint.Value = new color(0.97f, 0.97f, 1f, 1f);
        ring.OutlineColor.Value = new color(0.04f, 0.04f, 0.06f, 1f);
        ring.OutlineThickness.Value = 2f;
    }

    private static ColorGradientMaterial AttachGradient(Pad2D pad, ColorGradientMode mode)
    {
        var material = pad.Slot.AttachComponent<ColorGradientMaterial>();
        material.Mode.Value = mode;
        var background = pad.Slot.GetComponent<Image>();
        if (background != null)
        {
            // White tint so the material's own color is what shows.
            background.Tint.Value = color.White;
            background.Material.Target = material;
        }
        return material;
    }

    private Slider BuildChannelRow(Slot page, string label, color tint,
        Action<Slider, float> changed, Action<TextInput, string> submitted, out TextInput input)
    {
        Row(page, label, InspectorUI.RowHeight, out var ui);
        BuildLabelChip(ui, label, tint);

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var slider = ui.Slider(0f, 0f, 1f, changed, TrackColor);
        // The slider builder stamps a fixed 96x24 element over the pushed style, which is what left
        // these rows as stubby bars floating in dead space.
        var sliderLE = slider.Slot.GetComponent<LayoutElement>();
        if (sliderLE != null)
        {
            sliderLE.MinWidth.Value = 80f;
            sliderLE.PreferredWidth.Value = 80f;
            sliderLE.FlexibleWidth.Value = 1f;
            sliderLE.MinHeight.Value = 0f;
            sliderLE.PreferredHeight.Value = InspectorUI.RowHeight;
            sliderLE.FlexibleHeight.Value = 1f;
        }
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(56f);
        ui.PreferredWidth(56f);
        ui.FlexibleWidth(0f);
        input = InspectorUI.CreateTextInput(ui, label + "Byte");
        input.SetSubmitAction(submitted);
        ui.PopStyle();
        return slider;
    }

    private static void BuildLabelChip(UIBuilder ui, string label, color tint)
    {
        ui.PushStyle();
        ui.MinWidth(26f);
        ui.PreferredWidth(26f);
        ui.FlexibleWidth(0f);
        var chip = ui.Next("Chip");
        var backing = chip.AttachComponent<Image>();
        backing.Tint.Value = new color(tint.r, tint.g, tint.b, 0.28f);
        ui.NestInto(chip);
        var text = ui.Text(label, InspectorUI.FontSize, tint);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.NestOut();
        ui.PopStyle();
    }

    private static Text BuildCenteredLabel(UIBuilder ui, string content)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var text = ui.Text(content, InspectorUI.FontSize - 2f, InspectorUI.MutedColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
        return text;
    }

    // INPUT HANDLERS - user gestures only (programmatic Value writes never re-invoke actions).

    [SyncMethod]
    public void OnSquareChanged(Pad2D pad, float2 value)
    {
        _sat = value.x;
        _val = value.y;
        WriteHsv();
    }

    [SyncMethod]
    public void OnHueStripChanged(Pad2D pad, float2 value)
    {
        _hue = value.y * 360f;
        WriteHsv();
    }

    [SyncMethod]
    public void OnAlphaStripChanged(Pad2D pad, float2 value) => WriteChannel(3, value.y);

    [SyncMethod]
    public void OnRedChanged(Slider slider, float value) => WriteChannel(0, value);
    [SyncMethod]
    public void OnGreenChanged(Slider slider, float value) => WriteChannel(1, value);
    [SyncMethod]
    public void OnBlueChanged(Slider slider, float value) => WriteChannel(2, value);
    [SyncMethod]
    public void OnAlphaChanged(Slider slider, float value) => WriteChannel(3, value);

    [SyncMethod]
    public void OnRedSubmitted(TextInput input, string text) => SubmitByte(0, text);
    [SyncMethod]
    public void OnGreenSubmitted(TextInput input, string text) => SubmitByte(1, text);
    [SyncMethod]
    public void OnBlueSubmitted(TextInput input, string text) => SubmitByte(2, text);
    [SyncMethod]
    public void OnAlphaSubmitted(TextInput input, string text) => SubmitByte(3, text);

    // #RRGGBB or #RRGGBBAA, applied when editing finishes.
    [SyncMethod]
    public void OnHexSubmitted(TextInput input, string text)
    {
        if (IsDestroyed || !ColorSwatchStore.TryParseHex(text, out var parsed))
        {
            // Snap the box back to the real value rather than leaving junk sitting in it.
            var current = ReadColor();
            if (current != null)
                SyncControls(current.Value);
            return;
        }
        RgbToHsv(parsed, ref _hue, ref _sat, out _val);
        WriteColor(parsed);
        SyncControls(parsed);
    }

    // The byte fields are the precise way in: 0-255 per channel, anything else snaps back.
    private void SubmitByte(int channel, string text)
    {
        if (IsDestroyed)
            return;
        var current = ReadColor();
        if (current == null)
            return;
        if (!float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float raw))
        {
            SyncControls(current.Value);
            return;
        }
        if (raw < 0f) raw = 0f;
        if (raw > 255f) raw = 255f;
        WriteChannel(channel, raw / 255f);
    }

    private void WriteChannel(int channel, float value)
    {
        var current = ReadColor();
        if (current == null)
            return;
        var c = current.Value;
        switch (channel)
        {
            case 0: c.r = value; break;
            case 1: c.g = value; break;
            case 2: c.b = value; break;
            case 3: c.a = value; break;
        }
        if (channel != 3)
            RgbToHsv(c, ref _hue, ref _sat, out _val); // keep HSV state tracking manual RGB edits
        WriteColor(c);
        SyncControls(c);
    }

    private void WriteHsv()
    {
        var current = ReadColor();
        if (current == null)
            return;
        var rgb = HsvToRgb(_hue, _sat, _val);
        rgb.a = current.Value.a;
        WriteColor(rgb);
        SyncControls(rgb);
    }

    // FIELD I/O - the value can be color or colorHDR; edits round-trip through plain color (LDR range;
    // HDR overbright values keep editing through the row's text fields).

    private color? ReadColor()
    {
        var field = Field;
        var accessor = Accessor;
        if (field == null || field.IsDestroyed || accessor == null)
            return null;
        try
        {
            return accessor.GetValue(field.BoxedValue) switch
            {
                color c => c,
                colorHDR hdr => new color(hdr.r, hdr.g, hdr.b, hdr.a),
                _ => null,
            };
        }
        catch { return null; }
    }

    private void WriteColor(color c)
    {
        var field = Field;
        var accessor = Accessor;
        if (field == null || field.IsDestroyed || accessor == null)
            return;

        // A driven field's value is derived; writing it here would be overwritten on the driver's next
        // pass anyway, so the picker reads it but never authors it. A HOOKED link is the passthrough
        // escape - it intercepts the write rather than rejecting it. Matches MemberEditor.IsReadOnly.
        if (field is ILinkable { IsDriven: true, IsHooked: false })
            return;

        if (!_undoCaptured)
        {
            _undoCaptured = true;
            _undoBefore = field.BoxedValue;
        }

        // Boxed in two statements on purpose. A ternary here picks colorHDR as the common type of both
        // arms (color converts to it implicitly), so EVERY write boxed a colorHDR and a plain color
        // member threw InvalidCastException on the first drag. -xlinka
        object leaf = c;
        if (accessor.LeafType == typeof(colorHDR))
            leaf = new colorHDR(c.r, c.g, c.b, c.a);
        object? after = accessor.SetValue(field.BoxedValue, leaf);
        field.BoxedValue = after!;
    }

    private void RefreshFromField()
    {
        var current = ReadColor();
        if (current == null)
            return;
        RgbToHsv(current.Value, ref _hue, ref _sat, out _val);
        SyncControls(current.Value);
    }

    // pushes the color into every control + the preview (programmatic, no action re-fire)
    private void SyncControls(color c)
    {
        SetSlider(_sliderR.Target, c.r);
        SetSlider(_sliderG.Target, c.g);
        SetSlider(_sliderB.Target, c.b);
        SetSlider(_sliderA.Target, c.a);

        SetByteField(_inputR.Target, c.r);
        SetByteField(_inputG.Target, c.g);
        SetByteField(_inputB.Target, c.b);
        SetByteField(_inputA.Target, c.a);

        var square = _svSquare.Target;
        if (square != null && !square.IsDestroyed)
        {
            square.Value.Value = new float2(_sat, _val);
            square.UpdateHandleDrives();
        }
        var hue = _hueStrip.Target;
        if (hue != null && !hue.IsDestroyed)
        {
            hue.Value.Value = new float2(0.5f, _hue / 360f);
            hue.UpdateHandleDrives();
        }
        var alpha = _alphaStrip.Target;
        if (alpha != null && !alpha.IsDestroyed)
        {
            alpha.Value.Value = new float2(0.5f, c.a);
            alpha.UpdateHandleDrives();
        }

        // The SV square renders the current hue; the alpha ramp fades the current color.
        var svMaterial = _svMaterial.Target;
        if (svMaterial != null && !svMaterial.IsDestroyed)
            svMaterial.Hue.Value = _hue;
        var alphaMaterial = _alphaMaterial.Target;
        if (alphaMaterial != null && !alphaMaterial.IsDestroyed)
            alphaMaterial.ColorA.Value = new colorHDR(c.r, c.g, c.b, 1f);

        var after = _afterSwatch.Target;
        if (after != null && !after.IsDestroyed)
            after.Tint.Value = c;
        var afterLabel = _afterHexLabel.Target;
        if (afterLabel != null && !afterLabel.IsDestroyed)
            afterLabel.Content.Value = ColorSwatchStore.FormatHexShort(c);

        var hex = _hexInput.Target;
        if (hex != null && !hex.IsDestroyed && !hex.IsFocused)
            hex.Text.Value = ColorSwatchStore.FormatHexShort(c);
    }

    private static void SetSlider(Slider? slider, float value)
    {
        if (slider != null && !slider.IsDestroyed)
            slider.Value.Value = value;
    }

    private static void SetByteField(TextInput? input, float value)
    {
        if (input == null || input.IsDestroyed || input.IsFocused)
            return;
        input.Text.Value = ColorSwatchStore.ToByte(value).ToString(CultureInfo.InvariantCulture);
    }

    private void RecordUndoNow()
    {
        if (!_undoCaptured || _undoRecorded || _cancelled)
            return;
        if (Field is not { IsDestroyed: false } field)
            return;
        _undoRecorded = true;
        if (Equals(_undoBefore, field.BoxedValue))
            return;
        InspectorUndo.RecordEdit(this, field, _undoBefore, field.BoxedValue);
        var committed = ReadColor();
        if (committed != null)
            ColorSwatchStore.PushRecent(committed.Value);
    }

    public override void OnDestroy()
    {
        // Closing via the X commits like Save (live edits already applied); Cancel already restored.
        RecordUndoNow();
        // A dead panel must never keep eating world presses.
        if (ReferenceEquals(ActiveSampler, this))
        {
            ActiveSampler = null;
            _sampling = false;
        }
        base.OnDestroy();
    }

    // Standard HSV conversions. Hue in degrees [0,360), s/v in [0,1]. Degenerate RGB (grey) keeps the
    // caller's existing hue/sat instead of snapping to 0.

    private static void RgbToHsv(color c, ref float hue, ref float sat, out float value)
    {
        float max = MathF.Max(c.r, MathF.Max(c.g, c.b));
        float min = MathF.Min(c.r, MathF.Min(c.g, c.b));
        float delta = max - min;
        value = max;

        if (delta > 1e-6f)
        {
            float h;
            if (max == c.r)
                h = 60f * (((c.g - c.b) / delta) % 6f);
            else if (max == c.g)
                h = 60f * ((c.b - c.r) / delta + 2f);
            else
                h = 60f * ((c.r - c.g) / delta + 4f);
            if (h < 0f) h += 360f;
            hue = h;
        }
        if (max > 1e-6f)
            sat = delta / max;
    }

    private static color HsvToRgb(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        (float r, float g, float b) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return new color(r + m, g + m, b + m, 1f);
    }
}
