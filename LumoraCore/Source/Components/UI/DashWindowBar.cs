// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.Touch;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// A window's title bar: press and drag moves the window (laser or desktop mouse alike, both arrive as a
// canvas pointer), and a grab on it in VR lifts the window off the dash as a panel in the hand. -xlinka
//
// A press raises the window and does nothing else until the pointer has plainly left the spot it went down on,
// so a click on the bar is never a drop: it fills, tiles and frees nothing. In a headset the press also has to
// come from a beam that had settled on the bar. One squeezed while the beam was still sweeping in toward a
// button moves nothing, however far the sweep then carries it. -xlinka
[ComponentCategory("Hidden")]
public sealed class DashWindowBar : InteractionElement, IUIGrabbable
{
    // Angles at the beam's distance, sized the way the canvas sizes its hover hold so they hold at any reach:
    // how far a headset beam goes from the press before the window follows (past the jolt of the squeeze
    // itself), how far it may drift and still count as resting, and how long it has to rest on the bar.
    private const float DragAngle = 1.5f * MathF.PI / 180f;
    private const float RestAngle = 0.75f * MathF.PI / 180f;
    private const double RestSeconds = 0.1;

    internal DashWindow? Window;

    private float2 _pressPoint;
    private float2 _lastPoint;
    private float2 _startPosition;
    private bool _pressed;
    private bool _moving;
    private UIInteractionSource _pressSource;
    private int _pressPointer;
    // Per hand: where its beam came to rest on the bar, since when, and since when the rest before that one
    // began. The squeeze that presses can jolt the beam on the press frame itself; that jolt is not a sweep.
    private readonly float2[] _restPoint = new float2[2];
    private readonly double[] _restSince = new double[2];
    private readonly double[] _restBefore = new double[2];

    protected override void OnHoverEnter(in UIInteractionContext context) => TrackRest(in context, entered: true);

    protected override void OnHoverMove(in UIInteractionContext context) => TrackRest(in context, entered: false);

    protected override void OnPress(in UIInteractionContext context)
    {
        _pressed = false;
        _moving = false;
        var window = Window;
        if (window == null || window.IsDestroyed)
            return;
        _pressed = !SweptIn(in context);
        _pressSource = context.Source;
        _pressPointer = context.PointerId;
        _pressPoint = context.LocalPoint;
        _lastPoint = context.LocalPoint;
        _startPosition = window.Position;
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        var window = Window;
        if (!_pressed || context.Source != _pressSource || context.PointerId != _pressPointer
            || window == null || window.IsDestroyed || window.Workspace == null || window.Dashboard == null)
            return;
        _lastPoint = context.LocalPoint;
        if (!_moving)
        {
            if (!Travelled(in context, in _pressPoint))
                return;
            _moving = true;
        }
        var delta = context.LocalPoint - _pressPoint;
        window.Workspace.DragTo(window, new float2(_startPosition.x + delta.x, _startPosition.y - delta.y),
            window.Dashboard.TopDown(context.LocalPoint));
    }

    protected override void OnRelease(in UIInteractionContext context)
    {
        // Another hand letting go of its own press on this bar is not this drag ending. A canvas letting go of
        // every pointer at once names no source, and ends it. So does a finger of the pressing hand taking the
        // press over: it lets go with its own pointer, somewhere the beam never was, so the drag ends where the
        // beam was last seen.
        bool own = context.Source == _pressSource && context.PointerId == _pressPointer;
        bool takenOver = context.Source == UIInteractionSource.Touch && HandOf(_pressSource) >= 0
            && HandOf(_pressSource) == HandOf(CanvasTouchSource.SideOf(context.PointerId));
        if (_pressed && context.Source != UIInteractionSource.Unknown && !own && !takenOver)
            return;
        var window = Window;
        bool moving = _pressed && _moving;
        _pressed = false;
        _moving = false;
        if (!moving || window == null || window.IsDestroyed || window.Workspace == null || window.Dashboard == null)
            return;
        var end = own ? EndPoint(in context, in _lastPoint) : _lastPoint;
        window.Workspace.EndDrag(window, window.Dashboard.TopDown(end));
    }

    public IGrabbable? TryGrab(in UIInteractionContext context)
    {
        var window = Window;
        if (window == null || window.IsDestroyed || window.Workspace == null)
            return null;
        return window.Workspace.GrabOut(window);
    }

    // Whether a press on the window chrome has gone far enough to be a drag. A mouse and a fingertip are where
    // they say they are; a headset beam jumps with the squeeze that presses it.
    internal static bool Travelled(in UIInteractionContext context, in float2 pressPoint)
    {
        float slop = DragPassThreshold;
        if (FromHeadset(in context))
            slop = MathF.Max(slop, Margin(in context, DragAngle));
        return (context.LocalPoint - pressPoint).LengthSquared > slop * slop;
    }

    // Where a drag on the chrome ends. A press cut short (the beam off the dash, the dash put away) can be let go
    // with no ray behind it and a point at the middle of the canvas; it ends where the pointer was last seen.
    internal static float2 EndPoint(in UIInteractionContext context, in float2 lastSeen)
        => context.RayDirection == float3.Zero ? lastSeen : context.LocalPoint;

    private void TrackRest(in UIInteractionContext context, bool entered)
    {
        int hand = HandOf(context.Source);
        if (hand < 0)
            return;
        float reach = Margin(in context, RestAngle);
        if (entered || (context.LocalPoint - _restPoint[hand]).LengthSquared > reach * reach)
        {
            double now = World?.Time.TotalTime ?? 0.0;
            _restBefore[hand] = entered ? now : _restSince[hand];
            _restPoint[hand] = context.LocalPoint;
            _restSince[hand] = now;
        }
    }

    private bool SweptIn(in UIInteractionContext context)
    {
        int hand = HandOf(context.Source);
        if (hand < 0 || !FromHeadset(in context))
            return false;
        double now = World?.Time.TotalTime ?? 0.0;
        double since = _restSince[hand] >= now ? _restBefore[hand] : _restSince[hand];
        return now - since < RestSeconds;
    }

    // The desktop mouse reaches the dash as a laser too; only a worn headset gets the angular rules.
    private static bool FromHeadset(in UIInteractionContext context)
    {
        if (context.Source is not (UIInteractionSource.VRLeft or UIInteractionSource.VRRight))
            return false;
        var input = Engine.Current?.InputInterface;
        return input == null || input.IsVRActive;
    }

    // An angle at the beam's distance, in this canvas's units.
    private static float Margin(in UIInteractionContext context, float angle)
    {
        float scale = MathF.Abs(context.Canvas?.Slot?.GlobalScale.x ?? 0f);
        return scale > 0.000001f ? context.Distance * angle / scale : 0f;
    }

    private static int HandOf(UIInteractionSource source) => source switch
    {
        UIInteractionSource.VRLeft => 0,
        UIInteractionSource.VRRight => 1,
        _ => -1,
    };

    private static int HandOf(Chirality side) => side switch
    {
        Chirality.Left => 0,
        Chirality.Right => 1,
        _ => -1,
    };
}
