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

    /// <summary>Root slot of the open menu's visuals; null while closed.</summary>
    public Slot? VisualRoot => _menuRoot != null && !_menuRoot.IsDestroyed ? _menuRoot : null;
    private Slot _canvasSlot = null!;
    private FontProvider _font = null!;

    // Flick select: deflect the summoning hand's stick toward an item, then
    // release to pick it.
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

        _openTime = 0f;
        PositionMenu(CurrentContext.Pointer);
        RebuildVisual();
        SuppressLocomotion();

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

        ReleaseLocomotionSuppression();
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
        ReleaseLocomotionSuppression();
        DestroyVisual();
        base.OnDestroy();
    }

    // ── Flick select ────────────────────────────────────────────────────────

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        UpdateFlickSelect();
        UpdateEdgeClose(delta);
    }

    // Desktop selection: mouse look is frozen while the menu is open and the
    // mouse deflects the hand laser instead (HandTool.UpdateDesktopMenuAim),
    // so the laser cursor is the single pointer and hover/click flow through
    // the normal laser-canvas path.
    private bool _restoreMouseLook;

    // Close when the pointer wanders past the ring edge - moving the aim
    // well off the menu dismisses it without a button press.
    private const float EdgeCloseRadius = 320f;
    private float _openTime;

    private void UpdateEdgeClose(float delta)
    {
        if (!IsOpen.Value || _canvasSlot == null || _canvasSlot.IsDestroyed)
            return;

        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        // Grace so the menu doesn't instantly dismiss while it settles.
        _openTime += delta;
        if (_openTime < 0.35f)
            return;

        Slot? rayOrigin = CurrentContext?.Pointer;
        if (rayOrigin == null || rayOrigin.IsDestroyed)
            rayOrigin = Slot?.ActiveUserRoot?.HeadSlot;
        if (rayOrigin == null || rayOrigin.IsDestroyed)
            return;

        // Use the laser's actual cast ray when available - on desktop the slot
        // pose isn't the ray (head + mouse deflection is).
        float3 origin, direction;
        var laser = rayOrigin.GetComponent<Interaction.InteractionLaser>();
        if (laser != null)
        {
            origin = laser.RayOrigin;
            direction = laser.RayDirection;
        }
        else
        {
            origin = rayOrigin.GlobalPosition;
            direction = rayOrigin.GlobalRotation * float3.Backward;
        }

        // Intersect the aim ray with the menu plane; pointing away entirely
        // also counts as leaving.
        var planeNormal = _canvasSlot.GlobalRotation * float3.Forward;
        float denom = float3.Dot(direction, planeNormal);
        if (MathF.Abs(denom) < 1e-4f)
            return;

        float t = float3.Dot(_canvasSlot.GlobalPosition - origin, planeNormal) / denom;
        if (t < 0f)
        {
            Close();
            return;
        }

        var local = _canvasSlot.GlobalPointToLocal(origin + direction * t);
        float distance = MathF.Sqrt(local.x * local.x + local.y * local.y);
        if (distance > EdgeCloseRadius)
            Close();
    }

    // Deflect the summoning hand's stick toward an item, release to pick it.
    // Locomotion is suppressed while open so the gesture doesn't snap-turn.
    private void UpdateFlickSelect()
    {
        if (!IsOpen.Value || _builtItems.Count == 0)
            return;

        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var side = CurrentContext?.Side;
        if (side == null)
            return;

        var input = Engine.Current?.InputInterface;
        var controller = side == Input.Chirality.Left ? input?.LeftController : input?.RightController;
        if (controller == null)
            return;

        var stick = controller.ThumbstickPosition;
        float magnitude = MathF.Sqrt(stick.X * stick.X + stick.Y * stick.Y);

        if (magnitude >= FlickEngageThreshold)
        {
            // Stick up = top item: same clockwise-from-right angle convention
            // as the arc layout.
            float angle = MathF.Atan2(-stick.Y, stick.X) * (180f / MathF.PI);
            SetFlickItem(FindItemAtAngle(angle));
        }
        else if (magnitude <= FlickReleaseThreshold && _flickItem != null)
        {
            var selected = _flickItem;
            SetFlickItem(null);
            SelectItem(selected);
        }
    }

    private ContextMenuItem? FindItemAtAngle(float angle)
    {
        foreach (var (item, _, _) in _builtItems)
        {
            if (!item.IsEnabled)
                continue;
            float relative = angle - item.AngleStart;
            relative -= MathF.Floor(relative / 360f) * 360f;
            if (relative <= item.ArcLength)
                return item;
        }
        return null;
    }

    private void SetFlickItem(ContextMenuItem? item)
    {
        if (ReferenceEquals(_flickItem, item))
            return;

        _flickItem = item;
        foreach (var (built, arc, fill) in _builtItems)
        {
            if (arc == null || arc.IsDestroyed)
                continue;
            bool highlighted = ReferenceEquals(built, item);
            arc.Tint.Value = highlighted
                ? new color(
                    MathF.Min(fill.r + 0.18f, 1f),
                    MathF.Min(fill.g + 0.18f, 1f),
                    MathF.Min(fill.b + 0.18f, 1f),
                    fill.a)
                : fill;
        }
    }

    private void SuppressLocomotion()
    {
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var state = UserInputState.ForFocusedLocalUser;
        if (state == null)
            return;

        if (Engine.Current?.InputInterface?.VR_Active == true)
        {
            // VR: the flick gesture shares the stick with locomotion.
            state.SetDesktopInputSuppressed(this, true);
        }
        else if (!state.MouseLookSuppressed)
        {
            // Desktop: freeze the camera and hand the mouse to the menu
            // pointer. WASD stays live.
            state.SetMouseLookSuppressed(true);
            _restoreMouseLook = true;
        }
    }

    private void ReleaseLocomotionSuppression()
    {
        var state = UserInputState.ForFocusedLocalUser;
        state?.SetDesktopInputSuppressed(this, false);

        if (_restoreMouseLook)
        {
            state?.SetMouseLookSuppressed(false);
            _restoreMouseLook = false;
        }
    }

    // Visual (engine-side mesh UI, local per peer)

    private void PositionMenu(Slot? pointer)
    {
        EnsureVisualRoot();

        var head = Slot.ActiveUserRoot?.HeadSlot;
        bool vrActive = Engine.Current?.InputInterface?.VR_Active == true;

        // View/laser facing is -Z (rotation * Backward) - Slot.Forward (+Z)
        // points behind the camera and was putting the menu out of view.
        //
        // Desktop/first-person: directly in front of the camera at half a
        // meter, so it always lands in view. VR: in front of the summoning
        // pointer. (FaceLocalUser turns it toward the viewer either way.)
        if (!vrActive && head != null)
        {
            float scale = Slot.ActiveUserRoot?.GlobalScale ?? 1f;
            var viewDirection = head.GlobalRotation * float3.Backward;
            _menuRoot.GlobalPosition = head.GlobalPosition + viewDirection * (0.5f * scale);
        }
        else if (pointer != null && !pointer.IsDestroyed)
        {
            var pointDirection = pointer.GlobalRotation * float3.Backward;
            _menuRoot.GlobalPosition = pointer.GlobalPosition + pointDirection * OpenDistance;
        }
        else if (head != null)
        {
            var viewDirection = head.GlobalRotation * float3.Backward;
            _menuRoot.GlobalPosition = head.GlobalPosition + viewDirection * (OpenDistance + 0.15f);
        }

        // Face the head ONCE here (yaw billboard). The menu is a child of the user root, so after this
        // it rides along rigidly with no per-frame world re-derivation - locked in place when you move
        // (never re-faces or re-positions the menu per frame).
        if (head != null)
        {
            var toHead = head.GlobalPosition - _menuRoot.GlobalPosition;
            toHead.y = 0f;
            if (toHead.LengthSquared > 1e-6f)
                _menuRoot.GlobalRotation = floatQ.AxisAngle(float3.Up, MathF.Atan2(toHead.x, toHead.z));
        }
    }

    private void EnsureVisualRoot()
    {
        if (_menuRoot != null && !_menuRoot.IsDestroyed)
            return;

        // The menu is a child of the user root, so it already moves + turns rigidly with you. Re-facing
        // it toward the head every frame (in world space) lagged the body translation by a frame and
        // made it jitter while moving. We face it once on open (in PositionMenu) and animate open/close
        // by scale only.
        _menuRoot = Slot.AddLocalSlot("ContextMenu");
        _font = _menuRoot.AttachComponent<FontProvider>();
        _font.URL.Value = new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");
    }

    private void RebuildVisual()
    {
        EnsureVisualRoot();

        _builtItems.Clear();
        _flickItem = null;

        _canvasSlot?.Destroy();
        _canvasSlot = _menuRoot.AddLocalSlot("Canvas");
        _canvasSlot.LocalScale.Value = float3.One * CanvasScale;

        // Canvas root size comes from a centered RectTransform on the canvas
        // slot (same convention as PanelShell).
        var rootRect = _canvasSlot.AttachComponent<RectTransform>();
        rootRect.OffsetMin.Value = new float2(-CanvasSize * 0.5f, -CanvasSize * 0.5f);
        rootRect.OffsetMax.Value = new float2(CanvasSize * 0.5f, CanvasSize * 0.5f);
        var canvas = _canvasSlot.AttachComponent<Canvas>();
        // The menu is overlay UI: it renders over everything.
        canvas.SortingOrder.Value = 10000;

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

        // Round center disc, matching the ring (a full 360-degree arc), not a
        // square image plate.
        var disc = center.AttachComponent<ArcSegment>();
        disc.AngleStart.Value = 0f;
        disc.ArcLength.Value = 360f;
        disc.InnerRadius.Value = 0f;
        disc.OuterRadius.Value = 46f;
        disc.Tint.Value = new color(0.08f, 0.09f, 0.12f, 0.92f);
        disc.OutlineColor.Value = new color(0.10f, 0.11f, 0.14f, 0.9f);
        disc.OutlineThickness.Value = 2f;

        var button = center.AttachComponent<ArcButton>();
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
        arc.OutlineColor.Value = ToColor(item.OutlineColor, new color(0.45f, 0.45f, 0.45f, 1f));

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
