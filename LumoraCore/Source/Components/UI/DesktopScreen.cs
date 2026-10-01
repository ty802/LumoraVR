// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Components.Assets;
using Lumora.Core.Desktop;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The Desktop tab: the real desktop on the dash, one monitor at a time, driven from inside VR.
//
// The picture is a stack of horizontal strips, one texture each, so a change in one corner of the screen
// re-uploads one strip. The pointer is drawn apart from the picture, the way the system hands it over.
//
// Capture runs only while this tab is on show and the dash is open; a closed dash costs nothing. What
// the page shows never leaves this machine: it lives in userspace, and the textures refuse to capture
// anywhere else.
//
// The picture is the page: it fills the window, keeping its own shape. The controls are one slim strip along
// the top, always one press away (which monitor, follow the mouse between monitors, the keyboard, Start,
// brightness), and what the page can and cannot do right now is one line along the bottom. -xlinka
[ComponentCategory("Hidden")]
public sealed class DesktopScreen : WidgetScreen, IDashboardKeyInput
{
    private const float StripHeight = 34f;
    private const float StatusHeight = 20f;
    private const float CellWidth = 34f;
    private const float CellGap = 6f;
    private const float Pad = DashTheme.Gap;
    private const double DisplayRefreshSeconds = 5.0;

    private IReadOnlyList<DesktopDisplay> _displays = Array.Empty<DesktopDisplay>();
    private DesktopDisplay? _display;
    private DesktopCapture? _held;
    private DesktopCapture? _builtFor;
    private int _builtLayout = -1;
    private double _nextDisplayRefresh;

    private AspectRatioConstraint? _aspect;
    private DesktopPointerSurface? _surface;
    private Slot? _strips;
    private readonly List<(Slot Slot, DesktopTextureProvider Texture, RawImage Image)> _stripParts = new();
    private Slot? _pointer;
    private RectTransform? _pointerRect;
    private DesktopTextureProvider? _pointerTexture;
    private Text? _waiting;

    private Slot? _displayRow;
    private readonly List<(DesktopDisplay Display, Cell Cell)> _displayButtons = new();
    private Cell? _follow;
    private Cell? _keyboardCell;
    private Text? _brightnessValue;
    private Text? _status;

    private bool _keyboard;
    private bool _refused;
    private int _uploadsSeen;
    private Canvas? _canvas;

    // A button on the strip, and the driver that paints its states.
    private sealed record Cell(BorderedImage Panel, Text Label, ColorDriver Driver);

    private static bool InVr => Lumora.Core.Engine.Current?.InputInterface?.IsVRActive == true;

    private static IDesktopInput? Input => DesktopPlatform.Backend?.Input;

    // BUILD

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        var root = builder.Current;
        BuildStrip(root);

        // Everything between the strip and the status line, the picture centred in it at its own shape.
        var viewport = root.AddSlot("Viewport");
        Pin(viewport.AttachComponent<RectTransform>(), float2.Zero, float2.One, new float2(Pad, Pad + StatusHeight + 4f),
            new float2(-Pad, -(Pad + StripHeight + Pad)));

        var picture = viewport.AddSlot("Picture");
        Pin(picture.AttachComponent<RectTransform>(), float2.Zero, float2.One, float2.Zero, float2.Zero);
        _aspect = picture.AttachComponent<AspectRatioConstraint>();
        _aspect.Mode.Value = AspectRatioConstraint.AspectMode.FitInParent;
        _aspect.AspectRatio.Value = 16f / 9f;
        picture.AttachComponent<GraphicChunkRoot>();
        _surface = picture.AttachComponent<DesktopPointerSurface>();

        _strips = picture.AddSlot("Strips");
        Pin(_strips.AttachComponent<RectTransform>(), float2.Zero, float2.One, float2.Zero, float2.Zero);

        // A chunk of its own, so the pointer moving re-meshes one quad and not the whole screen, and a
        // band above the strips it sits on.
        _pointer = picture.AddSlot("Pointer");
        _pointerRect = _pointer.AttachComponent<RectTransform>();
        _pointer.AttachComponent<GraphicChunkRoot>().OverlayLevel = 1;
        _pointerTexture = _pointer.AttachComponent<DesktopTextureProvider>();
        _pointerTexture.Layer.Value = IDesktopFrameSource.PointerLayer;
        _pointer.AttachComponent<RawImage>().Texture.Target = _pointerTexture;
        _pointer.ActiveSelf.Value = false;

        var waiting = viewport.AddSlot("Waiting");
        Pin(waiting.AttachComponent<RectTransform>(), float2.Zero, float2.One, float2.Zero, float2.Zero);
        _waiting = AddFillLabel(waiting, "Waiting for the desktop...", DashTheme.FontBody, TextDim);

        var statusSlot = root.AddSlot("Status");
        Pin(statusSlot.AttachComponent<RectTransform>(), float2.Zero, new float2(1f, 0f), new float2(Pad + 2f, Pad), new float2(-Pad, Pad + StatusHeight));
        _status = AddFillLabel(statusSlot, string.Empty, DashTheme.FontSmall, DashTheme.TextMuted);
        _status.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _status.AutoSizeHorizontal.Value = true;
        _status.AutoSizeMin.Value = 10f;

        RefreshDisplays();
    }

    // One row along the top, fixed widths left to right; the picture keeps every unit under it.
    private void BuildStrip(Slot root)
    {
        var strip = root.AddSlot("Controls");
        Pin(strip.AttachComponent<RectTransform>(), new float2(0f, 1f), float2.One, new float2(Pad, -(Pad + StripHeight)), new float2(-Pad, -Pad));
        strip.AttachComponent<GraphicChunkRoot>();
        var layout = strip.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = CellGap;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = true;

        StripLabel(strip, "DisplayLabel", "Display", 54f);
        _displayRow = StripItem(strip, "Displays", CellWidth);
        var row = _displayRow.AttachComponent<HorizontalLayout>();
        row.Spacing.Value = CellGap;
        row.ForceExpandWidth.Value = true;
        row.ForceExpandHeight.Value = true;

        _follow = AddCell(StripItem(strip, "Follow", 116f), "Follow cursor", ToggleFollow);
        _keyboardCell = AddCell(StripItem(strip, "Keyboard", 92f), "Keyboard", ToggleKeyboard);
        AddCell(StripItem(strip, "Start", 60f), "Start", () => Press(DesktopKey.Start));
        AddCell(StripItem(strip, "Copy", 56f), "Copy", () => Press(DesktopKey.Copy));
        AddCell(StripItem(strip, "Paste", 60f), "Paste", () => Press(DesktopKey.Paste));

        StripLabel(strip, "BrightnessLabel", "Brightness", 74f);
        var slider = StripItem(strip, "Brightness", 132f);
        var sliderRow = slider.AttachComponent<HorizontalLayout>();
        sliderRow.Spacing.Value = 6f;
        sliderRow.ForceExpandWidth.Value = false;
        sliderRow.ForceExpandHeight.Value = true;
        var b = RowBuilder(slider);
        b.MinWidth(60f).PreferredWidth(86f).FlexibleWidth(1f);
        var brightnessSlider = b.Slider(EngineSettings.DesktopBrightness, SettingsCatalog.DesktopBrightness.Min, SettingsCatalog.DesktopBrightness.Max, (_, v) =>
        {
            EngineSettings.DesktopBrightness = v;
            ApplyBrightness();
        });
        var sliderElement = brightnessSlider.Slot.GetComponent<LayoutElement>()!;
        sliderElement.MinWidth.Value = 60f;
        sliderElement.PreferredWidth.Value = 86f;
        sliderElement.FlexibleWidth.Value = 1f;
        b.MinWidth(40f).PreferredWidth(40f).FlexibleWidth(0f);
        _brightnessValue = AddRowLabel(b, Percent(EngineSettings.DesktopBrightness), DashTheme.FontSmall, TextDim, TextHorizontalAlignment.Right);

        RefreshToggles();
    }

    private static Slot StripItem(Slot strip, string name, float width)
    {
        var slot = strip.AddSlot(name);
        slot.AttachComponent<RectTransform>();
        SetWidth(slot.AttachComponent<LayoutElement>(), width);
        return slot;
    }

    private static void SetWidth(LayoutElement element, float width)
    {
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
    }

    private void StripLabel(Slot strip, string name, string text, float width)
    {
        var label = AddFillLabel(StripItem(strip, name, width), text, DashTheme.FontSmall, DashTheme.TextMuted, SemiboldFont);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Right;
    }

    // The width the strip's controls need, for the window's floor.
    public const float StripWidth = 54f + CellWidth * 2f + CellGap + 116f + 92f + 60f + 56f + 60f + 74f + 132f + CellGap * 9f + Pad * 2f;

    private Cell AddCell(Slot slot, string label, Action onClick)
    {
        var panel = ApplyCard(slot, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);
        var button = slot.AttachComponent<Button>();
        button.Clicked += (_, _) => onClick();
        var driver = DriveCardStates(button, panel, DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed);
        var text = AddFillLabel(slot, label, DashTheme.FontBody, TextPrimary, SemiboldFont);
        return new Cell(panel, text, driver);
    }

    private static void Pin(RectTransform rect, float2 anchorMin, float2 anchorMax, float2 offsetMin, float2 offsetMax)
    {
        rect.AnchorMin.Value = anchorMin;
        rect.AnchorMax.Value = anchorMax;
        rect.OffsetMin.Value = offsetMin;
        rect.OffsetMax.Value = offsetMax;
    }

    private static string Percent(float value) => $"{(int)MathF.Round(value * 100f)}%";

    // SHOW, HIDE AND THE CAPTURE

    public override void OnAwake()
    {
        base.OnAwake();
        Slot.ActiveChanged += OnActiveChanged;
    }

    public override void OnDestroy()
    {
        Slot.ActiveChanged -= OnActiveChanged;
        Release();
        base.OnDestroy();
    }

    protected override void OnShow()
    {
        DesktopPlatform.Backend?.Retry();
        RefreshDisplays();
        SyncHold();
    }

    protected override void OnHide()
    {
        _keyboard = false;
        _surface?.ReleaseButtons();
        RefreshToggles();
        SyncHold();
    }

    private void OnActiveChanged(Slot slot) => SyncHold();

    // Held while the page can be seen: shown, with the dash open around it.
    private void SyncHold()
    {
        bool want = !IsDestroyed && Slot.IsActive && _display != null;
        if (!want)
        {
            Release();
            return;
        }
        if (_held != null && _held.Display.Key == _display!.Key)
            return;
        Release();
        _held = DesktopCaptures.Hold(_display!);
    }

    private void Release()
    {
        if (_held == null)
            return;
        DesktopCaptures.Release(_held);
        _held = null;
    }

    // DISPLAYS

    private void RefreshDisplays()
    {
        _nextDisplayRefresh = (World?.Time.TotalTime ?? 0.0) + DisplayRefreshSeconds;
        var displays = DesktopPlatform.Backend?.Displays() ?? Array.Empty<DesktopDisplay>();
        bool changed = displays.Count != _displays.Count;
        for (int i = 0; !changed && i < displays.Count; i++)
            changed = displays[i] != _displays[i];
        if (!changed && _displayButtons.Count > 0)
            return;
        _displays = displays;
        RebuildDisplayButtons();

        DesktopDisplay? pick = null;
        foreach (var display in _displays)
        {
            if (display.Key == (_display?.Key ?? EngineSettings.DesktopMonitor))
                pick = display;
        }
        Select(pick ?? (_displays.Count > 0 ? _displays[0] : null));
    }

    private void RebuildDisplayButtons()
    {
        if (_displayRow == null)
            return;
        _displayRow.DestroyChildren();
        _displayButtons.Clear();
        var rowElement = _displayRow.GetComponent<LayoutElement>();
        if (rowElement != null)
            SetWidth(rowElement, System.Math.Max(1, _displays.Count) * (CellWidth + CellGap) - CellGap);
        for (int i = 0; i < _displays.Count; i++)
        {
            var display = _displays[i];
            var slot = _displayRow.AddSlot("Display" + (i + 1));
            slot.AttachComponent<RectTransform>();
            var element = slot.AttachComponent<LayoutElement>();
            element.FlexibleWidth.Value = 1f;
            element.FlexibleHeight.Value = 1f;
            var cell = AddCell(slot, (i + 1).ToString(), () =>
            {
                EngineSettings.DesktopFollowCursor = false;
                Select(display);
                RefreshToggles();
            });
            _displayButtons.Add((display, cell));
        }
    }

    private void Select(DesktopDisplay? display)
    {
        bool moved = display?.Key != _display?.Key;
        _display = display;
        if (_surface != null)
            _surface.Display = display;
        if (display != null && moved)
            EngineSettings.DesktopMonitor = display.Key;
        if (_pointerTexture != null)
            _pointerTexture.Display.Value = display?.Key ?? string.Empty;
        foreach (var (entry, cell) in _displayButtons)
            Paint(cell, entry.Key == display?.Key);
        SyncHold();
    }

    // Switches to whichever monitor the real mouse is on.
    private void FollowCursor()
    {
        if (!EngineSettings.DesktopFollowCursor || Input?.TryGetCursor(out int x, out int y) != true)
            return;
        foreach (var display in _displays)
        {
            if (display.Contains(x, y))
            {
                if (display.Key != _display?.Key)
                    Select(display);
                return;
            }
        }
    }

    // PER FRAME

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if ((World?.Time.TotalTime ?? 0.0) >= _nextDisplayRefresh)
            RefreshDisplays();
        FollowCursor();

        var capture = _held;
        if (capture != null && (!ReferenceEquals(capture, _builtFor) || capture.LayoutVersion != _builtLayout))
            BuildStrips(capture);
        bool picture = capture != null && ReferenceEquals(capture, _builtFor) && capture.HasPicture;
        foreach (var part in _stripParts)
        {
            bool show = picture && part.Texture.Asset?.Version > 0;
            if (part.Slot.ActiveSelf.Value != show)
                part.Slot.ActiveSelf.Value = show;
        }
        if (_waiting != null && _waiting.Slot.ActiveSelf.Value == picture)
            _waiting.Slot.ActiveSelf.Value = !picture;
        UpdatePointer(picture ? capture : null);
        UpdateStatus(capture);
        RequestRenderOnNewPixels();
    }

    // The dash is captured offscreen only when its canvas asks, and the canvas only asks when its mesh
    // changes. The strips change pixels in place and never touch the mesh, so without this the picture
    // would sit still until a hover somewhere happened to redraw the dash.
    private void RequestRenderOnNewPixels()
    {
        int uploads = _pointerTexture?.Asset?.Uploads ?? 0;
        foreach (var part in _stripParts)
            uploads += part.Texture.Asset?.Uploads ?? 0;
        if (uploads == _uploadsSeen)
            return;
        _uploadsSeen = uploads;
        // Found again once the app has moved: popped out, it draws on its panel's canvas, not the dash's.
        if (_canvas == null || _canvas.IsDestroyed || !Slot.IsDescendantOf(_canvas.Slot))
            _canvas = Slot.GetComponentInParents<Canvas>();
        _canvas?.RequestRender();
    }

    private void BuildStrips(DesktopCapture capture)
    {
        int width = capture.Width, height = capture.Height, count = capture.StripCount;
        if (_strips == null || width <= 0 || height <= 0 || count <= 0)
            return;
        _builtFor = capture;
        _builtLayout = capture.LayoutVersion;
        if (_aspect != null)
            _aspect.AspectRatio.Value = (float)width / height;

        _strips.DestroyChildren();
        _stripParts.Clear();
        var tint = BrightnessTint();
        for (int i = 0; i < count; i++)
        {
            int top = i * DesktopCapture.StripHeight;
            int rows = System.Math.Min(DesktopCapture.StripHeight, height - top);
            var slot = _strips.AddSlot("Strip" + i);
            Pin(slot.AttachComponent<RectTransform>(), new float2(0f, 1f - (float)(top + rows) / height), new float2(1f, 1f - (float)top / height), float2.Zero, float2.Zero);
            var texture = slot.AttachComponent<DesktopTextureProvider>();
            texture.Display.Value = capture.Display.Key;
            texture.Layer.Value = i;
            var image = slot.AttachComponent<RawImage>();
            image.Texture.Target = texture;
            image.Tint.Value = tint;
            slot.ActiveSelf.Value = false;
            _stripParts.Add((slot, texture, image));
        }
    }

    // The pointer's image sits at its top-left corner, in the monitor's pixels, sized by anchors so it
    // scales with the picture.
    private void UpdatePointer(DesktopCapture? capture)
    {
        if (_pointer == null || _pointerRect == null)
            return;
        int width = capture?.Width ?? 0, height = capture?.Height ?? 0;
        int pw = capture?.PointerWidth ?? 0, ph = capture?.PointerHeight ?? 0;
        bool show = capture != null && capture.PointerVisible && width > 0 && height > 0 && pw > 0
            && _pointerTexture?.Asset?.Version > 0;
        if (_pointer.ActiveSelf.Value != show)
            _pointer.ActiveSelf.Value = show;
        if (!show)
            return;
        int x = capture!.PointerX, y = capture.PointerY;
        var min = new float2((float)x / width, 1f - (float)(y + ph) / height);
        var max = new float2((float)(x + pw) / width, 1f - (float)y / height);
        if (_pointerRect.AnchorMin.Value != min)
            _pointerRect.AnchorMin.Value = min;
        if (_pointerRect.AnchorMax.Value != max)
            _pointerRect.AnchorMax.Value = max;
        if (_pointerRect.OffsetMin.Value != float2.Zero)
            _pointerRect.OffsetMin.Value = float2.Zero;
        if (_pointerRect.OffsetMax.Value != float2.Zero)
            _pointerRect.OffsetMax.Value = float2.Zero;
    }

    private void UpdateStatus(DesktopCapture? capture)
    {
        if (_status == null)
            return;
        string text;
        if (_displays.Count == 0)
            text = DesktopPlatform.Backend?.Status ?? "No monitor to show.";
        else if (capture?.Problem is { } problem)
            text = problem;
        else if (Input == null)
            text = "This desktop lets itself be watched, not driven.";
        else if (!InVr)
            text = "Viewing only. In VR the laser drives the mouse.";
        else if (_keyboard && _refused)
            text = "Click a window on the desktop first, then type.";
        else if (_keyboard)
            text = "Keys go to the window you last clicked.";
        else
            text = "Trigger clicks, secondary right-clicks, the stick scrolls.";
        if (_status.Content.Value != text)
            _status.Content.Value = text;
    }

    // CONTROLS

    private void ToggleFollow()
    {
        EngineSettings.DesktopFollowCursor = !EngineSettings.DesktopFollowCursor;
        RefreshToggles();
    }

    private void ToggleKeyboard()
    {
        _keyboard = !_keyboard && InVr;
        _refused = false;
        RefreshToggles();
    }

    private void RefreshToggles()
    {
        Paint(_follow, EngineSettings.DesktopFollowCursor);
        Paint(_keyboardCell, _keyboard);
    }

    // On reads like the selected dash tab: a soft accent wash under an accent label. A solid accent fill
    // comes out pale enough on the dash that a white label on it all but disappears.
    private static void Paint(Cell? cell, bool on)
    {
        if (cell == null)
            return;
        var normal = on ? DashTheme.AccentSoft : DashTheme.Surface;
        cell.Driver.NormalColor.Value = normal;
        cell.Driver.HighlightColor.Value = on ? DashTheme.Accent.WithAlpha(0.26f) : DashTheme.SurfaceHover;
        cell.Driver.PressedColor.Value = on ? DashTheme.Accent.WithAlpha(0.32f) : DashTheme.SurfacePressed;
        cell.Driver.DisabledColor.Value = normal;
        cell.Driver.Apply();
        cell.Label.Color.Value = on ? DashTheme.Accent : DashTheme.Text;
    }

    private color BrightnessTint()
    {
        float value = EngineSettings.DesktopBrightness;
        return new color(value, value, value, 1f);
    }

    private void ApplyBrightness()
    {
        var tint = BrightnessTint();
        foreach (var part in _stripParts)
            part.Image.Tint.Value = tint;
        if (_brightnessValue != null)
            _brightnessValue.Content.Value = Percent(EngineSettings.DesktopBrightness);
    }

    private void Press(DesktopKey key)
    {
        if (!InVr)
            return;
        _refused = Input?.Press(key) != true;
    }

    // KEYS
    //
    // With the keyboard on, the VR keyboard types into the desktop instead of the dash. Off, every key
    // falls through to the dash as usual.

    public bool HasFieldFocus => IsExecutionAllowed && _keyboard && InVr;

    public bool ConsumeChar(char c)
    {
        if (!HasFieldFocus)
            return false;
        _refused = Input?.Type(c.ToString()) != true;
        return true;
    }

    public bool ConsumeBackspace() => ConsumeKey(DesktopKey.Backspace);

    public bool ConsumeEnter() => ConsumeKey(DesktopKey.Enter);

    public bool ConsumeEscape() => ConsumeKey(DesktopKey.Escape);

    public bool ConsumeKey(DashboardKey key) => ConsumeKey(key switch
    {
        DashboardKey.Tab => DesktopKey.Tab,
        DashboardKey.Left => DesktopKey.Left,
        DashboardKey.Right => DesktopKey.Right,
        DashboardKey.Up => DesktopKey.Up,
        DashboardKey.Down => DesktopKey.Down,
        DashboardKey.Copy => DesktopKey.Copy,
        _ => DesktopKey.Paste,
    });

    private bool ConsumeKey(DesktopKey key)
    {
        if (!HasFieldFocus)
            return false;
        _refused = Input?.Press(key) != true;
        return true;
    }

    public void ReleaseFieldFocus()
    {
        if (!_keyboard)
            return;
        _keyboard = false;
        RefreshToggles();
    }
}
