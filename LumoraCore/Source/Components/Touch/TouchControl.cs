// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components.Touch;

// Signature every touch control's bound action uses. Stored as target plus method name so it
// survives duplication and a save/load round trip, the same way a Helio button's action does.
public delegate void TouchAction(TouchControl source, TouchContact contact);

// Shared spine of every physical control: who may work it, which probe kinds it answers, and how it
// looks to a pointer.
//
// Controls implement IInteractionTarget as well as ITouchTarget and sit at a higher priority than a
// bare grabbable. That is what makes a panel of buttons bolted to a pick-up-able object behave the
// way people expect: point at a button and you press the button, point at the frame around it and
// you pick the whole thing up. Without the priority the grabbable root swallows every press and the
// panel is decoration. -xlinka
public abstract class TouchControl : Component, ITouchTarget, IInteractionTarget
{
    public readonly Sync<bool> AcceptFingertipTouch;

    // Leave on or desktop users are locked out.
    public readonly Sync<bool> AcceptRemoteTouch;

    public readonly Sync<bool> AcceptOutOfSightTouch;

    // Only usable in a world that allows editing. For build-tool controls, not world content.
    public readonly Sync<bool> EditModeOnly;

    public readonly Sync<TouchUserFilter> UserFilter;

    // Priority against other pointer targets on the same object. Above a grabbable by default.
    public readonly Sync<int> InteractionPriority;

    // Pulse fired on the toucher's controller when hover starts.
    public readonly Sync<TouchHaptics> HoverHaptics;

    // Pulse fired on the toucher's controller when contact starts or ends.
    public readonly Sync<TouchHaptics> ContactHaptics;

    protected TouchControl()
    {
        AcceptFingertipTouch = new Sync<bool>(this, true);
        AcceptRemoteTouch = new Sync<bool>(this, true);
        AcceptOutOfSightTouch = new Sync<bool>(this, false);
        EditModeOnly = new Sync<bool>(this, false);
        UserFilter = new Sync<TouchUserFilter>(this, TouchUserFilter.Anyone);
        InteractionPriority = new Sync<int>(this, DefaultInteractionPriority);
        HoverHaptics = new Sync<TouchHaptics>(this, TouchHaptics.Light);
        ContactHaptics = new Sync<TouchHaptics>(this, TouchHaptics.Medium);
    }

    // Sits above Grabbable and RayTarget, which both default to zero.
    public const int DefaultInteractionPriority = 10;

    public bool AcceptsFingertip => AcceptFingertipTouch.Value;

    public bool AcceptsRemote => AcceptRemoteTouch.Value;

    public bool AcceptsOutOfSight => AcceptOutOfSightTouch.Value;

    public int InteractionTargetPriority => InteractionPriority.Value;

    // Text shown on the pointer while this control is aimed at. Slot name unless overridden.
    protected virtual string? PointerLabel => Slot?.SlotName.Value;

    public virtual bool CanTouch(TouchProbe probe)
    {
        if (probe == null || IsDestroyed || !Enabled.Value)
            return false;

        if (EditModeOnly.Value && World?.AllowsWorldEditing != true)
            return false;

        return UserFilter.Value.Allows(this, probe.Owner.Target);
    }

    public virtual InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        bool usable = AcceptRemoteTouch.Value
            && (!EditModeOnly.Value || World?.AllowsWorldEditing == true)
            && UserFilter.Value.Allows(this, World?.LocalUser);

        return new InteractionDescription
        {
            Name = PointerLabel,
            Cursor = usable ? LaserCursor.Default : LaserCursor.Disabled,
            ForceActivate = false,
        };
    }

    void ITouchTarget.OnTouch(in TouchContact contact) => OnTouchContact(in contact);

    // After the accept and filter gates have already passed.
    protected abstract void OnTouchContact(in TouchContact contact);

    protected void PulseHover(in TouchContact contact)
        => Haptics.Pulse(contact.User, contact.Hand, HoverHaptics.Value);

    protected void PulseContact(in TouchContact contact)
        => Haptics.Pulse(contact.User, contact.Hand, ContactHaptics.Value);

    // Bind an action the house way: a method on a world element becomes a duplicable reference, a
    // closure falls back to the local-only event. Mirrors how a Helio button takes its action so the
    // same handler can serve a screen button and a physical one.
    protected static void Bind(SyncDelegate<TouchAction> slot, ref TouchAction? localEvent, TouchAction? action)
    {
        if (action == null)
            return;
        if (action.Target is IWorldElement)
            slot.Target = action;
        else
            localEvent += action;
    }

    // Contains anything either of them throws.
    protected void Run(SyncDelegate<TouchAction> slot, TouchAction? localEvent, in TouchContact contact)
    {
        var boundContact = contact;
        try
        {
            slot.Target?.Invoke(this, boundContact);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Touch control {ParentHierarchyToString()} action threw: {ex}");
        }

        try
        {
            localEvent?.Invoke(this, boundContact);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Touch control {ParentHierarchyToString()} local handler threw: {ex}");
        }
    }
}
