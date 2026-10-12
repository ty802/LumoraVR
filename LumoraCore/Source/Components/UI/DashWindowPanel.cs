// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// A dash window out in the room. The app itself comes along, not a copy of it: the window's own screen moves
// onto the panel's canvas and back again, so a filter, a scroll, a half-typed search or a page three tabs deep
// is where it was on both sides of the trip.
//
// The panel is a PanelSurface (its canvas renders on a rig of its own, so modals and scroll panes stack as
// they do on the dash), a PanelPlacement (stay, follow the view, ride a wrist, turn to face you) and this
// chrome: a header to drag it by, with the placement buttons, dock and close; a corner that resizes the app;
// a corner that scales the whole panel. A grab anywhere on it carries it, and let go over the dash it docks
// back into its window. -xlinka
[ComponentCategory("Hidden")]
[UserspaceOnly]
public sealed class DashWindowPanel : Component
{
    public const float HeaderHeight = 44f;
    private const float ButtonSize = 32f;
    private const float ButtonGap = 4f;
    private const float GroupGap = 14f;
    private const float BarPad = 6f;
    private const float GripSize = 28f;
    private const float IconTile = 26f;
    private const float IconGlyph = 16f;
    private const float ButtonGlyph = 16f;
    private const float LiftOffDash = 0.04f;
    private const float FrontDistance = 0.9f;
    // The scale corner goes from a watch face to a cinema screen and no further.
    private const float MinHeightMeters = 0.08f;
    private const float MaxHeightMeters = 6f;
    // A header press in a headset moves the panel only from a beam that was resting on it. One that leaves
    // DragAngle (an angle at the beam's distance, so it holds on a wrist panel as on a wall-sized one) within
    // SweepSeconds of the press was a sweep crossing the header, and moves nothing at all; leaving it later is a
    // drag. A mouse is where it says it is and only has to leave MouseSlop.
    private const float DragAngle = 1.5f * MathF.PI / 180f;
    private const double SweepSeconds = 0.15;
    private const float MouseSlop = 10f;

    private sealed class ModeButton
    {
        public PanelAnchor Mode;
        public Chirality Side;
        public string Name = string.Empty;
        public Slot Slot = null!;
        public RoundedButton Button = null!;
        public ColorDriver? Fill;
        public ColorDriver? Ink;
    }

    private static DashWindowPanel? _keyFocus;

    private readonly List<ModeButton> _modes = new();
    private readonly List<Slot> _barButtons = new();
    private Grabbable? _grab;
    private PanelSurface? _surface;
    private PanelPlacement? _placement;
    private Slot? _body;
    private Text? _modeLabel;
    private string? _hoverMode;
    private bool _vrShown;
    private bool _dragging;
    private bool _dragMoving;
    private double _dragSince;
    private float2 _dragPoint;
    private float2 _sizeStart;
    private float2 _sizePoint;
    private float2? _pendingSize;
    private float _scaleDistance;
    private bool _retiring;

    public Dashboard? Owner { get; private set; }
    public DashWindow? Window { get; private set; }
    // The app on the panel: the window's own screen, moved here.
    public DashboardScreen? Screen { get; private set; }
    public PanelSurface? Surface => _surface is { IsDestroyed: false } surface ? surface : null;
    public PanelPlacement? Placement => _placement is { IsDestroyed: false } placement ? placement : null;
    public Grabbable? GrabHandle => _grab is { IsDestroyed: false } grab ? grab : null;
    public Slot? CanvasSlot => Surface?.CanvasSlot;
    public Slot? Body => _body;
    public bool Retiring => _retiring;
    public string ModeLabel => _modeLabel?.Content.Value ?? string.Empty;

    // The popped app that was pressed last. Its text fields take the keyboard, the hardware one and the
    // headset's, whether or not the dash is open, until something on the dash is pressed instead.
    public static DashWindowPanel? KeyFocus => _keyFocus is { IsDestroyed: false, Retiring: false } panel ? panel : null;

    // A popped app whose field is waiting for keys.
    public static bool TakesKeys => KeyFocus?.Screen is IDashboardKeyInput { HasFieldFocus: true };

    public static void ClearKeyFocus() => _keyFocus = null;

    public static DashWindowPanel? From(IGrabbable? item) => (item as Component)?.Slot?.GetComponent<DashWindowPanel>();

    // The panel a slot on a popped app belongs to, from anywhere on its canvas.
    public static DashWindowPanel? Of(Slot? slot) => PanelSurface.Of(slot)?.Slot?.Parent?.GetComponent<DashWindowPanel>();

    private static bool InVr => Engine.Current?.InputInterface?.IsVRActive == true;

    // SPAWN

    // Lifted off the dash where the window sits on its surface, one dash pixel per canvas unit, so on a
    // desktop it floats exactly where the window was on the screen.
    internal static DashWindowPanel? Spawn(Dashboard dash, DashWindow window)
    {
        var local = UserspaceDashboard.LocalInstance;
        if (window.Screen == null)
            return null;
        var panel = Create(dash, window, window.Size);
        if (panel == null)
            return null;
        var bounds = window.Bounds;
        var canvasSize = dash.Size.Value;
        var centre = new float2(bounds.x + bounds.width * 0.5f - canvasSize.x * 0.5f, canvasSize.y * 0.5f - (bounds.y + bounds.height * 0.5f));
        if (local != null && !local.IsDestroyed
            && local.TryCanvasToSurface(centre, out var position, out var rotation, out var metersPerPixel) && metersPerPixel > 0f)
        {
            panel.Slot.GlobalPosition = position + rotation * float3.Forward * LiftOffDash;
            panel.Slot.GlobalRotation = rotation;
            panel.Slot.LocalScale.Value = float3.One * (metersPerPixel / PanelSurface.UnitMeters);
        }
        else
        {
            panel.PlaceInFront();
        }
        panel._surface!.RefreshDensity();
        panel._placement!.Settle();
        panel._placement.Appear();
        return panel;
    }

    // Back from a save: hidden until the frame it was saved against exists, then placed and shown.
    internal static DashWindowPanel? SpawnRestored(Dashboard dash, DashWindow window, float2 size, PanelAnchor mode, Chirality side,
        in float3 position, in floatQ rotation, float scale)
    {
        if (window.Screen == null)
            return null;
        var panel = Create(dash, window, size);
        if (panel == null)
            return null;
        panel._surface!.SetShown(false);
        panel._placement!.Restore(mode, side, position, rotation, scale);
        panel.PaintModes();
        return panel;
    }

    private static DashWindowPanel? Create(Dashboard dash, DashWindow window, float2 size)
    {
        var root = Templates.Userspace.LocalRoot;
        if (root == null || root.IsDestroyed)
            root = Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot;
        if (root == null || root.IsDestroyed || !ReferenceEquals(root.World, dash.World))
            return null;
        var slot = root.AddSlot(window.Label + " Window");
        slot.Persistent.Value = false;
        var panel = slot.AttachComponent<DashWindowPanel>();
        panel.Build(dash, window, Clamp(window, size));
        return panel;
    }

    private static float2 Clamp(DashWindow window, float2 size)
        => window.ClampSize(size, window.MaxSize);

    // Arm's length along the view, at the panel's own size at one dash pixel per unit.
    private void PlaceInFront()
    {
        if (!PanelPlacement.TryHeadFrame(out var head, out var yaw))
            return;
        Slot.GlobalPosition = head + yaw * float3.Backward * FrontDistance + float3.Down * 0.1f;
        Slot.GlobalRotation = yaw;
        Slot.LocalScale.Value = float3.One * 1.4f;
    }

    // BUILD

    private void Build(Dashboard dash, DashWindow window, float2 size)
    {
        Owner = dash;
        Window = window;

        _grab = Slot.AttachComponent<Grabbable>();
        _grab.AllowGrab.Value = true;
        _grab.Scalable.Value = true;
        _grab.FollowRotation.Value = true;
        _grab.Receivable.Value = false;
        // Below the canvas: a press on a control is a press, a grab walks up to this from the surface hit.
        _grab.InteractionPriority.Value = -1;
        _grab.OnLocalGrabbed += OnGrabbed;
        _grab.OnLocalReleased += OnReleased;

        _placement = Slot.AttachComponent<PanelPlacement>();
        _placement.Changed += OnPlacementChanged;
        _placement.Placed += OnPlaced;

        _surface = PanelSurface.Create(Slot, size);
        _surface.WatchFont(dash.Font.Target);
        _surface.WatchFont(dash.FontSemibold.Target);
        _surface.WatchFont(dash.FontBold.Target);
        if (_surface.Canvas != null)
            _surface.Canvas.PressBegan += OnPressBegan;

        Slot.OnPrepareDestroy += OnPrepareDestroy;

        BuildFrame(dash, window);
        Take(window.Screen);
        _vrShown = InVr;
        LayoutBar();
        PaintModes();
    }

    private void BuildFrame(Dashboard dash, DashWindow window)
    {
        var canvas = _surface!.CanvasSlot!;
        // Clear under the frame: the capture is shown over the world, so the rounded corners are the room.
        canvas.AttachComponent<Image>().Tint.Value = color.Transparent;

        var frame = SettingsUI.Fill(canvas, "Frame");
        frame.AttachComponent<GraphicChunkRoot>();
        SettingsUI.Panel(frame, DashTheme.GlassWindow, DashTheme.OutlineStrong, DashTheme.RadiusPanel);

        BuildBar(dash, window, frame);

        _body = SettingsUI.Child(frame, "Body", float2.Zero, float2.One,
            new float2(DashWindow.BodyInset, DashWindow.BodyInset), new float2(-DashWindow.BodyInset, -HeaderHeight));
        _body.AttachComponent<Mask>();

        BuildGrip(dash, frame, "Resize", "resize", right: true, OnResizePressed, OnResizeDragged, OnResizeReleased);
        BuildGrip(dash, frame, "Scale", "scale", right: false, OnScalePressed, OnScaleDragged, OnScaleReleased);
    }

    private void BuildBar(Dashboard dash, DashWindow window, Slot frame)
    {
        var bar = SettingsUI.Child(frame, "Bar", new float2(0f, 1f), new float2(1f, 1f), new float2(0f, -HeaderHeight), float2.Zero);
        bar.AttachComponent<GraphicChunkRoot>();
        var drag = bar.AttachComponent<InteractionElement>();
        drag.Pressed += OnBarPressed;
        drag.Dragged += OnBarDragged;
        drag.Released += OnBarReleased;

        var tile = SettingsUI.Child(bar, "Icon", new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(12f, -IconTile * 0.5f), new float2(12f + IconTile, IconTile * 0.5f));
        SettingsUI.Panel(tile, window.Tint.WithAlpha(0.22f), color.Transparent, 8f);
        var glyph = SettingsUI.Child(tile, "Glyph", new float2(0.5f, 0.5f), new float2(0.5f, 0.5f),
            new float2(-IconGlyph * 0.5f, -IconGlyph * 0.5f), new float2(IconGlyph * 0.5f, IconGlyph * 0.5f));
        var image = glyph.AttachComponent<Image>();
        image.Texture.Target = dash.IconProvider(window.Icon);
        image.PreserveAspect.Value = true;
        image.InteractionTarget.Value = false;
        image.Tint.Value = window.Tint;

        float left = 12f + IconTile + 10f;
        var title = SettingsUI.Label(bar, "Title", dash.FontBold.Target ?? dash.Font.Target, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0.5f), new float2(1f, 1f), new float2(left, -2f), new float2(-8f, -4f), shrinkToFit: true);
        title.Content.Value = window.Label;
        title.InteractionTarget.Value = false;
        _modeLabel = SettingsUI.Label(bar, "Mode", dash.Font.Target, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 0.5f), new float2(left, 4f), new float2(-8f, 2f), shrinkToFit: true);
        _modeLabel.InteractionTarget.Value = false;

        AddBarButton(dash, bar, "Close", "close", "Close", DashTheme.Negative.WithAlpha(0.16f), DashTheme.Negative, Close);
        AddBarButton(dash, bar, "Dock", "workspace", "Dock", DashTheme.AccentSoft, DashTheme.Accent, DockBack);
        AddMode(dash, bar, PanelAnchor.Facing, Chirality.Left, "anchor-face", "Turns to face you");
        AddMode(dash, bar, PanelAnchor.Wrist, Chirality.Right, "anchor-wrist-right", "On your right wrist");
        AddMode(dash, bar, PanelAnchor.Wrist, Chirality.Left, "anchor-wrist-left", "On your left wrist");
        AddMode(dash, bar, PanelAnchor.Follow, Chirality.Left, "anchor-follow", "Follows your view");
        AddMode(dash, bar, PanelAnchor.World, Chirality.Left, "anchor-world", "Stays here");
    }

    private Slot AddBarButton(Dashboard dash, Slot bar, string name, string icon, string tip, in color hoverFill, in color hoverInk, Action press)
        => AddBarButton(dash, bar, name, icon, tip, hoverFill, hoverInk, press, out _, out _);

    // A button answers over its whole cell of the bar: the bar's full height and out to half the gap on either
    // side, the one at the end out to the panel's edge, so a press just off a glyph is that button and never
    // the drag strip behind it. The plate is drawn inset in the cell, square to the hit and rounded only in
    // paint. LayoutBar sizes both.
    private Slot AddBarButton(Dashboard dash, Slot bar, string name, string icon, string tip, in color hoverFill, in color hoverInk,
        Action press, out ColorDriver fill, out ColorDriver ink)
    {
        var slot = SettingsUI.Child(bar, name, new float2(1f, 0f), new float2(1f, 1f), float2.Zero, float2.Zero);
        var button = slot.AttachComponent<RoundedButton>();
        button.Tip.Value = tip;
        button.Clicked += (_, _) => press();
        var plateSlot = SettingsUI.Child(slot, "Plate", new float2(1f, 0.5f), new float2(1f, 0.5f), float2.Zero, float2.Zero);
        var plate = plateSlot.AttachComponent<RoundedPanel>();
        plate.CornerRadius.Value = 9f;
        plate.Color.Value = color.Transparent;
        plate.OutlineThickness.Value = 0f;
        var glyphSlot = SettingsUI.Child(plateSlot, "Glyph", new float2(0.5f, 0.5f), new float2(0.5f, 0.5f),
            new float2(-ButtonGlyph * 0.5f, -ButtonGlyph * 0.5f), new float2(ButtonGlyph * 0.5f, ButtonGlyph * 0.5f));
        var glyph = glyphSlot.AttachComponent<Image>();
        glyph.Texture.Target = dash.IconProvider(icon);
        glyph.PreserveAspect.Value = true;
        glyph.InteractionTarget.Value = false;
        glyph.Tint.Value = DashTheme.TextDim;
        fill = button.AddColorDriver(plate.Color, color.Transparent, InteractionColorMode.Direct);
        ink = button.AddColorDriver(glyph.Tint, DashTheme.TextDim, InteractionColorMode.Direct);
        Paint(fill, ink, color.Transparent, DashTheme.TextDim, hoverFill, hoverInk);
        _barButtons.Add(slot);
        return slot;
    }

    private void AddMode(Dashboard dash, Slot bar, PanelAnchor mode, Chirality side, string icon, string name)
    {
        var slot = AddBarButton(dash, bar, "Mode" + mode + (mode == PanelAnchor.Wrist ? side.ToString() : string.Empty), icon, name,
            DashTheme.SurfaceHover, DashTheme.Text, () => SetMode(mode, side), out var fill, out var ink);
        var button = slot.GetComponent<RoundedButton>()!;
        var entry = new ModeButton
        {
            Mode = mode,
            Side = side,
            Name = name,
            Slot = slot,
            Button = button,
            Fill = fill,
            Ink = ink,
        };
        button.HoverEntered += _ => { _hoverMode = name; PaintModes(); };
        button.HoverExited += _ => { if (_hoverMode == name) _hoverMode = null; PaintModes(); };
        _modes.Add(entry);
    }

    private static void Paint(ColorDriver? fill, ColorDriver? ink, in color normalFill, in color normalInk, in color hoverFill, in color hoverInk)
    {
        if (fill is { IsDestroyed: false })
        {
            fill.NormalColor.Value = normalFill;
            fill.HighlightColor.Value = hoverFill;
            fill.PressedColor.Value = hoverFill.WithAlpha(MathF.Min(1f, hoverFill.a * 1.6f));
            fill.DisabledColor.Value = color.Transparent;
            fill.Apply();
        }
        if (ink is { IsDestroyed: false })
        {
            ink.NormalColor.Value = normalInk;
            ink.HighlightColor.Value = hoverInk;
            ink.PressedColor.Value = hoverInk;
            ink.DisabledColor.Value = DashTheme.TextMuted.WithAlpha(0.45f);
            ink.Apply();
        }
    }

    // Right to left: close, dock, a gap, then the placements. The wrist ones only where there are wrists.
    private void LayoutBar()
    {
        float right = BarPad;
        float before = 0f;
        bool first = true;
        for (int i = 0; i < _barButtons.Count; i++)
        {
            var slot = _barButtons[i];
            bool wrist = slot.Name.Value.StartsWith("ModeWrist", StringComparison.Ordinal);
            bool shown = !wrist || _vrShown;
            if (slot.ActiveSelf.Value != shown)
                slot.ActiveSelf.Value = shown;
            if (!shown)
                continue;
            float gap = i == 1 ? GroupGap : ButtonGap;
            float cellRight = first ? 0f : right - before * 0.5f;
            float cellLeft = right + ButtonSize + gap * 0.5f;
            SettingsUI.SetOffsets(SettingsUI.Rect(slot), new float2(-cellLeft, 0f), new float2(-cellRight, 0f));
            var plate = slot.FindChild("Plate", false);
            if (plate != null)
            {
                float inset = right - cellRight;
                SettingsUI.SetOffsets(SettingsUI.Rect(plate), new float2(-(inset + ButtonSize), -ButtonSize * 0.5f), new float2(-inset, ButtonSize * 0.5f));
            }
            right += ButtonSize + gap;
            before = gap;
            first = false;
        }
        if (_modeLabel != null)
        {
            float left = 12f + IconTile + 10f;
            SettingsUI.SetOffsets(SettingsUI.Rect(_modeLabel.Slot), new float2(left, 4f), new float2(-(right + 4f), 2f));
            var title = _modeLabel.Slot.Parent?.FindChild("Title", false);
            if (title != null)
                SettingsUI.SetOffsets(SettingsUI.Rect(title), new float2(left, -2f), new float2(-(right + 4f), -4f));
        }
    }

    private void BuildGrip(Dashboard dash, Slot frame, string name, string icon, bool right,
        Action<UIInteractionContext> pressed, Action<UIInteractionContext> dragged, Action<UIInteractionContext> released)
    {
        var anchor = new float2(right ? 1f : 0f, 0f);
        var grip = SettingsUI.Child(frame, name, anchor, anchor,
            new float2(right ? -GripSize : 0f, 0f), new float2(right ? 0f : GripSize, GripSize));
        grip.AttachComponent<GraphicChunkRoot>();
        var element = grip.AttachComponent<InteractionElement>();
        element.Pressed += context => pressed(context);
        element.Dragged += context => dragged(context);
        element.Released += context => released(context);
        var glyph = SettingsUI.Child(grip, "Glyph", new float2(0.5f, 0.5f), new float2(0.5f, 0.5f),
            new float2(-7f, -7f), new float2(7f, 7f));
        var image = glyph.AttachComponent<Image>();
        image.Texture.Target = dash.IconProvider(icon);
        image.PreserveAspect.Value = true;
        image.InteractionTarget.Value = false;
        image.Tint.Value = DashTheme.TextMuted;
    }

    // THE APP

    private void Take(DashboardScreen? screen)
    {
        if (screen == null || screen.IsDestroyed || _body == null)
            return;
        Screen = screen;
        // A dialog the app had up on the dash stays with the dash.
        screen.Slot.GetComponentInParents<ModalHost>()?.CloseAll();
        Canvas.Move(screen.Slot, _body);
        screen.ShowScreen();
    }

    // The app goes home to its window: docked back, closed, or this panel going away under it.
    internal void ReturnScreen(bool hide)
    {
        var screen = Screen;
        var window = Window;
        Screen = null;
        if (ReferenceEquals(_keyFocus, this))
            _keyFocus = null;
        if (screen == null || screen.IsDestroyed || window == null || window.IsDestroyed || window.Body == null || window.Body.IsDestroyed)
            return;
        if (hide)
            screen.HideScreen();
        else
            CanvasSlot?.GetComponent<ModalHost>()?.CloseAll();
        Canvas.Move(screen.Slot, window.Body);
    }

    // Away: the app back in its window, then this panel gone, after a short shrink when asked for one.
    internal void Retire(bool animate)
    {
        if (_retiring)
        {
            if (!animate)
                Finish();
            return;
        }
        _retiring = true;
        if (ReferenceEquals(_keyFocus, this))
            _keyFocus = null;
        var placement = Placement;
        if (animate && placement != null && !placement.Pending && Surface is { Shown: true })
            placement.Vanish(Finish);
        else
            Finish();
    }

    private void Finish()
    {
        if (Screen != null)
            ReturnScreen(hide: true);
        if (!IsDestroyed && Slot != null && !Slot.IsDestroyed)
            Slot.Destroy();
    }

    private void OnPrepareDestroy(Slot slot)
    {
        // The app rides a rig that goes with this panel: it goes home before anything is taken apart.
        if (Screen != null)
            ReturnScreen(hide: true);
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.OnPrepareDestroy -= OnPrepareDestroy;
        if (ReferenceEquals(_keyFocus, this))
            _keyFocus = null;
        if (_grab != null)
        {
            _grab.OnLocalGrabbed -= OnGrabbed;
            _grab.OnLocalReleased -= OnReleased;
        }
        base.OnDestroy();
    }

    // PLACEMENT

    public void SetMode(PanelAnchor mode, Chirality side = Chirality.Left)
    {
        var placement = Placement;
        if (placement == null || (mode == PanelAnchor.Wrist && !InVr))
            return;
        placement.SetMode(mode, side);
        Surface?.RefreshDensity();
        PaintModes();
    }

    private void PaintModes()
    {
        var placement = Placement;
        string current = "Stays here";
        foreach (var entry in _modes)
        {
            bool on = placement != null && placement.Mode == entry.Mode && (entry.Mode != PanelAnchor.Wrist || placement.Side == entry.Side);
            if (on)
                current = entry.Name;
            Paint(entry.Fill, entry.Ink, on ? DashTheme.AccentSoft : color.Transparent, on ? DashTheme.Accent : DashTheme.TextDim,
                DashTheme.SurfaceHover, DashTheme.Text);
        }
        if (_modeLabel != null)
            ListingStyle.SetText(_modeLabel, _hoverMode ?? current);
    }

    private void OnPlacementChanged() => Window?.Workspace?.NoteChange();

    private void OnPlaced()
    {
        Surface?.SetShown(true);
        Surface?.RefreshDensity();
        Placement?.Appear();
        PaintModes();
    }

    private void OnGrabbed(IGrabbable item)
    {
        // Pointers first: a header or corner drag let go here hands the panel back to its anchor, and the grab has it.
        Surface?.Canvas?.ReleaseAllPointers();
        if (Placement != null)
            Placement.Held = true;
    }

    private void OnReleased(IGrabbable item)
    {
        var placement = Placement;
        if (placement == null)
            return;
        placement.Held = false;
        Surface?.RefreshDensity();
        placement.Settle();
    }

    private void OnPressBegan(IUIInteractable? target, UIInteractionContext context) => _keyFocus = this;

    // HEADER DRAG: the panel slides in its own plane under the pointer. The point the drag began at is in the
    // canvas's units, and the panel moves by however far the pointer has got from it, so it stays under it.

    private void OnBarPressed(UIInteractionContext context)
    {
        _dragging = true;
        _dragMoving = false;
        _dragPoint = context.LocalPoint;
        _dragSince = World?.Time.TotalTime ?? 0.0;
    }

    private void OnBarDragged(UIInteractionContext context)
    {
        if (!_dragging)
            return;
        if (!_dragMoving)
        {
            bool headset = FromHeadset(in context);
            float slop = headset ? Margin(in context, DragAngle) : 0f;
            if (slop <= 0f)
                slop = MouseSlop;
            if ((context.LocalPoint - _dragPoint).LengthSquared <= slop * slop)
                return;
            if (headset && (World?.Time.TotalTime ?? 0.0) - _dragSince < SweepSeconds)
            {
                _dragging = false;
                return;
            }
            _dragMoving = true;
            if (Placement != null)
                Placement.Held = true;
        }
        var delta = context.LocalPoint - _dragPoint;
        float meters = PanelSurface.UnitMeters * Slot.LocalScale.Value.y;
        Slot.GlobalPosition += Slot.GlobalRotation * new float3(delta.x * meters, delta.y * meters, 0f);
    }

    private void OnBarReleased(UIInteractionContext context)
    {
        bool moved = _dragging && _dragMoving;
        _dragging = false;
        _dragMoving = false;
        if (!moved)
            return;
        var placement = Placement;
        if (placement != null)
        {
            placement.Held = false;
            placement.Settle();
        }
    }

    // The desktop mouse reaches the panel as a laser too; only a worn headset gets the angular rules.
    private static bool FromHeadset(in UIInteractionContext context)
        => context.Source is (UIInteractionSource.VRLeft or UIInteractionSource.VRRight) && InVr;

    // An angle at the beam's distance, in the canvas's units.
    private static float Margin(in UIInteractionContext context, float angle)
    {
        float scale = MathF.Abs(context.Canvas?.Slot?.GlobalScale.x ?? 0f);
        return scale > 0.000001f ? context.Distance * angle / scale : 0f;
    }

    // RESIZE: the app gets more room; the canvas grows from its top-left corner, so the corner stays on the
    // pointer. One size per finished rebuild, the same rule the dash windows keep.

    private void OnResizePressed(UIInteractionContext context)
    {
        _sizeStart = Surface?.Size ?? float2.Zero;
        _sizePoint = context.LocalPoint;
        if (Placement != null)
            Placement.Held = true;
    }

    private void OnResizeDragged(UIInteractionContext context)
    {
        if (Window == null)
            return;
        var delta = context.LocalPoint - _sizePoint;
        _pendingSize = Clamp(Window, new float2(_sizeStart.x + delta.x, _sizeStart.y - delta.y));
        ApplyPendingSize(force: false);
    }

    private void OnResizeReleased(UIInteractionContext context)
    {
        if (Window != null)
        {
            var delta = context.LocalPoint - _sizePoint;
            _pendingSize = Clamp(Window, new float2(_sizeStart.x + delta.x, _sizeStart.y - delta.y));
        }
        ApplyPendingSize(force: true);
        Surface?.RefreshDensity();
        var placement = Placement;
        if (placement != null)
        {
            placement.Held = false;
            placement.Settle();
        }
    }

    // The app's size out here, from a hand or from a save.
    public void Resize(float2 size)
    {
        if (Window == null)
            return;
        _pendingSize = Clamp(Window, size);
        ApplyPendingSize(force: true);
        Surface?.RefreshDensity();
        Window.Workspace?.NoteChange();
    }

    private void ApplyPendingSize(bool force)
    {
        var surface = Surface;
        if (_pendingSize == null || surface == null)
            return;
        if (!force && surface.Canvas is { IsRebuildInProgress: true })
            return;
        surface.SetSize(_pendingSize.Value);
        _pendingSize = null;
    }

    // SCALE: the whole panel grows or shrinks about its middle, so the corner stays on the pointer.

    private void OnScalePressed(UIInteractionContext context)
    {
        var surface = Surface;
        _scaleDistance = surface != null ? (context.LocalPoint - surface.Center).Length : 0f;
        if (Placement != null)
            Placement.Held = true;
    }

    private void OnScaleDragged(UIInteractionContext context)
    {
        var surface = Surface;
        if (surface == null || _scaleDistance < 1f)
            return;
        float distance = (context.LocalPoint - surface.Center).Length;
        if (distance < 1f)
            return;
        ScaleBy(distance / _scaleDistance);
    }

    private void OnScaleReleased(UIInteractionContext context)
    {
        Surface?.RefreshDensity();
        var placement = Placement;
        if (placement != null)
        {
            placement.Held = false;
            placement.Settle();
        }
    }

    // The panel's scale times k, inside the height limits, about the middle of what it shows.
    public void ScaleBy(float k)
    {
        var surface = Surface;
        if (surface == null || k <= 0f || float.IsNaN(k))
            return;
        float scale = Slot.LocalScale.Value.y;
        float height = surface.Size.y * PanelSurface.UnitMeters;
        if (height <= 0f)
            return;
        float next = System.Math.Clamp(scale * k, MinHeightMeters / height, MaxHeightMeters / height);
        if (MathF.Abs(next - scale) < 1e-6f)
            return;
        var before = surface.Slot.GlobalPosition;
        Slot.LocalScale.Value = float3.One * next;
        Slot.GlobalPosition += before - surface.Slot.GlobalPosition;
    }

    // UPDATE

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        bool vr = InVr;
        if (vr != _vrShown)
        {
            _vrShown = vr;
            LayoutBar();
            if (!vr && Placement is { Mode: PanelAnchor.Wrist } placement)
                placement.SetMode(PanelAnchor.World, placement.Side);
            PaintModes();
        }
        ApplyPendingSize(force: false);
        Window?.Workspace?.TickSave();
    }

    // DOCK, CLOSE, SAVE

    public void DockBack()
    {
        var window = Window;
        if (window != null && !window.IsDestroyed && window.Workspace != null)
            window.Workspace.DockBack(window);
        else
            Retire(animate: false);
    }

    public void Close()
    {
        var window = Window;
        if (window != null && !window.IsDestroyed && window.Workspace != null)
            window.Workspace.Close(window);
        else
            Retire(animate: true);
    }

    // key=mode,side,scale,width,height,x,y,z,qx,qy,qz,qw with the pose in the frame its mode keeps.
    internal bool TryDescribe(out string entry)
    {
        entry = string.Empty;
        var placement = Placement;
        var surface = Surface;
        if (Window == null || placement == null || surface == null || _retiring || !placement.TryPoseInFrame(out var p, out var q))
            return false;
        var size = surface.Size;
        entry = Window.Key + "=" + (int)placement.Mode + "," + (int)placement.Side
            + "," + F(Slot.LocalScale.Value.y) + "," + F(size.x) + "," + F(size.y)
            + "," + F(p.x) + "," + F(p.y) + "," + F(p.z)
            + "," + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
        return true;
    }

    private static string F(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}
