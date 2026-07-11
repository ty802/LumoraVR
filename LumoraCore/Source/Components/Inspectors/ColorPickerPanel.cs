// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// spawned color picker for a color/colorHDR member: a saturation/value square you drag a cursor in,
// a vertical hue strip, a vertical alpha ramp over a checkerboard, precise R/G/B rows, a hex field,
// and Save/Cancel. every drag writes the field live so the world previews the color; Save commits
// the whole session as ONE undo step, Cancel restores the original value.
public class ColorPickerPanel : Component, IInspectorActionHandler
{
    public readonly SyncRef<IWorldElement> TargetMember;
    public readonly Sync<string> MemberPath;

    private readonly SyncRef<Image> _previewSolid;
    private readonly SyncRef<Image> _previewAlpha;
    private readonly SyncRef<Pad2D> _svSquare;
    private readonly SyncRef<Pad2D> _hueStrip;
    private readonly SyncRef<Pad2D> _alphaStrip;
    private readonly SyncRef<ColorGradientMaterial> _svMaterial;
    private readonly SyncRef<ColorGradientMaterial> _alphaMaterial;
    private readonly SyncRef<TextInput> _hexInput;

    private Slider?[] _rgbSliders = new Slider?[3];

    // Hue/sat persist through degenerate RGB (black/grey has no hue of its own), so dragging V to 0
    // and back doesn't snap hue to red.
    private float _hue;
    private float _sat = 1f;
    private float _val = 1f;

    private object? _undoBefore;
    private bool _undoCaptured;
    private bool _undoRecorded;
    private bool _cancelled;

    public ColorPickerPanel()
    {
        TargetMember = new SyncRef<IWorldElement>(this);
        MemberPath = new Sync<string>(this, "");
        _previewSolid = new SyncRef<Image>(this);
        _previewAlpha = new SyncRef<Image>(this);
        _svSquare = new SyncRef<Pad2D>(this);
        _hueStrip = new SyncRef<Pad2D>(this);
        _alphaStrip = new SyncRef<Pad2D>(this);
        _svMaterial = new SyncRef<ColorGradientMaterial>(this);
        _alphaMaterial = new SyncRef<ColorGradientMaterial>(this);
        _hexInput = new SyncRef<TextInput>(this);
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
        theme.PanelBackground.Value = new color(0.075f, 0.07f, 0.115f, 1f);
        theme.Header.Value = new color(0.11f, 0.10f, 0.17f, 1f);
        theme.ButtonFill.Value = new color(0.22f, 0.20f, 0.34f, 1f);
        theme.Accent.Value = InspectorUI.AccentColor;
        theme.Separator.Value = new color(0.52f, 0.46f, 0.82f, 0.6f);
        theme.Border.Value = new color(0.52f, 0.46f, 0.82f, 0.45f);

        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Color";
        shell.Size.Value = new float2(520f, 720f);
        theme.ApplyTo(shell);

        shell.RebuildContent(BuildLayout);
        RefreshFromField();
    }

    public override void OnUpdate(float delta)
    {
        // Close the husk when the edited member dies with its component.
        if (World?.IsAuthority == true && TargetMember.RawTarget is { IsDestroyed: true })
            Slot.Destroy();
    }

    // save commits the session; cancel restores the original value. both close the panel.
    public void HandleInspectorAction(string argument)
    {
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
        }
    }

    private void BuildLayout(UIBuilder ui)
    {
        var page = ui.Current;
        InspectorUI.ApplyTheme(ui, Slot);

        var vLayout = page.AttachComponent<Helio.UI.Layout.VerticalLayout>();
        vLayout.Spacing.Value = 8f;
        vLayout.PaddingLeft.Value = 10f;
        vLayout.PaddingRight.Value = 10f;
        vLayout.PaddingTop.Value = 10f;
        vLayout.PaddingBottom.Value = 10f;
        vLayout.ForceExpandWidth.Value = true;
        vLayout.ForceExpandHeight.Value = false;

        // Preview: the color solid on the left, its real alpha over a dark backdrop on the right.
        InspectorUI.FixedRow(page, "Preview", 44f, out var previewUi, Slot);
        previewUi.PushStyle();
        previewUi.FlexibleWidth(1f);
        var solidSlot = previewUi.Next("Solid");
        _previewSolid.Target = solidSlot.AttachComponent<Image>();
        var alphaSlot = previewUi.Next("Alpha");
        var alphaBackdrop = alphaSlot.AttachComponent<Image>();
        alphaBackdrop.Tint.Value = new color(0.05f, 0.05f, 0.08f, 1f);
        var alphaChild = alphaSlot.AddSlot("Tint");
        InspectorUI.FillParent(alphaChild.AttachComponent<RectTransform>());
        _previewAlpha.Target = alphaChild.AttachComponent<Image>();
        previewUi.PopStyle();

        InspectorUI.FixedRow(page, "Picker", 280f, out var pickerUi, Slot);

        pickerUi.PushStyle();
        pickerUi.FlexibleWidth(1f);
        var square = pickerUi.Pad2D(new float2(_sat, _val), OnSquareChanged);
        ConfigurePadArea(square, flexible: true, width: 0f);
        var svMaterial = AttachGradient(square, ColorGradientMode.SaturationValue);
        _svMaterial.Target = svMaterial;
        _svSquare.Target = square;
        pickerUi.PopStyle();

        pickerUi.PushStyle();
        pickerUi.MinWidth(36f);
        pickerUi.PreferredWidth(36f);
        pickerUi.FlexibleWidth(0f);
        var hue = pickerUi.Pad2D(new float2(0.5f, _hue / 360f), OnHueStripChanged);
        ConfigurePadArea(hue, flexible: false, width: 36f);
        var hueMaterial = AttachGradient(hue, ColorGradientMode.HueStrip);
        hueMaterial.Vertical.Value = true;
        _hueStrip.Target = hue;

        var alphaPad = pickerUi.Pad2D(new float2(0.5f, 1f), OnAlphaStripChanged);
        ConfigurePadArea(alphaPad, flexible: false, width: 36f);
        var alphaMaterial = AttachGradient(alphaPad, ColorGradientMode.AlphaRamp);
        alphaMaterial.Vertical.Value = true;
        alphaMaterial.CheckerPx.Value = 8f;
        _alphaMaterial.Target = alphaMaterial;
        _alphaStrip.Target = alphaPad;
        pickerUi.PopStyle();

        _rgbSliders[0] = BuildSliderRow(page, "R", OnRedChanged);
        _rgbSliders[1] = BuildSliderRow(page, "G", OnGreenChanged);
        _rgbSliders[2] = BuildSliderRow(page, "B", OnBlueChanged);

        // Hex row: #RRGGBB or #RRGGBBAA, applied when editing finishes.
        InspectorUI.FixedRow(page, "Hex", 34f, out var hexUi, Slot);
        hexUi.PushStyle();
        hexUi.MinWidth(46f);
        hexUi.PreferredWidth(46f);
        hexUi.FlexibleWidth(0f);
        var hexLabel = hexUi.Text("Hex", InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(hexLabel.RectTransform!);
        hexLabel.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        hexUi.PopStyle();
        hexUi.PushStyle();
        hexUi.FlexibleWidth(1f);
        var hexInput = InspectorUI.CreateTextInput(hexUi, "HexInput");
        hexInput.EditingFinished += OnHexCommitted;
        _hexInput.Target = hexInput;
        hexUi.PopStyle();

        // Cancel restores the original; Save commits the session as one undo step.
        InspectorUI.FixedRow(page, "Actions", 38f, out var actionsUi, Slot);
        actionsUi.PushStyle();
        actionsUi.FlexibleWidth(1f);
        InspectorUI.RelayButton(actionsUi, this, "cancel", "Cancel", 0f);
        actionsUi.PushStyle();
        actionsUi.TextColor(InspectorUI.AccentColor);
        InspectorUI.RelayButton(actionsUi, this, "save", "Save", 0f);
        actionsUi.PopStyle();
        actionsUi.PopStyle();
    }

    // The Pad2D builder authors a fixed 96x96 element; retune its layout for our block and point its
    // background Image at a gradient material (tint white so the material's color comes through).
    private static void ConfigurePadArea(Pad2D pad, bool flexible, float width)
    {
        var layout = pad.Slot.GetComponent<Helio.UI.Layout.LayoutElement>();
        if (layout != null)
        {
            layout.MinWidth.Value = flexible ? 200f : width;
            layout.PreferredWidth.Value = flexible ? 260f : width;
            layout.FlexibleWidth.Value = flexible ? 1f : 0f;
            layout.MinHeight.Value = 260f;
            layout.PreferredHeight.Value = 272f;
        }
    }

    private ColorGradientMaterial AttachGradient(Pad2D pad, ColorGradientMode mode)
    {
        var material = pad.Slot.AttachComponent<ColorGradientMaterial>();
        material.Mode.Value = mode;
        var background = pad.Slot.GetComponent<Image>();
        if (background != null)
        {
            background.Tint.Value = color.White;
            background.Material.Target = material;
        }
        return material;
    }

    private Slider BuildSliderRow(Slot page, string label, Action<Slider, float> action)
    {
        InspectorUI.FixedRow(page, label, 32f, out var ui, Slot);

        ui.PushStyle();
        ui.MinWidth(28f);
        ui.PreferredWidth(28f);
        ui.FlexibleWidth(0f);
        var text = ui.Text(label, InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var slider = ui.Slider(0f, 0f, 1f, action, new color(0.16f, 0.15f, 0.24f, 0.96f));
        InspectorUI.FillParent(slider.RectTransform!);
        ui.PopStyle();
        return slider;
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

    private void OnHexCommitted(TextInput input, string text)
    {
        if (IsDestroyed || string.IsNullOrWhiteSpace(text))
            return;
        text = text.Trim().TrimStart('#');
        if (text.Length != 6 && text.Length != 8)
            return;
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
            return;

        float a = 1f;
        if (text.Length == 8)
        {
            a = (packed & 0xFF) / 255f;
            packed >>= 8;
        }
        var c = new color(
            ((packed >> 16) & 0xFF) / 255f,
            ((packed >> 8) & 0xFF) / 255f,
            (packed & 0xFF) / 255f,
            a);
        RgbToHsv(c, ref _hue, ref _sat, out _val);
        WriteColor(c);
        SyncControls(c);
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

        object leaf = accessor.LeafType == typeof(colorHDR)
            ? new colorHDR(c.r, c.g, c.b, c.a)
            : c;
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
        if (_rgbSliders[0] is { } r) r.Value.Value = c.r;
        if (_rgbSliders[1] is { } g) g.Value.Value = c.g;
        if (_rgbSliders[2] is { } b) b.Value.Value = c.b;

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

        var solid = _previewSolid.Target;
        if (solid != null && !solid.IsDestroyed)
            solid.Tint.Value = new color(c.r, c.g, c.b, 1f);
        var alphaPreview = _previewAlpha.Target;
        if (alphaPreview != null && !alphaPreview.IsDestroyed)
            alphaPreview.Tint.Value = c;

        var hex = _hexInput.Target;
        if (hex != null && !hex.IsDestroyed && !hex.IsFocused)
        {
            hex.Text.Value = c.a >= 0.999f
                ? $"#{ToByte(c.r):X2}{ToByte(c.g):X2}{ToByte(c.b):X2}"
                : $"#{ToByte(c.r):X2}{ToByte(c.g):X2}{ToByte(c.b):X2}{ToByte(c.a):X2}";
        }
    }

    private static int ToByte(float v)
    {
        int b = (int)MathF.Round(v * 255f);
        return b < 0 ? 0 : (b > 255 ? 255 : b);
    }

    private void RecordUndoNow()
    {
        if (!_undoCaptured || _undoRecorded || _cancelled)
            return;
        if (Field is not { IsDestroyed: false } field)
            return;
        _undoRecorded = true;
        if (!Equals(_undoBefore, field.BoxedValue))
            InspectorUndo.RecordEdit(this, field, _undoBefore, field.BoxedValue);
    }

    public override void OnDestroy()
    {
        // Closing via the X commits like Save (live edits already applied); Cancel already restored.
        RecordUndoNow();
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
