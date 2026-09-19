// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The in-VR typing surface. It lives in userspace beside the dash rather than inside it: a focused
// field can be on the dash, on a world panel or on an inspector, and one keyboard has to reach all
// three. Keys drive the focused TextInput through its public typing surface, so the caret, the
// selection replace and the length clamp are the same code the hardware keyboard runs.
//
// Showing is driven by focus, not by a button: a TextInput holding the static focus, or a dash
// screen reporting one of its inline fields focused. Neither has a focus-changed event, so this polls
// each common update. That poll has to keep running while the panel is HIDDEN, which is why
// CanRunUpdates drops the active-slot gate below - an inactive slot gets no updates, and a keyboard
// that only wakes up while it is already visible never appears. The update order puts that poll
// ahead of UserspacePointer (-1000), so the frame the panel appears is already the frame a laser
// wakes for it rather than a frame of keyboard nothing can press. -xlinka
[ComponentCategory("UI")]
[DefaultUpdateOrder(-1100)]
public sealed class VrKeyboard : Component
{
    // Canvas units. Left column is a preview strip over four glyph rows and a modifier row; the right
    // column carries the numeric pad up top and the arrow cluster at the bottom.
    private const float PanelWidth = 800f;
    private const float PanelHeight = 276f;
    private const float PanelScale = 0.0005f;
    private const float Padding = 6f;
    private const float RowSpacing = 4f;
    private const float KeyGap = 3f;
    private const float PreviewHeight = 32f;
    private const float KeyHeight = 33f;
    private const float KeyWidth = 49f;
    private const float LabelSize = 16f;
    // The corner glyph on a dual-face key, as a fraction of the main face.
    private const float AltLabelScale = 0.55f;
    private const float PreviewSize = 15f;
    private const float CornerRadius = 8f;
    private const float LabelInset = 8f;

    // Right column: three keys wide, so the numpad and the arrow cluster share one width.
    private const float SideColumnWidth = KeyWidth * 3f + KeyGap * 2f;
    // Breathing room between the main grid and the right column.
    private const float SideColumnGap = 7f;
    private const float NumpadHeight = KeyHeight * 5f + RowSpacing * 4f;
    private const float ArrowsHeight = KeyHeight * 2f + RowSpacing;

    // Modifier row, left to right. Sums with the eight gaps to exactly the padded content width.
    private const float ShiftKeyWidth = 78f;
    private const float TabKeyWidth = 48f;
    private const float CopyKeyWidth = 52f;
    private const float SpaceKeyWidth = 130f;
    private const float PasteKeyWidth = 52f;
    private const float BackspaceKeyWidth = 78f;
    private const float EnterKeyWidth = 66f;
    private const float EscapeKeyWidth = 44f;
    private const float CloseKeyWidth = 56f;

    // Re-place a panel WE put down when it has drifted out of comfortable reach: too far, or too far
    // around the head to read without turning. One the user has moved by hand stays where they left
    // it, in reach or not; that placement is theirs.
    private const float ReachDistance = 1f;
    private const float ReachAngleDegrees = 70f;
    // Late updates after a show that keep re-docking. The dash positions itself in its own late
    // update, after ours, so the frame it opens the surface is still where it last was.
    private const int DockSettleFrames = 2;

    // Keys are poked through the userspace hand's CanvasTouchSource like any other panel; the press,
    // release, hover and punch-through depths it ships with are the ones this keyboard was tuned on.

    // One band above the dash surface (UserspaceDashboard.DashSurfaceSortingOrder) and below the
    // laser cursor bands, so the keyboard covers the dash it is typing into and the pointer still
    // shows on top of the keys. -xlinka
    private const int KeyboardSortingOrder = 20100;

    private static readonly color KeyFill = new color(0.22f, 0.20f, 0.34f, 1f);
    private static readonly color KeyBorder = new color(0.52f, 0.46f, 0.82f, 0.45f);
    private static readonly color PanelBorder = new color(0.52f, 0.46f, 0.82f, 0.55f);
    // Held caps reads brighter than the one-shot accent so the two shift states are never confused.
    private static readonly color ShiftHeldFill = new color(0.62f, 0.55f, 0.98f, 1f);
    // A key that is present but cannot do anything - Paste on a platform with no clipboard.
    private static readonly color KeyDisabledFill = new color(0.15f, 0.14f, 0.20f, 1f);

    private static readonly string[] GlyphRows =
    {
        "1234567890-=",
        "qwertyuiop[]",
        "asdfghjkl;'\\",
        "zxcvbnm,./",
    };

    // Index-aligned with GlyphRows: row r, column c on one is the same key on the other.
    private static readonly string[] ShiftGlyphRows =
    {
        "!@#$%^&*()_+",
        "QWERTYUIOP{}",
        "ASDFGHJKL:\"|",
        "ZXCVBNM<>?",
    };

    // Numeric pad, top row first. These type themselves whether or not shift is up - there is no
    // second face for a number pad, and nobody wants a shifted 7 to come out as an ampersand.
    private static readonly string[] NumpadRows =
    {
        "789",
        "456",
        "123",
        "0.-",
        "+*/",
    };

    // Shift is applying right now, whether that is a one-shot or a held caps.
    public readonly Sync<bool> ShiftActive;
    // Sticky: shift survives the next character instead of clearing after it.
    public readonly Sync<bool> ShiftHeld;
    public readonly Sync<float> Distance;
    public readonly Sync<float> DropBelowGaze;
    // Seconds a repeating key is held before it starts re-firing, and the gap between re-fires after
    // that. The defaults are the usual OS shape: long enough that a normal keypress never repeats,
    // fast enough that a held backspace clears a line at a sensible rate.
    public readonly Sync<float> RepeatDelay;
    public readonly Sync<float> RepeatInterval;
    // How long after a shift tap a second tap still counts as a DOUBLE tap and locks caps on.
    public readonly Sync<float> ShiftHoldWindow;
    // Seconds the panel stays up after the field it was typing into loses focus. A field that blurs
    // and refocuses inside the window (Enter into the next field, a tap that bounces focus) keeps the
    // keyboard where it is instead of hiding and re-showing it.
    public readonly Sync<float> HideGrace;

    private readonly SyncRef<Text> _preview;
    private readonly SyncRef<VrKey> _shiftKey;
    private readonly SyncRef<VrKey> _pasteKey;

    // Per-peer handles: the panel is rebuilt locally, never loaded, so these are refilled by
    // BuildLayout rather than replicated.
    private readonly List<VrKey> _keys = new();
    private UITheme? _theme;
    private Canvas? _canvas;
    private Grabbable? _grab;
    private bool _built;
    // Whether a field has held focus since the panel came up. Without it, a keyboard summoned by
    // hand with nothing focused would hide itself on its very first update.
    private bool _typingSession;
    // World time the focus went away, NaN while it is held. The hide waits HideGrace from here.
    private double _focusLostAt = double.NaN;
    // Whether the panel has ever been placed, and whether the user has moved it since. A panel the
    // user moved is never re-docked; one we placed is re-docked only when it drifts out of reach.
    private bool _placed;
    private bool _userPlaced;
    private int _dockFrames;
    // World time of the last shift tap, for the double-tap window.
    private double _lastShiftTap = double.NegativeInfinity;

    public static VrKeyboard? LocalInstance { get; private set; }

    // What UserspacePointer reads to keep a laser alive for the keyboard the way it does for the dash.
    public static bool LocalShown => LocalInstance is { IsDestroyed: false } keyboard && keyboard.IsShown;

    public bool IsShown => Slot != null && !Slot.IsDestroyed && Slot.ActiveSelf.Value;

    // Held by a hand right now. Keys do nothing while this holds: a grab is a move, not a press.
    public bool IsGrabbed => _grab is { IsDestroyed: false, IsGrabbed: true };

    // Whether a text field wants the keyboard: the static TextInput focus, or a dash screen with one of
    // its own inline fields focused. The dash being open, or its screen being ABLE to take keys, is
    // not a field. -xlinka
    public static bool WantsKeys => TextInput.Focused is { IsDestroyed: false } || DashFieldFocused();

    // The focus poll is the whole point of this component and it has to run while the panel is off.
    protected override bool CanRunUpdates => !IsDestroyed && Slot != null && !Slot.IsDestroyed;

    public VrKeyboard()
    {
        ShiftActive = new Sync<bool>(this, false);
        ShiftHeld = new Sync<bool>(this, false);
        Distance = new Sync<float >(this, 0.55f);
        DropBelowGaze = new Sync<float>(this, 0.2f);
        RepeatDelay = new Sync<float>(this, 0.45f);
        RepeatInterval = new Sync<float>(this, 0.05f);
        ShiftHoldWindow = new Sync<float>(this, 0.5f);
        HideGrace = new Sync<float>(this, 0.3f);
        _preview = new SyncRef<Text>(this);
        _shiftKey = new SyncRef<VrKey>(this);
        _pasteKey = new SyncRef<VrKey>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        LocalInstance = this;
        BuildLayout();
        RefreshShiftVisuals();
        RefreshPasteKey();
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(LocalInstance, this))
            LocalInstance = null;
        if (_grab != null)
        {
            _grab.OnLocalGrabbed -= OnGrabbed;
            _grab.OnLocalReleased -= OnReleased;
        }
        _keys.Clear();
        base.OnDestroy();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        var focused = TextInput.Focused;

        // Up while a field has focus, down once it has gone; the same rule for a TextInput anywhere
        // and for a dash screen's inline field. Dash screens do not focus a TextInput (their fields
        // take raw keystrokes through the dashboard's feed), so they report their own field focus
        // through IDashboardKeyInput instead. A screen that merely COULD take keys is not a reason to
        // be up: that rule put the keyboard over every dash screen you opened. -xlinka
        if (WantsKeys)
        {
            _typingSession = true;
            _focusLostAt = double.NaN;
            // Auto-show is VR-only: on desktop the hardware keyboard is right there and a panel
            // covering the view would be in the way. Show() itself stays open to anyone.
            if (!IsShown && Engine.Current?.InputInterface?.IsVRActive == true)
                Show();
        }
        else if (_typingSession && IsShown)
        {
            // Not straight away: a field that blurs and refocuses within the grace (Enter moving to
            // the next field, a tap that bounces focus) keeps the panel up rather than blinking it.
            double now = World?.Time?.TotalTime ?? 0.0;
            if (double.IsNaN(_focusLostAt))
                _focusLostAt = now;
            else if (now - _focusLostAt >= HideGrace.Value)
                Hide();
        }
        else if (_typingSession)
        {
            _typingSession = false;
            _focusLostAt = double.NaN;
        }

        if (IsShown)
            UpdatePreview(focused);
    }

    private static bool DashFieldFocused()
    {
        var input = Engine.Current?.InputInterface;
        if (input == null || !input.IsDashboardOpen)
            return false;
        var dash = UserspaceDashboard.LocalInstance;
        if (dash == null || dash.IsDestroyed || !dash.IsOpen.Value)
            return false;
        return dash.Dashboard?.CurrentScreen is IDashboardKeyInput keys && keys.HasFieldFocus;
    }

    // SHOW / HIDE

    public void Show()
    {
        if (Slot == null || Slot.IsDestroyed)
            return;

        // Coming up for a field that is already focused counts as the start of a typing session, so
        // the poll knows to put the panel away again when that focus goes.
        if (WantsKeys)
        {
            _typingSession = true;
            _focusLostAt = double.NaN;
        }

        Slot.ActiveSelf.Value = true;
        // The transform is the user's once they have moved the panel, and ours until then: the first
        // show docks it, a later show finds it where it was left, and only a panel we placed that has
        // drifted out of reach gets docked again. Hide/show and dash open/close never move it. -xlinka
        if (!_placed || (!_userPlaced && !IsWithinReach()))
        {
            Dock();
            _dockFrames = DockSettleFrames;
        }
        // The clipboard service is wired at startup, before userspace exists, but a harness (or a
        // platform that hands one over late) can arrive after the panel was built.
        RefreshPasteKey();
    }

    // Putting the keyboard away ends the typing session too. Leaving the field focused would keep
    // the whole hardware keyboard gated off (InputInterface.TextFocusHeld) with nothing on screen
    // saying why you cannot walk. -xlinka
    public void Hide()
    {
        PokeCanvas()?.ReleaseAllPointers();
        if (Slot != null && !Slot.IsDestroyed)
            Slot.ActiveSelf.Value = false;
        _typingSession = false;
        _focusLostAt = double.NaN;
        _dockFrames = 0;
        BlurFocused();
    }

    // GRAB
    // The panel is an ordinary grabbable: the userspace pointer's laser and a hand both pick it up
    // through the Grabber on the pointer rig, two hands stretch it, and it turns with the hand that
    // holds it. What the keyboard adds is that a grab is never a keypress. -xlinka

    private void OnGrabbed(IGrabbable item)
    {
        _userPlaced = true;
        _dockFrames = 0;
        // Whatever was down on a key when the hand closed is cancelled, not submitted, and the hands
        // stay disarmed for poking until each tip has come back out of the panel.
        Touch.CanvasTouchSource.DisarmAll(World);
        PokeCanvas()?.ReleaseAllPointers();
    }

    private void OnReleased(IGrabbable item)
    {
        Touch.CanvasTouchSource.DisarmAll(World);
        PokeCanvas()?.ReleaseAllPointers();
    }

    private static void BlurFocused()
    {
        var focused = TextInput.Focused;
        if (focused is { IsDestroyed: false })
            focused.Unfocus();
    }

    // With the dash up, the first show hangs the keyboard off its bottom edge, tilted up toward the
    // wearer. Placing both from the head independently put the keyboard right in front of the panel
    // it was typing into. With no dash it goes in front of the head instead. -xlinka
    private const float DockGap = 0.06f;
    private const float DockTowardViewer = 0.18f;
    private const float DockTiltDegrees = -45f;

    public override void OnLateUpdate(float delta)
    {
        base.OnLateUpdate(delta);
        if (_dockFrames <= 0 || !IsShown || IsGrabbed)
            return;
        _dockFrames--;
        Dock();
    }

    private void Dock()
    {
        if (Slot == null || Slot.IsDestroyed)
            return;
        _placed = true;

        var dash = UserspaceDashboard.LocalInstance;
        var surface = dash != null && !dash.IsDestroyed && dash.IsOpen.Value ? dash.SurfaceSlot : null;
        if (surface == null || Engine.Current?.InputInterface?.IsVRActive != true)
        {
            PlaceInFrontOfHead();
            return;
        }

        float halfHeight = dash!.DisplayHeight.Value * 0.5f;
        var local = new float3(0f, -halfHeight - DockGap, DockTowardViewer);
        Slot.GlobalPosition = surface.LocalPointToGlobal(local);
        // AxisAngle takes radians; the constant is in degrees so it reads as a tilt.
        Slot.GlobalRotation = surface.GlobalRotation * floatQ.AxisAngleRad(float3.Right, DockTiltDegrees * (MathF.PI / 180f));
        Slot.LocalScale.Value = float3.One * PanelScale;
    }

    private void PlaceInFrontOfHead()
    {
        if (Slot == null || Slot.IsDestroyed)
            return;

        var head = HeadSlot();
        if (head == null)
        {
            Slot.LocalPosition.Value = new float3(0f, 1.4f, -Distance.Value);
            Slot.LocalRotation.Value = floatQ.Identity;
            Slot.LocalScale.Value = float3.One * PanelScale;
            return;
        }

        Slot.GlobalPosition = head.GlobalPosition
            + FlatViewForward(head) * Distance.Value
            + float3.Down * DropBelowGaze.Value;

        // Yaw so the panel's readable +Z face turns back at the head. Never floatQ.LookRotation here:
        // it hands back the inverse rotation and the panel goes edge-on at oblique angles. -xlinka
        var toViewer = head.GlobalPosition - Slot.GlobalPosition;
        toViewer.y = 0f;
        if (toViewer.LengthSquared > 1e-6f)
            Slot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z));

        Slot.LocalScale.Value = float3.One * PanelScale;
    }

    private bool IsWithinReach()
    {
        var head = HeadSlot();
        // No head to measure against is not a reason to yank the panel somewhere else.
        if (head == null || Slot == null || Slot.IsDestroyed)
            return true;

        var offset = Slot.GlobalPosition - head.GlobalPosition;
        if (offset.Length > ReachDistance)
            return false;

        var flat = new float3(offset.x, 0f, offset.z);
        if (flat.LengthSquared <= 1e-6f)
            return false;

        float alignment = float3.Dot(flat.Normalized, FlatViewForward(head));
        return alignment >= MathF.Cos(ReachAngleDegrees * (MathF.PI / 180f));
    }

    private Slot? HeadSlot()
    {
        var head = World?.LocalUser?.Root?.HeadSlot;
        return head != null && !head.IsDestroyed ? head : null;
    }

    private static float3 FlatViewForward(Slot head)
    {
        float3 forward = head.GlobalRotation * float3.Backward; // view forward is -Z
        forward.y = 0f;
        return forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;
    }

    // POKE
    // Pushing a fingertip into the panel presses whatever key it lands on. That is the general
    // CanvasTouchSource on each userspace hand driving this canvas like any other; the one thing the
    // keyboard adds is that a grab is never a keypress (see OnGrabbed). The holding hand's tip sits inside
    // the panel for as long as the hold lasts, and the source skips a panel held by its own hand. -xlinka

    private Canvas? PokeCanvas()
    {
        if (_canvas != null && !_canvas.IsDestroyed)
            return _canvas;
        _canvas = Slot != null && !Slot.IsDestroyed ? Slot.GetComponent<Canvas>() : null;
        return _canvas;
    }

    // KEY DISPATCH

    public void HandleKey(VrKey key)
    {
        if (key == null || key.IsDestroyed || IsDestroyed || IsGrabbed)
            return;

        switch (key.Role.Value)
        {
            case VrKeyRole.Shift:
                CycleShift();
                return;
            case VrKeyRole.Close:
                Hide();
                return;
            case VrKeyRole.Escape:
                // Cancel the edit without committing it; the focus poll puts the panel away next
                // update. With no field focused the dashboard is the one editing, so it gets the
                // cancel instead - otherwise Escape is a dead key on every dash screen.
                if (TextInput.Focused is { IsDestroyed: false })
                    BlurFocused();
                else
                    UserspaceDashboard.LocalInstance?.FeedEscape();
                return;
        }

        var input = TextInput.Focused;
        if (input == null || input.IsDestroyed)
        {
            // Nothing owns a text caret, so the dashboard is the consumer. Its feed already routes at
            // the current screen and falls back to the file-browser search well, which is exactly what
            // the desktop key handler does with the same keystrokes.
            FeedDashboard(key);
            return;
        }

        switch (key.Role.Value)
        {
            case VrKeyRole.Character:
                input.TypeString(key.ActiveGlyph(ShiftActive.Value));
                ConsumeOneShotShift();
                break;
            case VrKeyRole.Space:
                input.TypeString(" ");
                break;
            case VrKeyRole.Backspace:
                input.PressBackspace();
                break;
            case VrKeyRole.Enter:
                input.PressEnter();
                break;
            case VrKeyRole.Tab:
                input.PressTab();
                break;
            case VrKeyRole.ArrowLeft:
                input.MoveCaretBy(-1);
                break;
            case VrKeyRole.ArrowRight:
                input.MoveCaretBy(1);
                break;
            case VrKeyRole.ArrowUp:
                input.MoveCaretLine(up: true);
                break;
            case VrKeyRole.ArrowDown:
                input.MoveCaretLine(up: false);
                break;
            case VrKeyRole.Copy:
                input.CopySelection();
                break;
            case VrKeyRole.Paste:
                input.PasteClipboard();
                break;
        }
    }

    private void FeedDashboard(VrKey key)
    {
        var dash = UserspaceDashboard.LocalInstance;
        if (dash == null || dash.IsDestroyed)
            return;

        switch (key.Role.Value)
        {
            case VrKeyRole.Character:
                string glyph = key.ActiveGlyph(ShiftActive.Value);
                if (!string.IsNullOrEmpty(glyph))
                    dash.FeedSearchChar(glyph[0]);
                ConsumeOneShotShift();
                break;
            case VrKeyRole.Space:
                dash.FeedSearchChar(' ');
                break;
            case VrKeyRole.Backspace:
                dash.FeedSearchBackspace();
                break;
            case VrKeyRole.Enter:
                dash.FeedEnter();
                break;
            case VrKeyRole.Tab:
                break;
        }
    }

    // SHIFT STATE
    // Off -> one-shot -> held -> off. A one-shot clears itself after the next character key; held
    // rides through as many as you like. Two QUICK taps lock caps, a third drops it. -xlinka

    public void CycleShift()
    {
        double now = World?.Time?.TotalTime ?? 0.0;
        double sinceLast = now - _lastShiftTap;

        if (ShiftHeld.Value)
        {
            ShiftHeld.Value = false;
            ShiftActive.Value = false;
        }
        else if (ShiftActive.Value)
        {
            // Caps locks on a genuine double tap only. A second tap a minute later is somebody
            // reaching for shift again and finding it already armed - locking caps on there is a
            // surprise you then have to notice and undo, so it cancels the one-shot instead. -xlinka
            if (sinceLast <= ShiftHoldWindow.Value)
                ShiftHeld.Value = true;
            else
                ShiftActive.Value = false;
        }
        else
        {
            ShiftActive.Value = true;
        }

        _lastShiftTap = now;
        RefreshShiftVisuals();
    }

    private void ConsumeOneShotShift()
    {
        if (!ShiftActive.Value || ShiftHeld.Value)
            return;
        ShiftActive.Value = false;
        RefreshShiftVisuals();
    }

    // One pass over the registered keys instead of a driver component per key: fifty drivers on a
    // panel that already re-meshes its whole row on a text change buys nothing. -xlinka
    private void RefreshShiftVisuals()
    {
        bool shift = ShiftActive.Value;
        for (int i = 0; i < _keys.Count; i++)
        {
            var key = _keys[i];
            if (key != null && !key.IsDestroyed)
                key.ApplyShiftState(shift);
        }

        var shiftKey = _shiftKey.Target;
        if (shiftKey == null || shiftKey.IsDestroyed || shiftKey.Slot == null)
            return;

        color fill = KeyFill;
        if (ShiftHeld.Value)
            fill = ShiftHeldFill;
        else if (ShiftActive.Value)
            fill = InspectorUI.AccentColor;

        // Straight through the shared tint rule: a Helio button DRIVES its own backing tint, so a
        // direct write is reverted on the driver's next pass.
        InspectorUI.ApplyStateTint(shiftKey.Slot.GetComponent<BorderedImage>()?.Tint, fill);
    }

    // A platform with no clipboard service gets a Paste key that says so: muted fill, muted label, and
    // Interactable off so it cannot even be pressed. The alternative is a key that looks live and
    // silently does nothing, which is the worst of both. -xlinka
    private void RefreshPasteKey()
    {
        var key = _pasteKey.Target;
        if (key == null || key.IsDestroyed || key.Slot == null)
            return;

        bool available = TextInput.ClipboardAvailable;

        var button = key.Slot.GetComponent<Button>();
        if (button != null && !button.IsDestroyed)
            button.Interactable.Value = available;

        InspectorUI.ApplyStateTint(key.Slot.GetComponent<BorderedImage>()?.Tint, available ? KeyFill : KeyDisabledFill);

        var label = key.Label.Target;
        if (label != null && !label.IsDestroyed)
            label.Color.Value = available ? InspectorUI.TextColor : InspectorUI.MutedColor;
    }

    // PREVIEW

    private void UpdatePreview(TextInput? input)
    {
        var preview = _preview.Target;
        if (preview == null || preview.IsDestroyed)
            return;

        string next = string.Empty;
        if (input != null && !input.IsDestroyed)
        {
            string value = input.Text.Value ?? string.Empty;
            // A password field stays a password field on the big readable panel in front of your face.
            if (input.Mask.Value && value.Length > 0)
                value = new string('•', value.Length);
            int caret = System.Math.Clamp(input.CaretIndex, 0, value.Length);
            next = value.Insert(caret, "|");
        }

        // Only on change: writing the content every frame re-tessellates the strip's chunk every frame.
        if (preview.Content.Value != next)
            preview.Content.Value = next;
    }

    // LAYOUT

    private void BuildLayout()
    {
        if (_built || Slot == null || Slot.IsDestroyed)
            return;
        _built = true;

        _theme = Slot.GetOrAttachComponent<UITheme>();
        // Borrow the dash's font provider when there is one: same world so the ref is legal, one glyph
        // atlas between the two panels, and the keys never come up blank because the theme's own
        // default font URL had not been registered yet. -xlinka
        var dash = UserspaceDashboard.LocalInstance;
        if (dash != null && !dash.IsDestroyed && ReferenceEquals(dash.World, World) && dash.Font.Target != null)
            _theme.Font.Target = dash.Font.Target;
        _theme.PanelBackground.Value = InspectorUI.PaneColor;
        _theme.Header.Value = InspectorUI.HeaderColor;
        _theme.ButtonFill.Value = KeyFill;
        _theme.Accent.Value = InspectorUI.AccentColor;
        _theme.Separator.Value = PanelBorder;
        _theme.Border.Value = KeyBorder;
        _theme.CornerRadius.Value = CornerRadius;

        var rect = Slot.GetOrAttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(PanelWidth * -0.5f, PanelHeight * -0.5f);
        rect.OffsetMax.Value = new float2(PanelWidth * 0.5f, PanelHeight * 0.5f);

        var canvas = Slot.GetOrAttachComponent<Canvas>();
        // Overlay + a reserved band: a keyboard that world geometry can cut into is unusable, and the
        // depth test alone would clip it against whatever you happen to be standing in front of.
        canvas.Overlay.Value = true;
        canvas.SortingOrder.Value = KeyboardSortingOrder;
        _canvas = canvas;

        // Grabbable and scalable so a user can put it where they like and size it to their reach
        // rather than ours; the userspace pointer rig's hands carry the Grabber that takes it.
        _grab = Slot.GetOrAttachComponent<Grabbable>();
        _grab.AllowGrab.Value = true;
        _grab.Scalable.Value = true;
        _grab.FollowRotation.Value = true;
        _grab.Receivable.Value = false;
        _grab.KeepMomentum.Value = false;
        _grab.OnLocalGrabbed += OnGrabbed;
        _grab.OnLocalReleased += OnReleased;

        var background = Slot.GetOrAttachComponent<BorderedImage>();
        background.Tint.Value = InspectorUI.PaneColor;
        background.BorderTint.Value = PanelBorder;
        ApplyRounded(background);

        // The theme parks its font and rounded-sprite providers on child slots of this one, so the
        // rows go in their own container - a layout on the root would try to lay those out as keys.
        var content = Slot.FindChildOrAdd("Content");
        var contentRect = content.GetOrAttachComponent<RectTransform>();
        contentRect.AnchorMin.Value = float2.Zero;
        contentRect.AnchorMax.Value = float2.One;
        contentRect.OffsetMin.Value = new float2(Padding, Padding);
        // Stops short of the right column, so the main grid's rows are the exact width they always
        // were and every row below keeps its own key sizes.
        contentRect.OffsetMax.Value = new float2(-(Padding + SideColumnWidth + SideColumnGap), -Padding);

        var column = content.AttachComponent<VerticalLayout>();
        column.Spacing.Value = RowSpacing;
        column.ForceExpandWidth.Value = true;
        column.ForceExpandHeight.Value = false;
        // The main grid is shorter than the right column (six rows against seven), so centre it rather
        // than leaving all the slack in one lump under the modifier row.
        column.MainAlignment.Value = MainAxisAlignment.Center;

        BuildPreviewStrip(content);
        for (int i = 0; i < GlyphRows.Length; i++)
            BuildGlyphRow(content, i);
        BuildModifierRow(content);

        BuildSideColumn();
    }

    // The right-hand block: numeric pad anchored to the top, arrow cluster to the bottom. Two
    // independently anchored containers rather than one long layout, so the gap between the two blocks
    // is deliberate instead of whatever the leftover space happened to be. -xlinka
    private void BuildSideColumn()
    {
        var side = Slot.FindChildOrAdd("SideColumn");
        var sideRect = side.GetOrAttachComponent<RectTransform>();
        sideRect.AnchorMin.Value = new float2(1f, 0f);
        sideRect.AnchorMax.Value = new float2(1f, 1f);
        sideRect.OffsetMin.Value = new float2(-(Padding + SideColumnWidth), Padding);
        sideRect.OffsetMax.Value = new float2(-Padding, -Padding);

        var numpad = side.AddSlot("Numpad");
        var numpadRect = numpad.AttachComponent<RectTransform>();
        numpadRect.AnchorMin.Value = new float2(0f, 1f);
        numpadRect.AnchorMax.Value = new float2(1f, 1f);
        numpadRect.OffsetMin.Value = new float2(0f, -NumpadHeight);
        numpadRect.OffsetMax.Value = float2.Zero;
        var numpadColumn = numpad.AttachComponent<VerticalLayout>();
        numpadColumn.Spacing.Value = RowSpacing;
        numpadColumn.ForceExpandWidth.Value = true;
        numpadColumn.ForceExpandHeight.Value = false;

        for (int i = 0; i < NumpadRows.Length; i++)
        {
            var row = BuildRow(numpad, "Numpad" + (i + 1), KeyHeight);
            AddKeyLayout(row);
            string glyphs = NumpadRows[i];
            for (int c = 0; c < glyphs.Length; c++)
            {
                string glyph = glyphs[c].ToString();
                // Same glyph on both faces: the pad types literally whatever shift is doing.
                BuildKey(row, "Num" + glyph, glyph, glyph, VrKeyRole.Character, KeyWidth, KeyFill);
            }
        }

        var arrows = side.AddSlot("Arrows");
        var arrowsRect = arrows.AttachComponent<RectTransform>();
        arrowsRect.AnchorMin.Value = new float2(0f, 0f);
        arrowsRect.AnchorMax.Value = new float2(1f, 0f);
        arrowsRect.OffsetMin.Value = float2.Zero;
        arrowsRect.OffsetMax.Value = new float2(0f, ArrowsHeight);
        var arrowColumn = arrows.AttachComponent<VerticalLayout>();
        arrowColumn.Spacing.Value = RowSpacing;
        arrowColumn.ForceExpandWidth.Value = true;
        arrowColumn.ForceExpandHeight.Value = false;

        // Inverted T: up on its own over left/down/right, which is the shape every hand already knows
        // where to reach on.
        var top = BuildRow(arrows, "ArrowsTop", KeyHeight);
        AddKeyLayout(top);
        BuildKey(top, "ArrowUp", "↑", string.Empty, VrKeyRole.ArrowUp, KeyWidth, KeyFill);

        var bottom = BuildRow(arrows, "ArrowsBottom", KeyHeight);
        AddKeyLayout(bottom);
        BuildKey(bottom, "ArrowLeft", "←", string.Empty, VrKeyRole.ArrowLeft, KeyWidth, KeyFill);
        BuildKey(bottom, "ArrowDown", "↓", string.Empty, VrKeyRole.ArrowDown, KeyWidth, KeyFill);
        BuildKey(bottom, "ArrowRight", "→", string.Empty, VrKeyRole.ArrowRight, KeyWidth, KeyFill);
    }

    private void BuildPreviewStrip(Slot content)
    {
        var strip = BuildRow(content, "Preview", PreviewHeight);

        var backing = strip.AttachComponent<BorderedImage>();
        backing.Tint.Value = InspectorUI.RowColor;
        backing.BorderTint.Value = KeyBorder;
        ApplyRounded(backing);

        var labelSlot = strip.AddSlot("Text");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = float2.Zero;
        labelRect.AnchorMax.Value = float2.One;
        labelRect.OffsetMin.Value = new float2(LabelInset, 0f);
        labelRect.OffsetMax.Value = new float2(-LabelInset, 0f);

        var text = labelSlot.AttachComponent<Text>();
        text.Content.Value = string.Empty;
        text.Size.Value = PreviewSize;
        text.Color.Value = InspectorUI.TextColor;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ApplyFont(text);
        _preview.Target = text;
    }

    private void BuildGlyphRow(Slot content, int index)
    {
        var row = BuildRow(content, "Row" + (index + 1), KeyHeight);
        AddKeyLayout(row);

        string glyphs = GlyphRows[index];
        string shifted = ShiftGlyphRows[index];
        for (int i = 0; i < glyphs.Length; i++)
        {
            string glyph = glyphs[i].ToString();
            string shiftGlyph = i < shifted.Length ? shifted[i].ToString() : glyph;
            BuildKey(row, glyph, glyph, shiftGlyph, VrKeyRole.Character, KeyWidth, KeyFill);
        }
    }

    private void BuildModifierRow(Slot content)
    {
        var row = BuildRow(content, "Modifiers", KeyHeight);
        AddKeyLayout(row);

        _shiftKey.Target = BuildKey(row, "Shift", "Shift", string.Empty, VrKeyRole.Shift, ShiftKeyWidth, KeyFill);
        BuildKey(row, "Tab", "Tab", string.Empty, VrKeyRole.Tab, TabKeyWidth, KeyFill);
        BuildKey(row, "Copy", "Copy", string.Empty, VrKeyRole.Copy, CopyKeyWidth, KeyFill);
        BuildKey(row, "Space", "Space", string.Empty, VrKeyRole.Space, SpaceKeyWidth, KeyFill);
        _pasteKey.Target = BuildKey(row, "Paste", "Paste", string.Empty, VrKeyRole.Paste, PasteKeyWidth, KeyFill);
        BuildKey(row, "Backspace", "Back", string.Empty, VrKeyRole.Backspace, BackspaceKeyWidth, KeyFill);
        BuildKey(row, "Enter", "Enter", string.Empty, VrKeyRole.Enter, EnterKeyWidth, InspectorUI.AccentColor);
        BuildKey(row, "Escape", "Esc", string.Empty, VrKeyRole.Escape, EscapeKeyWidth, KeyFill);
        BuildKey(row, "Close", "Close", string.Empty, VrKeyRole.Close, CloseKeyWidth, InspectorUI.DangerColor);
    }

    private static Slot BuildRow(Slot content, string name, float height)
    {
        var row = content.AddSlot(name);
        row.AttachComponent<RectTransform>();
        var element = row.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
        element.FlexibleWidth.Value = 1f;
        // Own chunk per row: a shift flip rewrites four rows of labels, and the preview strip changes
        // on every keystroke. Chunked, each of those re-meshes one strip instead of the whole panel.
        row.AttachComponent<GraphicChunkRoot>();
        return row;
    }

    // Rows are narrower than the panel by design (twelve keys, then ten), so they centre rather than
    // leaving a ragged gutter down one side.
    private static void AddKeyLayout(Slot row)
    {
        var layout = row.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = KeyGap;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = true;
        layout.MainAlignment.Value = MainAxisAlignment.Center;
    }

    private VrKey BuildKey(Slot row, string name, string glyph, string shiftGlyph, VrKeyRole role,
        float width, color fill)
    {
        var slot = row.AddSlot(name);
        slot.AttachComponent<RectTransform>();
        var element = slot.AttachComponent<LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.MinHeight.Value = KeyHeight;
        element.PreferredHeight.Value = KeyHeight;
        element.FlexibleHeight.Value = 0f;

        var backing = slot.AttachComponent<BorderedImage>();
        backing.Tint.Value = fill;
        backing.BorderTint.Value = KeyBorder;
        ApplyRounded(backing);

        var button = slot.AttachComponent<Button>();
        // Button.OnAttach only adopts a plain Image; the rounded keys use BorderedImage, so the
        // hover/press driver has to be pointed at the tint by hand or the keys never light up.
        button.SetupBackgroundColor(backing.Tint);

        var key = slot.AttachComponent<VrKey>();
        key.Glyph.Value = glyph;
        key.ShiftGlyph.Value = shiftGlyph;
        key.Role.Value = role;
        key.Keyboard.Target = this;
        button.SetAction(key.OnPressed);

        key.Label.Target = BuildKeyFace(slot, "Label", glyph, LabelSize, InspectorUI.TextColor,
            TextHorizontalAlignment.Center, TextVerticalAlignment.Middle, float2.Zero, float2.One,
            float2.Zero, float2.Zero);

        // Two genuinely different glyphs (a symbol key, not a letter) get a second small face in the
        // corner showing the one shift is NOT on, so you can read what a key does both ways without
        // pressing shift to find out. Letters skip it - a big 'a' with a little 'A' beside it is
        // noise. -xlinka
        if (key.HasDualFace)
        {
            key.AltLabel.Target = BuildKeyFace(slot, "AltLabel", key.ActiveGlyph(true), LabelSize * AltLabelScale,
                InspectorUI.MutedColor, TextHorizontalAlignment.Right, TextVerticalAlignment.Top,
                new float2(0.45f, 0.45f), float2.One, float2.Zero, new float2(-4f, -2f));
        }

        _keys.Add(key);
        return key;
    }

    private Text BuildKeyFace(Slot keySlot, string name, string content, float size, color tint,
        TextHorizontalAlignment horizontal, TextVerticalAlignment vertical,
        float2 anchorMin, float2 anchorMax, float2 offsetMin, float2 offsetMax)
    {
        var faceSlot = keySlot.AddSlot(name);
        var faceRect = faceSlot.AttachComponent<RectTransform>();
        faceRect.AnchorMin.Value = anchorMin;
        faceRect.AnchorMax.Value = anchorMax;
        faceRect.OffsetMin.Value = offsetMin;
        faceRect.OffsetMax.Value = offsetMax;

        var text = faceSlot.AttachComponent<Text>();
        text.Content.Value = content;
        text.Size.Value = size;
        text.Color.Value = tint;
        text.HorizontalAlignment.Value = horizontal;
        text.VerticalAlignment.Value = vertical;
        ApplyFont(text);
        return text;
    }

    private void ApplyRounded(BorderedImage image)
    {
        var sprite = _theme?.RoundedSprite;
        if (sprite == null)
            return;
        image.Texture.Target = sprite;
        image.NineSlice.Value = true;
        image.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
    }

    private void ApplyFont(Text text)
    {
        var font = _theme?.ThemeFont;
        if (font != null)
            text.Font.Target = font;
    }
}
