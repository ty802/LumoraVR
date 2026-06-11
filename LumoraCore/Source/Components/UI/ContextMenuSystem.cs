// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Helio.UI;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.UI;

/// <summary>
/// What the menu was opened against: the pointer that summoned it and the
/// slot the laser was hitting at open time. Item sources use this to add
/// contextual actions (equip the avatar you point at, etc.).
/// </summary>
public sealed class ContextMenuContext
{
    public Slot? Pointer;
    public Slot? Target;

    /// <summary>Which hand summoned the menu - enables stick flick-select.</summary>
    public Input.Chirality? Side;
}

/// <summary>
/// The user's radial context menu. Owns the page stack, collects items from
/// sources, and renders itself as an engine-side mesh UI - a local Helio
/// canvas per peer, hit by the interaction laser like any world canvas.
/// There is no platform view layer.
/// </summary>
[ComponentCategory("UI/Context Menu")]
public class ContextMenuSystem : Component
{
    private const float CanvasScale = 0.001f;
    private const float CanvasSize = 460f;
    private const float ItemSize = 120f;
    private const float ItemRadius = 158f;
    private const float OpenDistance = 0.35f;

    /// <summary>Whether the menu is currently open.</summary>
    public readonly Sync<bool> IsOpen = null!;

    /// <summary>The page currently being displayed (null when closed).</summary>
    public ContextMenuPage? CurrentPage { get; private set; }

    /// <summary>The context the menu was opened with (null when closed).</summary>
    public ContextMenuContext? CurrentContext { get; private set; }

    public bool HasPageHistory => _pageStack.Count > 0;

    public event Action<ContextMenuPage>? MenuOpened;
    public event Action? MenuClosed;
    public event Action<ContextMenuPage>? PageChanged;

    private readonly Stack<ContextMenuPage> _pageStack = new();

    private Slot _menuRoot = null!;
    private Slot _canvasSlot = null!;
    private FontProvider _font = null!;

    // Flick select: deflect the summoning hand's stick toward an item, then
    // release to pick it - same gesture as the ref's radial menu.
    private const float FlickEngageThreshold = 0.65f;
    private const float FlickReleaseThreshold = 0.35f;
    private readonly List<(ContextMenuItem item, ArcSegment arc, color fill)> _builtItems = new();
    private ContextMenuItem? _flickItem;

    // Public API

    /// <summary>
    /// Toggle the menu at the pointer. Bind to a controller button.
    /// </summary>
    public void Toggle(ContextMenuContext? context = null)
    {
        if (IsOpen.Value) Close();
        else Open(context);
    }

    /// <summary>
    /// Open the root context menu. Collects items from RootContextMenuItem and
    /// ContextMenuItemSource components under this slot's user hierarchy.
    /// </summary>
    public void Open(ContextMenuContext? context = null)
    {
        CurrentContext = context ?? new ContextMenuContext();
        var page = BuildRootPage(CurrentContext);
        if (page.Items.Count == 0)
        {
            LumoraLogger.Log("ContextMenuSystem: No items to show");
            CurrentContext = null;
            return;
        }

        page.LayoutItems();

        _pageStack.Clear();
        CurrentPage = page;
        IsOpen.Value = true;

        PositionMenu(CurrentContext.Pointer);
        RebuildVisual();

        MenuOpened?.Invoke(page);
        LumoraLogger.Log($"ContextMenuSystem: Opened '{page.Title}' ({page.Items.Count} items)");
    }

    /// <summary>Navigate into a sub-page (PopPage goes back).</summary>
    public void PushPage(ContextMenuPage page)
    {
        if (page == null || CurrentPage == null) return;

        _pageStack.Push(CurrentPage);
        CurrentPage = page;
        CurrentPage.LayoutItems();
        RebuildVisual();
        PageChanged?.Invoke(CurrentPage);
    }

    /// <summary>Go back one page. Closes the menu if already at the root page.</summary>
    public void PopPage()
    {
        if (_pageStack.Count == 0) { Close(); return; }

        CurrentPage = _pageStack.Pop();
        RebuildVisual();
        PageChanged?.Invoke(CurrentPage);
    }

    /// <summary>Close the menu.</summary>
    public void Close()
    {
        if (!IsOpen.Value) return;

        _pageStack.Clear();
        CurrentPage = null;
        CurrentContext = null;
        IsOpen.Value = false;

        DestroyVisual();
        MenuClosed?.Invoke();
    }

    /// <summary>
    /// Handle item selection. Navigates to SubPage if set, otherwise invokes
    /// OnPressed and closes.
    /// </summary>
    public void SelectItem(ContextMenuItem item)
    {
        if (item == null || !item.IsEnabled) return;

        if (item.SubPage != null)
        {
            PushPage(item.SubPage);
        }
        else
        {
            var pressed = item.OnPressed;
            Close();
            pressed?.Invoke(item);
        }
    }

    public override void OnDestroy()
    {
        DestroyVisual();
        base.OnDestroy();
    }

    // Visual (engine-side mesh UI, local per peer)

    private void PositionMenu(Slot? pointer)
    {
        EnsureVisualRoot();

        var head = Slot.ActiveUserRoot?.HeadSlot;
        if (pointer != null && !pointer.IsDestroyed)
        {
            _menuRoot.GlobalPosition = pointer.GlobalPosition + pointer.Forward * OpenDistance;
        }
        else if (head != null)
        {
            _menuRoot.GlobalPosition = head.GlobalPosition + head.Forward * (OpenDistance + 0.15f);
        }
    }

    private void EnsureVisualRoot()
    {
        if (_menuRoot != null && !_menuRoot.IsDestroyed)
            return;

        _menuRoot = Slot.AddLocalSlot("ContextMenu");
        _menuRoot.AttachComponent<FaceLocalUser>();
        _font = _menuRoot.AttachComponent<FontProvider>();
        _font.URL.Value = new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");
    }

    private void RebuildVisual()
    {
        EnsureVisualRoot();

        _canvasSlot?.Destroy();
        _canvasSlot = _menuRoot.AddLocalSlot("Canvas");
        _canvasSlot.LocalScale.Value = float3.One * CanvasScale;

        // Canvas root size comes from a centered RectTransform on the canvas
        // slot (same convention as PanelShell).
        var rootRect = _canvasSlot.AttachComponent<RectTransform>();
        rootRect.OffsetMin.Value = new float2(-CanvasSize * 0.5f, -CanvasSize * 0.5f);
        rootRect.OffsetMax.Value = new float2(CanvasSize * 0.5f, CanvasSize * 0.5f);
        _canvasSlot.AttachComponent<Canvas>();

        var page = CurrentPage;
        if (page == null) return;

        BuildCenter(page);

        for (int i = 0; i < page.Items.Count; i++)
        {
            BuildItem(page.Items[i]);
        }
    }

    private void BuildCenter(ContextMenuPage page)
    {
        var center = _canvasSlot.AddLocalSlot("Center");
        var rect = center.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(-46f, -46f);
        rect.OffsetMax.Value = new float2(46f, 46f);

        var image = center.AttachComponent<Image>();
        image.Tint.Value = new color(0.08f, 0.09f, 0.12f, 0.92f);

        var button = center.AttachComponent<Button>();
        button.Clicked += (_, _) => PopPage();

        var labelSlot = center.AddLocalSlot("Title");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = float2.Zero;
        labelRect.AnchorMax.Value = float2.One;

        var text = labelSlot.AttachComponent<Text>();
        text.Content.Value = HasPageHistory ? "Back" : page.Title;
        text.Font.Target = _font;
        text.Size.Value = 22f;
        text.Color.Value = color.White;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        text.WordWrap.Value = false;
    }

    private void BuildItem(ContextMenuItem item)
    {
        // Arc segment spanning the item's slice of the ring (polar layout from
        // ContextMenuPage.LayoutItems), with arc-accurate hit testing.
        var itemSlot = _canvasSlot.AddLocalSlot(item.Label);
        var rect = itemSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;

        var fill = ToColor(item.FillColor, new color(0.12f, 0.12f, 0.12f, 0.9f));
        if (item.IsToggle && item.IsToggled)
            fill = new color(0.16f, 0.34f, 0.22f, 0.95f);
        if (!item.IsEnabled)
            fill = new color(fill.r * 0.5f, fill.g * 0.5f, fill.b * 0.5f, fill.a * 0.7f);

        var arc = itemSlot.AttachComponent<ArcSegment>();
        arc.AngleStart.Value = item.AngleStart;
        arc.ArcLength.Value = item.ArcLength;
        arc.InnerRadius.Value = item.RadiusStart;
        arc.OuterRadius.Value = item.RadiusEnd;
        arc.Tint.Value = fill;

        if (item.IsEnabled && (item.OnPressed != null || item.SubPage != null))
        {
            var button = itemSlot.AttachComponent<ArcButton>();
            button.AddColorDriver(arc.Tint, fill);
            var captured = item;
            button.Clicked += (_, _) => SelectItem(captured);
        }

        // Label centered on the arc's midpoint.
        float midRad = item.AngleMiddle * (MathF.PI / 180f);
        float2 midDirection = new float2(MathF.Cos(midRad), -MathF.Sin(midRad));
        float2 labelCenter = new float2(0.5f, 0.5f) + midDirection * (item.RadiusMiddle / CanvasSize);

        var labelSlot = itemSlot.AddLocalSlot("Label");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = labelCenter;
        labelRect.AnchorMax.Value = labelCenter;
        labelRect.OffsetMin.Value = new float2(-ItemSize * 0.5f, -ItemSize * 0.5f);
        labelRect.OffsetMax.Value = new float2(ItemSize * 0.5f, ItemSize * 0.5f);

        var text = labelSlot.AttachComponent<Text>();
        text.Content.Value = item.Label;
        text.Font.Target = _font;
        text.Size.Value = 20f;
        text.Color.Value = ToColor(item.LabelColor, color.White);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        _builtItems.Add((item, arc, fill));
    }

    private void DestroyVisual()
    {
        if (_menuRoot != null && !_menuRoot.IsDestroyed)
            _menuRoot.Destroy();
        _menuRoot = null!;
        _canvasSlot = null!;
        _font = null!;
    }

    private static color ToColor(float[] rgba, color fallback)
    {
        if (rgba == null || rgba.Length < 4)
            return fallback;
        return new color(rgba[0], rgba[1], rgba[2], rgba[3]);
    }

    // Item collection

    private ContextMenuPage BuildRootPage(ContextMenuContext context)
    {
        var page = new ContextMenuPage("Menu");

        // 1. Fixed items from RootContextMenuItem components (sorted by Priority desc)
        var rootItems = new List<(int priority, ContextMenuItem item)>();
        foreach (var root in CollectComponents<RootContextMenuItem>())
            rootItems.Add((root.Priority.Value, root.ToMenuItem()));

        foreach (var (_, item) in rootItems.OrderByDescending(x => x.priority))
            page.AddItem(item);

        // 2. Contextual items from ContextMenuItemSource components
        foreach (var source in CollectComponents<ContextMenuItemSource>()
                                    .OrderByDescending(s => s.Priority.Value))
        {
            try
            {
                source.PopulateContextMenu(page, context);
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"ContextMenuSystem: {source.GetType().Name}.PopulateContextMenu threw: {ex.Message}");
            }
        }

        return page;
    }

    private IEnumerable<T> CollectComponents<T>() where T : Component
    {
        var results = new List<T>();
        var root = Slot.ActiveUserRoot?.Slot ?? Slot;
        CollectRecursive<T>(root, results);
        return results;
    }

    private static void CollectRecursive<T>(Slot slot, List<T> results) where T : Component
    {
        foreach (var comp in slot.Components)
            if (comp is T t) results.Add(t);
        foreach (var child in slot.Children)
            CollectRecursive<T>(child, results);
    }
}
