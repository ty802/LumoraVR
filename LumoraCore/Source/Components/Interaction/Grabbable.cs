// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components;

// implements IGrabbable so any Grabber can pick this up. parent under the grabber's
// holder slot on grab; restore on release is left to the Grabber for now. - xlinka
[ComponentCategory("Interaction")]
public sealed class Grabbable : Component, IGrabbable
{
    public readonly Sync<bool> AllowGrab = new();
    public readonly Sync<bool> FollowRotation = new();
    public readonly Sync<bool> Scalable = new();
    public readonly Sync<bool> Receivable = new();
    public readonly Sync<bool> AllowOnlyPhysicalGrab = new();
    public readonly Sync<int> GrabPriority = new();
    public readonly Sync<int> InteractionPriority = new();

    private Grabber? _grabber;
    private Slot? _lastParent;

    public bool IsGrabbed => _grabber != null;
    public Grabber? Grabber => _grabber;

    bool IGrabbable.Scalable => Scalable.Value;
    bool IGrabbable.Receivable => Receivable.Value;
    bool IGrabbable.AllowOnlyPhysicalGrab => AllowOnlyPhysicalGrab.Value;
    int IGrabbable.GrabPriority => GrabPriority.Value;

    public int InteractionTargetPriority => InteractionPriority.Value;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        return new InteractionDescription
        {
            Name = Slot?.SlotName.Value,
            Cursor = AllowGrab.Value ? LaserCursor.Grab : LaserCursor.Disabled,
            ForceActivate = false,
        };
    }

    public event Action<IGrabbable>? OnLocalGrabbed;
    public event Action<IGrabbable>? OnLocalReleased;

    public override void OnInit()
    {
        base.OnInit();
        AllowGrab.Value = true;
        FollowRotation.Value = false;
        Scalable.Value = true;
        Receivable.Value = true;
        AllowOnlyPhysicalGrab.Value = false;
        GrabPriority.Value = 0;
        InteractionPriority.Value = 0;
    }

    public bool CanGrab(Grabber grabber)
    {
        return AllowGrab.Value && _grabber == null && !IsDestroyed;
    }

    public IGrabbable Grab(Grabber grabber, Slot holdSlot, bool suppressEvents = false)
    {
        if (!CanGrab(grabber)) return this;

        _grabber = grabber;
        _lastParent = Slot?.Parent;
        // preserveGlobalTransform: keep world position when grabbed, so the object
        // doesn't snap to the hand origin. - xlinka
        Slot?.SetParent(holdSlot, preserveGlobalTransform: true);

        if (!suppressEvents) OnLocalGrabbed?.Invoke(this);
        return this;
    }

    public void Release(Grabber grabber, bool suppressEvents = false)
    {
        if (_grabber != grabber) return;

        _grabber = null;

        var restoreParent = _lastParent;
        if (restoreParent == null || restoreParent.IsDestroyed || (Slot != null && restoreParent.IsDescendantOf(Slot)))
        {
            restoreParent = World?.RootSlot;
        }
        _lastParent = null;
        Slot?.SetParent(restoreParent!, preserveGlobalTransform: true);

        if (!suppressEvents) OnLocalReleased?.Invoke(this);
    }
}
