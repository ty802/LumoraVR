// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class Dashboard : UIComponent
{
    private static readonly color AccentColor = new color(0.47f, 0.37f, 0.94f, 1f);
    private static readonly color ButtonColor = new color(0.15f, 0.13f, 0.25f, 0.7f);
    private static readonly color BorderColor = new color(0.34f, 0.29f, 0.52f, 0.65f);
    private const float CornerRadius = 14f;
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
    private WidgetGrid? _facetGrid;
    private Text? _subtitle;
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
        BackgroundColor = new Sync<color>(this, new color(0.05f, 0.04f, 0.10f, 0.55f));
        HeaderColor = new Sync<color>(this, new color(0.13f, 0.11f, 0.20f, 0.74f));
        StatusColor = new Sync<color>(this, new color(0.12f, 0.10f, 0.19f, 0.74f));
        ContentColor = new Sync<color>(this, new color(0.09f, 0.08f, 0.14f, 0.5f));
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
        if (_subtitle != null)
            _subtitle.Content.Value = screen.Label.Value + " Dashboard";
        UpdateButtonHighlights();
        ScreenChanged?.Invoke(_currentScreen);
    }

    public void ToggleFacetEdit()
    {
        if (_facetGrid != null)
            _facetGrid.EditMode.Value = !_facetGrid.EditMode.Value;
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

        var builder = new UIBuilder(_headerSlot);
        builder.Font(Font.Target);
        var title = builder.Text(Title.Value, 22f, new color(0.92f, 0.92f, 0.96f, 1f));
        title.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        title.VerticalAlignment.Value = TextVerticalAlignment.Bottom;
        AnchorBox(title.RectTransform!, new float2(0f, 0.5f), new float2(0.6f, 1f), new float2(18f, 0f), new float2(0f, -8f));

        _subtitle = builder.Text("Home Dashboard", 11f, new color(0.5f, 0.5f, 0.6f, 1f));
        _subtitle.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _subtitle.VerticalAlignment.Value = TextVerticalAlignment.Top;
        AnchorBox(_subtitle.RectTransform!, new float2(0f, 0f), new float2(0.6f, 0.5f), new float2(18f, 6f), float2.Zero);

        BuildFacets();
    }

    private void BuildFacets()
    {
        var facetsSlot = _headerSlot!.AddSlot("Facets");
        var rect = facetsSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(1f, 0f);
        rect.AnchorMax.Value = new float2(1f, 1f);
        rect.OffsetMin.Value = new float2(-296f, 10f);
        rect.OffsetMax.Value = new float2(-14f, -10f);

        _facetGrid = facetsSlot.AttachComponent<WidgetGrid>();
        _facetGrid.CellSize.Value = new float2(132f, 38f);
        _facetGrid.Spacing.Value = new float2(8f, 4f);
        _facetGrid.Padding.Value = new float2(0f, 1f);

        AddFacet<FpsWidgetPreset>(facetsSlot, "FpsFacet", 0, new color(0.30f, 0.85f, 0.50f, 1f));
        AddFacet<ClockWidgetPreset>(facetsSlot, "ClockFacet", 1, new color(0.85f, 0.86f, 0.92f, 1f));
    }

    private void AddFacet<T>(Slot grid, string name, int gridX, color textColor) where T : TextWidgetPreset, new()
    {
        var slot = grid.AddSlot(name);
        var preset = slot.AttachComponent<T>();
        preset.Font.Target = Font.Target;
        preset.GridX.Value = gridX;
        preset.TextColor.Value = textColor;
        preset.Background.Value = new color(0.12f, 0.11f, 0.18f, 0.85f);
        preset.BorderColor.Value = BorderColor;
        preset.BackgroundSprite.Target = _rounded;
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
        rect.AnchorMin.Value = new float2(0f, 0.5f);
        rect.AnchorMax.Value = new float2(0f, 0.5f);
        rect.OffsetMin.Value = new float2(14f, -17f);
        rect.OffsetMax.Value = new float2(146f, 17f);

        var preset = slot.AttachComponent<ConnectionWidgetPreset>();
        preset.Font.Target = Font.Target;
        preset.BackgroundSprite.Target = _rounded;
        preset.CornerRadius.Value = 12f;
        preset.Background.Value = new color(0.12f, 0.11f, 0.18f, 0.85f);
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
        preset.BackgroundSprite.Target = _rounded;
        preset.CornerRadius.Value = 12f;
        preset.Background.Value = new color(0.12f, 0.11f, 0.18f, 0.85f);
        preset.BorderColor.Value = BorderColor;
        preset.TextColor.Value = new color(0.5f, 0.5f, 0.55f, 1f);
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

        var builder = new UIBuilder(_navSlot);
        builder.Font(Font.Target).FontSize(12f).MinWidth(84f).PreferredWidth(96f);
        var button = builder.Button(screen.Label.Value, (_, _) => SwitchTo(screen), ButtonColor);
        MakeRounded(button.Slot.GetComponent<Image>());
        _buttonSlots[screen] = button.Slot;
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
    }

    private void RoundedPanel(Slot slot, color fill, color border)
    {
        var borderImg = slot.GetComponent<Image>() ?? slot.AttachComponent<Image>();
        MakeRounded(borderImg);
        borderImg.Tint.Value = border;

        var fillSlot = slot.AddSlot("Fill");
        var rect = fillSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = new float2(2f, 2f);
        rect.OffsetMax.Value = new float2(-2f, -2f);
        var fillImg = fillSlot.AttachComponent<Image>();
        MakeRounded(fillImg);
        fillImg.Tint.Value = fill;
    }

    private void MakeRounded(Image? img)
    {
        if (img == null || _rounded == null) return;
        img.Texture.Target = _rounded;
        img.NineSlice.Value = true;
        img.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
    }

    private void ApplyRootSize()
    {
        if (_rootRect == null) return;
        _rootRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        _rootRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        _rootRect.OffsetMin.Value = Size.Value * -0.5f;
        _rootRect.OffsetMax.Value = Size.Value * 0.5f;
    }

    private static void AnchorBox(RectTransform rect, float2 anchorMin, float2 anchorMax, float2 offsetMin, float2 offsetMax)
    {
        rect.AnchorMin.Value = anchorMin;
        rect.AnchorMax.Value = anchorMax;
        rect.OffsetMin.Value = offsetMin;
        rect.OffsetMax.Value = offsetMax;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
