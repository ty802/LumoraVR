// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Desktop;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The desktop picture as something to point at. In VR the laser is the mouse: hovering moves the real
// pointer, the trigger is the left button, the secondary button right-clicks and the stick scrolls.
//
// Outside VR the page only shows the desktop. The real mouse is already on it, and a laser moving that
// same mouse would fight the hand holding it.
//
// One laser drives at a time: the one that got there first, or the last one to press. Two hands over the
// same screen would otherwise tear the pointer back and forth every frame. -xlinka
[ComponentCategory("Hidden")]
[UserspaceOnly]
public sealed class DesktopPointerSurface : InteractionElement, IUIAxisActionReceiver, IUISecondaryActionReceiver
{
    // Quarter detents, so a gentle push still moves the pages that take fine wheel steps.
    private const int WheelChunk = IDesktopInput.WheelStep / 4;

    private DesktopDisplay? _display;
    private int? _owner;
    private bool _leftDown;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private float _wheelX;
    private float _wheelY;

    public DesktopDisplay? Display
    {
        get => _display;
        set
        {
            if (value == _display)
                return;
            ReleaseButtons();
            _display = value;
            _lastX = _lastY = int.MinValue;
        }
    }

    private static IDesktopInput? Input => DesktopPlatform.Backend?.Input;

    private bool Live => IsExecutionAllowed && _display != null && Input != null && Lumora.Core.Engine.Current?.InputInterface?.IsVRActive == true;

    private static int Key(in UIInteractionContext context) => ((int)context.Source << 24) ^ context.PointerId;

    protected override void OnHoverEnter(in UIInteractionContext context)
    {
        _owner ??= Key(context);
        if (_owner == Key(context))
            MoveTo(in context);
    }

    protected override void OnHoverMove(in UIInteractionContext context)
    {
        _owner ??= Key(context);
        if (_owner == Key(context))
            MoveTo(in context);
    }

    protected override void OnHoverExit(in UIInteractionContext context)
    {
        if (_owner != Key(context))
            return;
        _owner = null;
        ReleaseButtons();
    }

    protected override void OnPress(in UIInteractionContext context)
    {
        _owner = Key(context);
        if (!MoveTo(in context) || _leftDown)
            return;
        _leftDown = Input!.Button(DesktopButton.Left, true);
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        if (_owner == Key(context))
            MoveTo(in context);
    }

    protected override void OnRelease(in UIInteractionContext context)
    {
        if (_owner != Key(context))
            return;
        MoveTo(in context);
        ReleaseButtons();
    }

    public bool TriggerSecondary(in UIInteractionContext context)
    {
        if (!Live)
            return false;
        _owner = Key(context);
        MoveTo(in context);
        Input!.Button(DesktopButton.Right, true);
        Input!.Button(DesktopButton.Right, false);
        return true;
    }

    public bool ProcessAxis(in UIInteractionContext context, in float2 axis)
    {
        if (!Live)
            return false;
        _wheelY += axis.y * IDesktopInput.WheelStep;
        _wheelX += axis.x * IDesktopInput.WheelStep;
        Spend(ref _wheelY, horizontal: false);
        Spend(ref _wheelX, horizontal: true);
        return true;
    }

    public bool WantsAxis(in UIInteractionContext context) => Live;

    public override void OnDisabled()
    {
        ReleaseButtons();
        base.OnDisabled();
    }

    public override void OnDestroy()
    {
        ReleaseButtons();
        base.OnDestroy();
    }

    // A button left down on the desktop stays down after the headset comes off, so every way out of a
    // press lets go of it.
    public void ReleaseButtons()
    {
        if (_leftDown)
            Input?.Button(DesktopButton.Left, false);
        _leftDown = false;
    }

    // The desktop pixel under a pointer, in virtual desktop coordinates.
    public bool TryMap(in UIInteractionContext context, out int x, out int y)
    {
        x = y = 0;
        var display = _display;
        var rect = RectTransform?.LocalComputeRect;
        if (display == null || !rect.HasValue || rect.Value.width <= 0f || rect.Value.height <= 0f)
            return false;
        // Canvas y runs up, the desktop's runs down.
        var point = context.PointIn(Slot);
        float u = System.Math.Clamp((point.x - rect.Value.xMin) / rect.Value.width, 0f, 1f);
        float v = System.Math.Clamp((rect.Value.yMax - point.y) / rect.Value.height, 0f, 1f);
        x = display.Left + (int)MathF.Round(u * (display.Width - 1));
        y = display.Top + (int)MathF.Round(v * (display.Height - 1));
        return true;
    }

    private bool MoveTo(in UIInteractionContext context)
    {
        if (!Live || !TryMap(in context, out int x, out int y))
            return false;
        if (x == _lastX && y == _lastY)
            return true;
        _lastX = x;
        _lastY = y;
        return Input!.MoveTo(x, y);
    }

    private static void Spend(ref float owed, bool horizontal)
    {
        if (MathF.Abs(owed) < WheelChunk)
            return;
        int delta = (int)owed;
        owed -= delta;
        Input?.Wheel(delta, horizontal);
    }
}
