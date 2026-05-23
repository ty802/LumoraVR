// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class Dashboard : UIComponent
{
    public readonly Sync<float> TopBarHeight;
    public readonly Sync<color> BackgroundColor;

    private readonly List<DashboardScreen> _screens = new();
    private bool _built;
    private Slot? _topBarSlot;
    private Slot? _screenHostSlot;
    private DashboardScreen? _currentScreen;

    public IReadOnlyList<DashboardScreen> Screens => _screens;
    public DashboardScreen? CurrentScreen => _currentScreen;
    public Slot? TopBarSlot => _topBarSlot;
    public Slot? ScreenHostSlot => _screenHostSlot;

    public event Action<DashboardScreen?>? ScreenChanged;

    public Dashboard()
    {
        TopBarHeight = new Sync<float>(this, 48f);
        BackgroundColor = new Sync<color>(this, new color(0.025f, 0.028f, 0.034f, 0.92f));
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureBuilt();
        RegisterExistingScreens();
    }

    public T AddScreen<T>(string label) where T : DashboardScreen, new()
    {
        EnsureBuilt();

        var screenSlot = _screenHostSlot!.AddSlot(label);
        Fill(screenSlot.AttachComponent<RectTransform>());
        var screen = screenSlot.AttachComponent<T>();
        screen.Label.Value = label;
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
        {
            SwitchTo(screen);
        }
    }

    public void SwitchTo(DashboardScreen screen)
    {
        if (!_screens.Contains(screen))
        {
            RegisterScreen(screen);
        }

        if (ReferenceEquals(_currentScreen, screen)) return;

        _currentScreen?.HideScreen();
        _currentScreen = screen;
        _currentScreen.ShowScreen();
        ScreenChanged?.Invoke(_currentScreen);
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        var rect = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        Fill(rect);
        _ = Slot.GetComponent<Canvas>() ?? Slot.AttachComponent<Canvas>();
        var background = Slot.GetComponent<Image>() ?? Slot.AttachComponent<Image>();
        background.Tint.Value = BackgroundColor.Value;

        _topBarSlot = Slot.AddSlot("TopBar");
        var topRect = _topBarSlot.AttachComponent<RectTransform>();
        topRect.AnchorMin.Value = new float2(0f, 1f);
        topRect.AnchorMax.Value = new float2(1f, 1f);
        topRect.OffsetMin.Value = new float2(0f, -TopBarHeight.Value);
        topRect.OffsetMax.Value = float2.Zero;
        var topLayout = _topBarSlot.AttachComponent<HorizontalLayout>();
        topLayout.Spacing.Value = 4f;
        topLayout.PaddingLeft.Value = 6f;
        topLayout.PaddingRight.Value = 6f;
        topLayout.PaddingTop.Value = 6f;
        topLayout.PaddingBottom.Value = 6f;

        _screenHostSlot = Slot.AddSlot("Screens");
        var hostRect = _screenHostSlot.AttachComponent<RectTransform>();
        Fill(hostRect);
        hostRect.OffsetMax.Value = new float2(0f, -TopBarHeight.Value);
    }

    private void RegisterExistingScreens()
    {
        if (_screenHostSlot == null) return;

        foreach (var screen in _screenHostSlot.GetComponentsInChildren<DashboardScreen>(true))
        {
            RegisterScreen(screen);
        }
    }

    private void CreateScreenButton(DashboardScreen screen)
    {
        if (_topBarSlot == null) return;

        var builder = new UIBuilder(_topBarSlot);
        builder.Button(screen.Label.Value, (_, _) => SwitchTo(screen), new color(0.10f, 0.12f, 0.15f, 1f));
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
