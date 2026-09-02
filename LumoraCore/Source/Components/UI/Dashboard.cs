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

[ComponentCategory("Hidden")]
public class Dashboard : UIComponent
{
    // The dash is three bands on one ground: a bare header strip, the screen panel, a bare tab bar.
    // Only the middle band gets a fill. Everything used to be its own bordered box inside another
    // bordered box, which is what made the whole thing read as one flat purple slab. -xlinka
    private const float Outer = 16f;
    private const float Gap = 12f;
    private const float TabPadding = 12f;
    private const float TabSpacing = 2f;
    private const float StatusSide = 14f;
    private const float ConnectionWidth = 116f;
    private const float VersionWidth = 154f;

    private static readonly color Transparent = new color(0f, 0f, 0f, 0f);
    // The dash composites over the world, so the ground has to stay fully opaque or bright scenes
    // read straight through the UI. Same hue as the token, alpha forced to 1.
    private static readonly color OpaqueBackdrop =
        new color(DashTheme.Backdrop.r, DashTheme.Backdrop.g, DashTheme.Backdrop.b, 1f);
    private static readonly color TabHover = new color(1f, 1f, 1f, 0.06f);
    private static readonly color TabPressed = new color(1f, 1f, 1f, 0.10f);
    private static readonly color TabActiveHover = DashTheme.Accent.WithAlpha(0.26f);
    private static readonly color TabActivePressed = DashTheme.Accent.WithAlpha(0.32f);

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
    public readonly AssetRef<FontSet> FontSemibold;
    public readonly AssetRef<FontSet> FontBold;
    // Fixed-width face for the header readouts. A proportional FPS counter reflows its own pill
    // every sample because "1" is narrower than "8"; mono holds still. -xlinka
    public readonly AssetRef<FontSet> FontMono;

    private readonly List<DashboardScreen> _screens = new();
    private readonly Dictionary<DashboardScreen, NavTab> _navTabs = new();
    private readonly Dictionary<int, RoundedRectTextureProvider> _roundedByRadius = new();

    private bool _built;
    private RectTransform? _rootRect;
    private RoundedRectTextureProvider? _rounded;
    private Slot? _headerSlot;
    private Slot? _navSlot;
    private Slot? _screenHostSlot;
    private WidgetGrid? _widgetGrid;

    // Header strip: the widget grid starts past the title, sits inset from the strip's top and bottom, and
    // the two status widgets are three cells wide each (room for a readout and its graph).
    private const float HeaderWidgetsStart = 440f;
    private const float HeaderWidgetsInset = 10f;
    private const float HeaderSpacing = 4f;
    private const int HeaderWidgetWidth = 3;
    // Cells for the Leave World card on the strip: room for its label and its "You are home" note.
    private const int HeaderCardWidth = 6;
    private Text? _title;
    private DashboardScreen? _currentScreen;

    public IReadOnlyList<DashboardScreen> Screens => _screens;
    public DashboardScreen? CurrentScreen => _currentScreen;
    public Slot? ScreenHostSlot => _screenHostSlot;
    public RoundedRectTextureProvider? RoundedSprite => _rounded;

    public event Action<DashboardScreen?>? ScreenChanged;

    private sealed class NavTab
    {
        public Text Label = null!;
        public ColorDriver? Driver;
    }

    // Nine-slice corner sprites, one per radius the theme uses. A single 14-unit sprite sliced at some
    // other border width cuts through its own arc and the corner comes out dented, so each radius gets
    // a texture cut for it. Cached per dash: four small SDF bitmaps, built once. -xlinka
    public RoundedRectTextureProvider RoundedSpriteFor(float radius)
    {
        int r = (int)MathF.Round(radius);
        if (r < 2) r = 2;
        if (r > 32) r = 32;
        if (_roundedByRadius.TryGetValue(r, out var existing) && !existing.IsDestroyed)
            return existing;

        var provider = Slot.AddSlot("Theme" + r).AttachComponent<RoundedRectTextureProvider>();
        provider.Size.Value = r * 4;
        provider.Radius.Value = r;
        _roundedByRadius[r] = provider;
        return provider;
    }

    public Dashboard()
    {
        Size = new Sync<float2>(this, new float2(1180f, 720f));
        HeaderHeight = new Sync<float>(this, 60f);
        StatusHeight = new Sync<float>(this, 60f);
        BackgroundColor = new Sync<color>(this, OpaqueBackdrop);
        HeaderColor = new Sync<color>(this, Transparent);
        StatusColor = new Sync<color>(this, Transparent);
        ContentColor = new Sync<color>(this, DashTheme.Panel);
        Title = new Sync<string>(this, "Lumora");
        Version = new Sync<string>(this, "Lumora v2026.05.29");
        Font = new AssetRef<FontSet>(this);
        FontSemibold = new AssetRef<FontSet>(this);
        FontBold = new AssetRef<FontSet>(this);
        FontMono = new AssetRef<FontSet>(this);
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

        if (!_navTabs.ContainsKey(screen))
        {
            _screens.Add(screen);
            CreateScreenButton(screen);
        }

        screen.HideScreen();
        if (_currentScreen == null)
            SwitchTo(screen);
        RefreshNavStyles();
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
        RefreshNavStyles();
        ScreenChanged?.Invoke(_currentScreen);
    }

    // One flag for every grid and every popped-out panel; the grids mirror it on their next update.
    public void ToggleWidgetEdit() => WidgetPanel.EditMode = !WidgetPanel.EditMode;

    // Re-render the current screen's content. Used when the dashboard becomes visible: our canvas
    // only renders on a dirty, and reactivating the (parked) render rig doesn't dirty it on its own,
    // so a freshly-opened dash would otherwise stay blank until you switch tabs (which forces a
    // rebuild). This does what a tab switch does, on open.
    public void ForceRebuild()
    {
        _currentScreen?.ShowScreen();
        Slot.GetComponent<Canvas>()?.MarkDirty();
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
        // A bar with an edge. Floating the title and the status widgets straight on the ground left the
        // top of the dash with no visible bounds, which reads as unfinished and is harder to aim at.
        AddRoundedPanel(_headerSlot, DashTheme.Panel, DashTheme.OutlineStrong, DashTheme.RadiusPanel);

        BuildTitle(_headerSlot);
        BuildWidgets();
    }

    private void BuildTitle(Slot headerSlot)
    {
        var box = headerSlot.AddSlot("TitleBox");
        var rect = box.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0f, 0f);
        rect.AnchorMax.Value = new float2(0f, 1f);
        rect.OffsetMin.Value = new float2(DashTheme.Inset, 0f);
        rect.OffsetMax.Value = new float2(420f, 0f);

        var builder = new UIBuilder(box);
        builder.Font(FontBold.Target ?? Font.Target);
        _title = builder.Text(Title.Value, DashTheme.FontTitle, DashTheme.Text);
        _title.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _title.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        var titleRect = _title.RectTransform!;
        titleRect.AnchorMin.Value = float2.Zero;
        titleRect.AnchorMax.Value = float2.One;
        titleRect.OffsetMin.Value = float2.Zero;
        titleRect.OffsetMax.Value = float2.Zero;
    }

    private void BuildWidgets()
    {
        // Full-width top-strip grid after the title: one row of square cells the height of the strip, as
        // many as fit its width, so the header is the same kind of grid as Home and a widget can move
        // between the two. A fixed count, not a derived one: the cells stretch with the width so the
        // layout stays valid at any aspect. The status widgets sit at the rightmost columns. -xlinka
        var widgetsSlot = _headerSlot!.AddSlot("Widgets");
        var rect = widgetsSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0f, 0f);
        rect.AnchorMax.Value = new float2(1f, 1f);
        rect.OffsetMin.Value = new float2(HeaderWidgetsStart, HeaderWidgetsInset);
        rect.OffsetMax.Value = new float2(-StatusSide, -HeaderWidgetsInset);

        float stripWidth = Size.Value.x - 2f * Outer - HeaderWidgetsStart - StatusSide;
        float stripHeight = HeaderHeight.Value - 2f * HeaderWidgetsInset;
        int columns = (int)MathF.Floor((stripWidth + HeaderSpacing) / (stripHeight + HeaderSpacing));
        if (columns < 2 * HeaderWidgetWidth)
            columns = 2 * HeaderWidgetWidth;

        _widgetGrid = widgetsSlot.AttachComponent<WidgetGrid>();
        _widgetGrid.FixedColumns.Value = columns;
        _widgetGrid.FixedRows.Value = 1;
        _widgetGrid.Spacing.Value = new float2(HeaderSpacing, HeaderSpacing);
        _widgetGrid.Padding.Value = float2.Zero;
        _widgetGrid.PlacedStyler = StyleDroppedWidget;

        AddWidget<FpsWidgetPreset>(widgetsSlot, "FpsWidget", columns - 2 * HeaderWidgetWidth);
        AddWidget<ClockWidgetPreset>(widgetsSlot, "ClockWidget", columns - HeaderWidgetWidth);
        // Leave World lives up here, left of the readouts: it is the one action wanted from every
        // screen, and Home had it buried in a column of cards.
        AddHeaderCard<LeaveWorldWidgetPreset>(widgetsSlot, "LeaveWorld", columns - 2 * HeaderWidgetWidth - HeaderCardWidth, HeaderCardWidth);
    }

    // A Home-style card on the header strip. Unlike the text pills above it brings its own chrome and
    // fonts, so all the strip has to give it is a chunk and a cell footprint.
    private void AddHeaderCard<T>(Slot grid, string name, int gridX, int gridWidth) where T : WidgetPreset, new()
    {
        var slot = grid.AddSlot(name);
        slot.AttachComponent<GraphicChunkRoot>();
        var preset = slot.AttachComponent<T>();
        preset.GridX.Value = gridX;
        preset.GridWidth.Value = gridWidth;
        preset.GridHeight.Value = 1;
    }

    // A widget that lands on a dash grid off a carried panel gets the header pill chrome and the status
    // font when it is one of the text widgets, so it looks like the built-ins wherever it is put down.
    // Home cards bring their own chrome.
    public void StyleDroppedWidget(WidgetPreset preset)
    {
        if (preset is not TextWidgetPreset text)
            return;
        text.Font.Target = FontMono.Target ?? Font.Target;
        text.TextSize.Value = DashTheme.FontBody;
        text.TextColor.Value = DashTheme.TextDim;
        StylePill(text);
    }

    private void AddWidget<T>(Slot grid, string name, int gridX) where T : TextWidgetPreset, new()
    {
        var slot = grid.AddSlot(name);
        // Live widgets (FPS counter, clock) re-render constantly. Their own chunk
        // root keeps those updates from rebuilding the whole dash canvas - without
        // it every tick re-meshes whatever screen is loaded (the file browser
        // makes that very expensive).
        slot.AttachComponent<GraphicChunkRoot>();
        var preset = slot.AttachComponent<T>();
        preset.Font.Target = FontMono.Target ?? Font.Target;
        preset.GridX.Value = gridX;
        preset.GridWidth.Value = HeaderWidgetWidth;
        preset.GridHeight.Value = 1;
        preset.TextSize.Value = DashTheme.FontBody;
        preset.TextColor.Value = DashTheme.TextDim;
        StylePill(preset);
    }

    private void StylePill(WidgetPreset preset)
    {
        preset.Background.Value = DashTheme.Surface;
        preset.BorderColor.Value = DashTheme.Outline;
        preset.BackgroundSprite.Target = _rounded!;
        // Matches the nine-slice sprite's own 14-unit corner: slice it tighter than that and the
        // border cuts through the arc, which reads as a dented corner. -xlinka
        preset.CornerRadius.Value = DashTheme.RadiusCard;
    }

    private void BuildContent()
    {
        _screenHostSlot = Slot.AddSlot("Content");
        var rect = _screenHostSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = new float2(Outer, Outer + StatusHeight.Value + Gap);
        rect.OffsetMax.Value = new float2(-Outer, -(Outer + HeaderHeight.Value + Gap));
        // No outline: the panel is lighter than the ground, and that step IS the edge.
        AddRoundedPanel(_screenHostSlot, ContentColor.Value, Transparent, DashTheme.RadiusPanel);
    }

    private void BuildStatusBar()
    {
        var statusSlot = Slot.AddSlot("StatusBar");
        var rect = statusSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0f, 0f);
        rect.AnchorMax.Value = new float2(1f, 0f);
        rect.OffsetMin.Value = new float2(Outer, Outer);
        rect.OffsetMax.Value = new float2(-Outer, Outer + StatusHeight.Value);
        // Same edge as the header, so the nav reads as a bar rather than tabs adrift on the ground.
        AddRoundedPanel(statusSlot, DashTheme.Panel, DashTheme.OutlineStrong, DashTheme.RadiusPanel);

        BuildConnection(statusSlot);

        _navSlot = statusSlot.AddSlot("Nav");
        var navRect = _navSlot.AttachComponent<RectTransform>();
        navRect.AnchorMin.Value = float2.Zero;
        navRect.AnchorMax.Value = float2.One;
        navRect.OffsetMin.Value = new float2(StatusSide + ConnectionWidth + Gap, 12f);
        navRect.OffsetMax.Value = new float2(-(StatusSide + VersionWidth + Gap), -12f);
        var nav = _navSlot.AttachComponent<HorizontalLayout>();
        nav.Spacing.Value = TabSpacing;
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
        rect.OffsetMin.Value = new float2(StatusSide, -17f);
        rect.OffsetMax.Value = new float2(StatusSide + ConnectionWidth, 17f);

        var preset = slot.AttachComponent<ConnectionWidgetPreset>();
        preset.Font.Target = Font.Target;
        preset.BackgroundSprite.Target = _rounded!;
        preset.CornerRadius.Value = DashTheme.RadiusChip;
        // No fill down here: the status dot carries the state, the bar is bare ground.
        preset.Background.Value = Transparent;
        preset.BorderColor.Value = Transparent;
        preset.LabelColor.Value = DashTheme.TextMuted;
    }

    private void BuildVersion(Slot statusSlot)
    {
        var slot = statusSlot.AddSlot("Version");
        var rect = slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(1f, 0.5f);
        rect.AnchorMax.Value = new float2(1f, 0.5f);
        rect.OffsetMin.Value = new float2(-(StatusSide + VersionWidth), -17f);
        rect.OffsetMax.Value = new float2(-StatusSide, 17f);

        var preset = slot.AttachComponent<LabelWidgetPreset>();
        preset.Font.Target = Font.Target;
        preset.Background.Value = Transparent;
        preset.BorderColor.Value = Transparent;
        preset.TextColor.Value = DashTheme.TextMuted;
        preset.TextSize.Value = DashTheme.FontLabel;
        preset.Alignment.Value = TextHorizontalAlignment.Right;
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

        // The tab sizes itself to its label: a fixed 96 wide box wrapped "Friends (Soon)" onto two
        // lines and left everything else swimming. The layout on the tab reports padding + text
        // width up to the nav row, so each tab is exactly as wide as its word. -xlinka
        var layout = slot.AttachComponent<HorizontalLayout>();
        layout.PaddingLeft.Value = TabPadding;
        layout.PaddingRight.Value = TabPadding;
        layout.Spacing.Value = 0f;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = true;
        layout.CenterChildren.Value = true;

        var wash = AddRoundedPanel(slot, Transparent, Transparent, DashTheme.RadiusControl);

        // The label is attached by hand, not through UIBuilder: the builder puts a LayoutElement on
        // every leaf under a layout, and a LayoutElement outranks the Text on the same slot, so the
        // tab's layout would see flex 1 and a preferred width of 0 instead of the word. Ten tabs
        // then split the bar evenly and the active pill ran into its neighbour. -xlinka
        var textSlot = slot.AddSlot("Text");
        textSlot.AttachComponent<RectTransform>();
        var text = textSlot.AttachComponent<Text>();
        text.Content.Value = screen.Label.Value;
        text.Font.Target = FontSemibold.Target ?? Font.Target;
        text.Size.Value = DashTheme.FontBody;
        text.Color.Value = screen.NavLabelColor;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        // No wrap, or the measurement eats itself: the first pass hands the label a narrow box, it
        // breaks "Friends (Soon)" onto two lines, and then it reports the WRAPPED width as what it
        // wants, so the tab never grows back. -xlinka
        text.WordWrap.Value = false;

        var button = slot.AttachComponent<Button>();
        button.Clicked += (_, _) => SwitchTo(screen);
        var driver = button.AddColorDriver(wash.Tint, Transparent, InteractionColorMode.Direct);

        _navTabs[screen] = new NavTab { Label = text, Driver = driver };
        Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    // Selected tab: soft accent wash + accent label. Everything else is text on the bare bar, so the
    // one thing that is lit is the thing you are looking at. Screens that set their own nav color
    // (Exit) keep it when idle; placeholder tabs read as muted.
    public void RefreshNavStyles()
    {
        foreach (var pair in _navTabs)
        {
            var screen = pair.Key;
            var tab = pair.Value;
            bool active = ReferenceEquals(screen, _currentScreen);

            if (tab.Driver != null && !tab.Driver.IsDestroyed)
            {
                var wash = active ? DashTheme.AccentSoft : Transparent;
                tab.Driver.NormalColor.Value = wash;
                tab.Driver.HighlightColor.Value = active ? TabActiveHover : TabHover;
                tab.Driver.PressedColor.Value = active ? TabActivePressed : TabPressed;
                // A tab is never disabled, and the derived disabled grey is what a driver paints
                // while its slot is still inactive. -xlinka
                tab.Driver.DisabledColor.Value = wash;
                tab.Driver.Apply();
            }

            if (tab.Label != null && !tab.Label.IsDestroyed)
            {
                tab.Label.Color.Value = active
                    ? DashTheme.Accent
                    : screen.Placeholder.Value ? DashTheme.TextMuted : screen.NavLabelColor;
            }
        }
        Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private BorderedImage AddRoundedPanel(Slot slot, color fill, color outline, float radius)
    {
        var image = slot.GetComponent<BorderedImage>() ?? slot.AttachComponent<BorderedImage>();
        bool hasOutline = outline.a > 0.001f;
        image.Tint.Value = fill;
        image.BorderTint.Value = hasOutline ? outline : Transparent;
        image.BorderThickness.Value = hasOutline ? DashTheme.OutlineWidth : 0f;
        var rounded = RoundedSpriteFor(radius);
        image.Texture.Target = rounded;
        image.NineSlice.Value = true;
        image.Borders.Value = new float4(radius, radius, radius, radius);
        return image;
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
