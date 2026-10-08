// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Diagnostics.CodeAnalysis;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.Touch;
using Lumora.Core.Components.UI;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// VR self-scale: both grips squeezed on empty air at about the same moment, then the hands pulled apart to
// shrink or pushed together to grow, as if the world were being stretched between them. Distances are taken
// in the user root's own space, so the hands' real spread drives it and the scale it produces never feeds
// back into it. The feet stay where they stand, and letting go records one undo step. The ring's Scaling
// toggle gates it and the desktop binding both. -xlinka
[ComponentCategory("Users")]
public class SelfScaleGesture : Component
{
    // How close together the two grips have to land to read as one gesture rather than two grabs.
    private const double GripWindow = 0.35;
    // Hands closer than this at the start give a ratio that jumps with every millimetre.
    private const float MinStartSpread = 0.05f;
    private const float MinSpread = 0.01f;
    // Smaller changes than this are not written, so hands held still put nothing on the wire.
    private const float ScaleStep = 1e-4f;

    private UserRoot? _root;
    private HandTool? _left;
    private HandTool? _right;
    private ContextMenuSystem? _menu;

    private bool _leftHeld;
    private bool _rightHeld;
    private double _leftDown = double.NegativeInfinity;
    private double _rightDown = double.NegativeInfinity;
    private bool _pending;
    private bool _active;
    private float _startScale;
    private float _startSpread;
    private float _scale;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (_root == null || _root.IsDestroyed)
            _root = Slot.GetComponent<UserRoot>();
        var root = _root;
        var input = Engine.Current?.InputInterface;
        var actions = input?.Actions;
        if (root == null || !root.IsLocalUserRoot || World?.IsFocused != true || input == null || actions == null || !input.IsVRActive)
        {
            End();
            _pending = false;
            return;
        }

        bool left = actions.Interaction(Chirality.Left).Grab.Held;
        bool right = actions.Interaction(Chirality.Right).Grab.Held;
        double now = World!.Time.TotalTime;
        bool landed = left && right && (!_leftHeld || !_rightHeld);
        if (left && !_leftHeld)
            _leftDown = now;
        if (right && !_rightHeld)
            _rightDown = now;
        _leftHeld = left;
        _rightHeld = right;

        if (_active)
        {
            if (!left || !right || !Free(root, out var locomotion))
                End();
            else
                Step(root, locomotion);
            return;
        }

        // Started a frame after both grips land: a grip that grabbed something shows by then, and a grab
        // means no gesture.
        if (_pending)
        {
            _pending = false;
            if (left && right && Free(root, out _))
                Begin(root);
            return;
        }

        if (landed && System.Math.Abs(_leftDown - _rightDown) <= GripWindow)
        {
            ResolveParts(root);
            _pending = true;
        }
    }

    public override void OnDestroy()
    {
        End();
        base.OnDestroy();
    }

    private void ResolveParts(UserRoot root)
    {
        var rootSlot = root.Slot;
        if (rootSlot == null)
            return;
        if (_left == null || _left.IsDestroyed || _right == null || _right.IsDestroyed)
        {
            _left = _right = null;
            foreach (var hand in rootSlot.GetComponentsInChildren<HandTool>())
            {
                if (hand.Side.Value == Chirality.Left)
                    _left ??= hand;
                else if (hand.Side.Value == Chirality.Right)
                    _right ??= hand;
            }
        }
        if (_menu == null || _menu.IsDestroyed)
            _menu = rootSlot.GetComponentInChildren<ContextMenuSystem>();
    }

    // Both hands empty and the user's own, no ring up, and scaling allowed. A hand with a tool equipped
    // holds the tool.
    private bool Free(UserRoot root, [NotNullWhen(true)] out LocomotionController? locomotion)
    {
        locomotion = root.GetRegisteredComponent<LocomotionController>() ?? root.Slot?.GetComponent<LocomotionController>();
        if (locomotion == null || locomotion.IsDestroyed || !locomotion.ScalingEnabled.Value)
            return false;
        if (_left == null || _left.IsDestroyed || _right == null || _right.IsDestroyed)
            return false;
        if (_left.IsHoldingObjects || _left.IsScalingObjects || _right.IsHoldingObjects || _right.IsScalingObjects)
            return false;
        if (_left.ActiveToolItem.Target is { IsDestroyed: false } || _right.ActiveToolItem.Target is { IsDestroyed: false })
            return false;
        if (ContextMenuSystem.IsHandOwnedByUserspace(Chirality.Left) || ContextMenuSystem.IsHandOwnedByUserspace(Chirality.Right))
            return false;
        return _menu == null || _menu.IsDestroyed || !_menu.IsShown;
    }

    private float Spread(UserRoot root)
    {
        var a = root.Slot.GlobalPointToLocal(_left!.Slot.GlobalPosition);
        var b = root.Slot.GlobalPointToLocal(_right!.Slot.GlobalPosition);
        return (a - b).Length;
    }

    private void Begin(UserRoot root)
    {
        float spread = Spread(root);
        if (!float.IsFinite(spread) || spread < MinStartSpread)
            return;
        _startSpread = spread;
        _startScale = root.GlobalScale;
        _scale = _startScale;
        _active = true;
        Haptics.Pulse(Chirality.Left, TouchHaptics.Light);
        Haptics.Pulse(Chirality.Right, TouchHaptics.Light);
    }

    private void Step(UserRoot root, LocomotionController locomotion)
    {
        float spread = MathF.Max(Spread(root), MinSpread);
        float scale = locomotion.ClampUserScale(_startScale * (_startSpread / spread));
        if (!float.IsFinite(scale) || MathF.Abs(scale - _scale) <= _scale * ScaleStep)
            return;
        // The user's own body, the same as walking: the root's ownership link can lag behind a join.
        using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
        var feet = root.FeetPosition;
        root.GlobalScale = scale;
        root.ShiftBy(feet - root.FeetPosition);
        _scale = scale;
    }

    private void End()
    {
        if (!_active)
            return;
        _active = false;
        var root = _root;
        if (root == null || root.IsDestroyed || MathF.Abs(_scale - _startScale) <= _startScale * ScaleStep)
            return;
        var undo = root.Slot?.GetComponentInChildren<UndoManager>();
        undo?.Record(new UserScaleUndoBatch(root, _startScale, _scale));
    }
}
