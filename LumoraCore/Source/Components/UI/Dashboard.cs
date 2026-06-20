// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Helio.UI.Layout;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class Dashboard : UIComponent
{
    private static readonly color AccentColor = new color(0.50f, 0.40f, 0.96f, 0.55f);
    private static readonly color ButtonColor = new color(0.22f, 0.20f, 0.34f, 0.45f);
    private static readonly color WidgetFill = new color(0.17f, 0.16f, 0.26f, 0.45f);
    private static readonly color BorderColor = new color(0.52f, 0.46f, 0.82f, 0.45f);
    private const float CornerRadius = 14f;
    private const float BorderThickness = 3f;
    private const float Outer = 16f;
    private const float Gap = 12f;

    public readonly Sync<float2> Size;
    public readonly Sync<float> HeaderHeight;
    public readonly Sync<float> StatusHeight;
    public readonly Sync<color> BackgroundColor;
    public readonly Sync<color> HeaderColor;
    public readonly Sync<color> StatusColor;
    public readonly Sync<color> ContentColor;
    public readonly Sync<string> Title;
    public readonly Sync<string> Version;
    public readonly AssetRef<FontSet> Font;

    private readonly List<DashboardScreen> _screens = new();
    private readonly Dictionary<DashboardScreen, Slot> _buttonSlots = new();

    private bool _built;
    private RectTransform? _rootRect;
    private RoundedRectTextureProvider? _rounded;
    private Slot? _headerSlot;
    private Slot? _navSlot;
    private Slot? _screenHostSlot;
    private WidgetGrid? _widgetGrid;
    private Text? _title;
    private DashboardScreen? _currentScreen;

    public IReadOnlyList<DashboardScreen> Screens => _screens;
    public DashboardScreen? CurrentScreen => _currentScreen;
    public Slot? ScreenHostSlot => _screenHostSlot;
    public RoundedRectTextureProvider? RoundedSprite => _rounded;

    public event Action<DashboardScreen?>? ScreenChanged;

    public Dashboard()
    {
        Size = new Sync<float2>(this, new float2(1180f, 720f));
        HeaderHeight = new Sync<float>(this, 60f);
        StatusHeight = new Sync<float>(this, 60f);
        // Opaque: the dash draws as an overlay above the world, and any
        // translucency makes its UI illegible against bright scenes.
        BackgroundColor = new Sync<color>(this, new color(0.06f, 0.05f, 0.11f, 1f));
        HeaderColor = new Sync<color>(this, new color(0.13f, 0.11f, 0.20f, 1f));
        StatusColor = new Sync<color>(this, new color(0.12f, 0.10f, 0.19f, 1f));
        ContentColor = new Sync<color>(this, new color(0.10f, 0.09f, 0.15f, 0.98f));
        Title = new Sync<string>(this, "Lumora");
        Version = new Sync<string>(this, "Lumora v2026.05.29");
        Font = new AssetRef<FontSet>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureBuilt();
        RegisterExistingScreens();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        ApplyRootSize();
    }

    public T AddScreen<T>(string label, color? activeColor = null) where T : DashboardScreen, new()
    {
        EnsureBuilt();

        var screenSlot = _screenHostSlot!.AddSlot(label);
        Fill(screenSlot.AttachComponent<RectTransform>());
        var screen = screenSlot.AttachComponent<T>();
        screen.Label.Value = label;
        if (activeColor.HasValue)
            screen.ActiveColor.Value = activeColor.Value;
        RegisterScreen(screen);
        return screen;
    }

    public void RegisterScreen(DashboardScreen screen)
    {
        EnsureBuilt();

        if (!_screens.Contains(screen))
        {
            _screens.Add(screen);
            CreateScreenButton(screen);
        }

        screen.HideScreen();
        if (_currentScreen == null)
            SwitchTo(screen);
        UpdateButtonHighlights();
    }

    public void SwitchTo(DashboardScreen screen)
    {
        if (!_screens.Contains(screen))
            RegisterScreen(screen);

        if (ReferenceEquals(_currentScreen, screen)) return;

        _currentScreen?.HideScreen();
        _currentScreen = screen;
        _currentScreen.ShowScreen();
        if (_title != null)
            _title.Content.Value = screen.Label.Value;
        UpdateButtonHighlights();
        ScreenChanged?.Invoke(_currentScreen);
    }

    public void ToggleWidgetEdit()
    {
        if (_widgetGrid != null)
            _widgetGrid.EditMode.Value = !_widgetGrid.EditMode.Value;
    }

    /// <summary>
    /// Re-render the current screen's content. Used when the dashboard becomes visible: our canvas
    /// only renders on a dirty, and reactivating the (parked) render rig doesn't dirty it on its own,
    /// so a freshly-opened dash would otherwise stay blank until you switch tabs (which forces a
    /// rebuild). This does what a tab switch does, on open.
    /// </summary>
    public void ForceRebuild()
    {
        _currentScreen?.ShowScreen();
        Slot.GetComponent<Canvas>()?.MarkLayoutDirty();
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _rootRect = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        ApplyRootSize();
        _ = Slot.GetComponent<Canvas>() ?? Slot.AttachComponent<Canvas>();

        var background = Slot.GetComponent<Image>() ?? Slot.AttachComponent<Image>();
        background.Tint.Value = BackgroundColor.Value;

        _rounded = Slot.AddSlot("Theme").AttachComponent<RoundedRectTextureProvider>();
        _rounded.Size.Value = 48;
        _rounded.Radius.Value = 14;

        BuildHeader();
        BuildContent();
        BuildStatusBar();
    }

    private void BuildHeader()
    {
        _headerSlot = Slot.AddSlot("Header");
        var rect = _headerSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0f, 1f);
        rect.AnchorMax.Value = new float2(1f, 1f);
        rect.OffsetMin.Value = new float2(Outer, -(Outer + HeaderHeight.Value));
        rect.OffsetMax.Value = new float2(-Outer, -Outer);
        RoundedPanel(_headerSlot, HeaderColor.Value, BorderColor);

        BuildTitleBox(_headerSlot);

        BuildWidgets();
    }

    private void BuildTitleBox(Slot headerSlot)
    {
        var box = headerSlot.AddSlot("TitleBox");
        var rect = box.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0f, 0f);
        rect.AnchorMax.Value = new float2(0f, 1f);
        rect.OffsetMin.Value = new float2(14f, 10f);
        rect.OffsetMax.Value = new float2(186f, -10f);

        var bg = box.AttachComponent<BorderedImage>();
        bg.Tint.Value = WidgetFill;
        bg.BorderTint.Value = BorderColor;
        bg.BorderThickness.Value = BorderThickness;
        if (_rounded != null)
        {
            bg.Texture.Target = _rounded;
            bg.NineSlice.Value = true;
            bg.Borders.Value = new float4(12f, 12f, 12f, 12f);
        }

        var builder = new UIBuilder(box);
        builder.Font(Font.Target);
        _title = builder.Text(Title.Value, 20f, new color(0.95f, 0.95f, 0.98f, 1f));
        _title.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        _title.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        var titleRect = _title.RectTransform!;
        titleRect.AnchorMin.Value = float2.Zero;
        titleRect.AnchorMax.Value = float2.One;
        titleRect.OffsetMin.Value = new float2(10f, 0f);
        titleRect.OffsetMax.Value = new float2(-10f, 0f);
    }

    private void BuildWidgets()
    {
        var widgetsSlot = _headerSlot!.AddSlot("Widgets");
        var rect = widgetsSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(1f, 0f);
        rect.AnchorMax.Value = new float2(1f, 1f);
        rect.OffsetMin.Value = new float2(-296f, 10f);
        rect.OffsetMax.Value = new float2(-14f, -10f);

        _widgetGrid = widgetsSlot.AttachComponent<WidgetGrid>();
        _widgetGrid.CellSize.Value = new float2(132f, 38f);
        _widgetGrid.Spacing.Value = new float2(8f, 4f);
        _widgetGrid.Padding.Value = new float2(0f, 1f);

        AddWidget<FpsWidgetPreset>(widgetsSlot, "FpsWidget", 0, new color(0.30f, 0.85f, 0.50f, 1f));
        AddWidget<ClockWidgetPreset>(widgetsSlot, "ClockWidget", 1, new color(0.85f, 0.86f, 0.92f, 1f));
    }

    /// <summary>
    /// Dock a widget back onto the top bar by preset type - used when a standalone
    /// userspace panel is released over the dash. Appends after the existing
    /// widgets and styles it like the built-in bar widgets.
    /// </summary>
    public bool TryDockWidget(Type presetType)
    {
        if (_widgetGrid == null || _widgetGrid.IsDestroyed || presetType == null)
            return false;

        var gridSlot = _widgetGrid.Slot;
        int gridX = gridSlot.GetComponentsInChildren<Widget>(false).Count();

        var slot = gridSlot.AddSlot(presetType.Name);
        slot.AttachComponent<GraphicChunkRoot>();
        if (slot.AttachComponent(presetType) is not WidgetPreset preset)
        {
            slot.Destroy();
            return false;
        }

        preset.GridX.Value = gridX;
        preset.Background.Value = WidgetFill;
        preset.BorderColor.Value = BorderColor;
        preset.BackgroundSprite.Target = _rounded!;
        preset.CornerRadius.Value = 12f;
        if (preset is TextWidgetPreset text)
        {
            text.Font.Target = Font.Target;
            text.TextColor.Value = new color(0.85f, 0.86f, 0.92f, 1f);
        }
        return true;
    }

    private void AddWidget<T>(Slot grid, string name, int gridX, color textColor) where T : TextWidgetPreset, new()
    {
        var slot = grid.AddSlot(name);
        // Live widgets (FPS counter, clock) re-render constantly. Their own chunk
        // root keeps those updates from rebuilding the whole dash canvas - without
        // it every tick re-meshes whatever screen is loaded (the file browser
        // makes that very expensive).
        slot.AttachComponent<GraphicChunkRoot>();
        var preset = slot.AttachComponent<T>();
        preset.Font.Target = Font.Target;
        preset.GridX.Value = gridX;
        preset.TextColor.Value = textColor;
        preset.Background.Value = WidgetFill;
        preset.BorderColor.Value = BorderColor;
        preset.BackgroundSprite.Target = _rounded!;
        preset.CornerRadius.Value = 12f;
    }

    private void BuildContent()
    {
        _screenHostSlot = Slot.AddSlot("Content");
        var rect = _screenHostSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = new float2(Outer, Outer + StatusHeight.Value + Gap);
        rect.OffsetMax.Value = new float2(-Outer, -(Outer + HeaderHeight.Value + Gap));
        RoundedPanel(_screenHostSlot, ContentColor.Value, BorderColor);
    }

    private void BuildStatusBar()
    {
        var statusSlot = Slot.AddSlot("StatusBar");
        var rect = statusSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0f, 0f);
        rect.AnchorMax.Value = new float2(1f, 0f);
        rect.OffsetMin.Value = new float2(Outer, Outer);
        rect.OffsetMax.Value = new float2(-Outer, Outer + StatusHeight.Value);
        RoundedPanel(statusSlot, StatusColor.Value, BorderColor);

        BuildConnection(statusSlot);

        _navSlot = statusSlot.AddSlot("Nav");
        var navRect = _navSlot.AttachComponent<RectTransform>();
        navRect.AnchorMin.Value = float2.Zero;
        navRect.AnchorMax.Value = float2.One;
        navRect.OffsetMin.Value = new float2(150f, 10f);
        navRect.OffsetMax.Value = new float2(-220f, -10f);
        var nav = _navSlot.AttachComponent<HorizontalLayout>();
        nav.Spacing.Value = 6f;
        nav.ForceExpandWidth.Value = false;
        nav.ForceExpandHeight.Value = true;
        nav.CenterChildren.Value = true;

        BuildVersion(statusSlot);
    }

    private void BuildConnection(Slot statusSlot)
    {
        var slot = statusSlot.AddSlot("Connection");
        var rect = slot.AttachComponent<RectTransform>();
        // Own chunk so the live connection status updating doesn't re-mesh the whole root. -xlinka
        slot.AttachComponent<GraphicChunkRoot>();
        rect.AnchorMin.Value = new float2(0f, 0.5f);
        rect.AnchorMax.Value = new float2(0f, 0.5f);
        rect.OffsetMin.Value = new float2(14f, -17f);
        rect.OffsetMax.Value = new float2(146f, 17f);

        var preset = slot.AttachComponent<ConnectionWidgetPreset>();
        preset.Font.Target = Font.Target;
        preset.BackgroundSprite.Target = _rounded!;
        preset.CornerRadius.Value = 12f;
        preset.Background.Value = WidgetFill;
        preset.BorderColor.Value = BorderColor;
    }

    private void BuildVersion(Slot statusSlot)
    {
        var slot = statusSlot.AddSlot("Version");
        var rect = slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(1f, 0.5f);
        rect.AnchorMax.Value = new float2(1f, 0.5f);
        rect.OffsetMin.Value = new float2(-212f, -17f);
        rect.OffsetMax.Value = new float2(-14f, 17f);

        var preset = slot.AttachComponent<LabelWidgetPreset>();
        preset.Font.Target = Font.Target;
        preset.BackgroundSprite.Target = _rounded!;
        preset.CornerRadius.Value = 12f;
        preset.Background.Value = WidgetFill;
        preset.BorderColor.Value = BorderColor;
        preset.TextColor.Value = new color(0.7f, 0.7f, 0.78f, 1f);
        preset.TextSize.Value = 10f;
        preset.LabelText.Value = Version.Value;
    }

    private void RegisterExistingScreens()
    {
        if (_screenHostSlot == null) return;
        foreach (var screen in _screenHostSlot.GetComponentsInChildren<DashboardScreen>(true))
            RegisterScreen(screen);
    }

    private void CreateScreenButton(DashboardScreen screen)
    {
        if (_navSlot == null) return;

        var slot = _navSlot.AddSlot(screen.Label.Value);
        slot.AttachComponent<RectTransform>();
        // Its own chunk so hovering a nav tab (it drives a tint) re-meshes just this button, not the whole
        // dashboard root chunk. -xlinka
        slot.AttachComponent<GraphicChunkRoot>();
        var layout = slot.AttachComponent<LayoutElement>();
        layout.MinWidth.Value = 84f;
        layout.PreferredWidth.Value = 96f;

        var img = slot.AttachComponent<BorderedImage>();
        img.Tint.Value = ButtonColor;
        img.BorderTint.Value = BorderColor;
        img.BorderThickness.Value = BorderThickness;
        if (_rounded != null)
        {
            img.Texture.Target = _rounded;
            img.NineSlice.Value = true;
            img.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
        }

        var builder = new UIBuilder(slot);
        builder.Font(Font.Target).FontSize(12f);
        var text = builder.Text(screen.Label.Value, 12f, screen.NavLabelColor);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        if (text.RectTransform != null)
            Fill(text.RectTransform);

        var button = slot.AttachComponent<Button>();
        button.Clicked += (_, _) => SwitchTo(screen);
        button.AddColorDriver(img.Tint, ButtonColor, InteractionColorMode.Direct);

        _buttonSlots[screen] = slot;
        Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private void UpdateButtonHighlights()
    {
        foreach (var pair in _buttonSlots)
        {
            var driver = pair.Value.GetComponent<ColorDriver>();
            if (driver == null) continue;

            bool active = ReferenceEquals(pair.Key, _currentScreen);
            driver.SetColors(active ? AccentColor : ButtonColor);
            driver.Apply();
        }
        Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private void RoundedPanel(Slot slot, color fill, color border)
    {
        var img = slot.GetComponent<BorderedImage>() ?? slot.AttachComponent<BorderedImage>();
        img.Tint.Value = fill;
        img.BorderTint.Value = border;
        img.BorderThickness.Value = BorderThickness;
        if (_rounded != null)
        {
            img.Texture.Target = _rounded;
            img.NineSlice.Value = true;
            img.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
        }
    }

    private void ApplyRootSize()
    {
        if (_rootRect == null) return;
        _rootRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        _rootRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        _rootRect.OffsetMin.Value = Size.Value * -0.5f;
        _rootRect.OffsetMax.Value = Size.Value * 0.5f;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
