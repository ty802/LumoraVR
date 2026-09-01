// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components.Touch;

// Signature every touch control's bound action uses. Stored as target plus method name so it
// survives duplication and a save/load round trip, the same way a Helio button's action does.
public delegate void TouchAction(TouchControl source, TouchContact contact);

// Which of a control's bound actions something wants to hook.
//
// The four controls do not name their delegates the same way, and they should not: a plunger is
// pressed, a flip is thrown. This is the shared vocabulary a component can ask for without knowing
// which control it landed on, and ResponseSlot below is where each control says what its own names
// mean in these terms. A control that has nothing for a given response answers null rather than
// handing back some near-enough delegate. -xlinka
public enum TouchResponse
{
    // The control did its thing once: pressed, thrown, first frame of contact.
    Pressed,

    // Every frame the control is still being worked.
    Pressing,

    // The control came back up.
    Released,

    // A latching control went to the on state.
    TurnedOn,

    // A latching control went to the off state.
    TurnedOff,
}

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
        => probe != null && CanTouch(probe.Owner.Target, probe.Kind);

    // The whole gate, expressed without a probe. The authority has to ask this about touches relayed
    // from other peers, where the probe is a component on a machine it cannot see - and that is the
    // only check standing between a client's touch request and the world reacting, so it has to be the
    // same one the toucher's own client ran, not a looser copy. The probe overload above is the same
    // question asked with a probe in hand. -xlinka
    public virtual bool CanTouch(User? user, TouchProbeKind kind)
    {
        if (IsDestroyed || !Enabled.Value)
            return false;

        if (!(kind == TouchProbeKind.Fingertip ? AcceptFingertipTouch.Value : AcceptRemoteTouch.Value))
            return false;

        if (EditModeOnly.Value && World?.AllowsWorldEditing != true)
            return false;

        // The Touch domain. On the AUTHORITY this is the enforcing answer, on the toucher's own peer
        // it is politeness - a joiner's gate has never seen the host's role tables, so its answer is
        // only ever a guess and is never what a relayed touch is judged on. -xlinka
        if (World?.DataModelPermissions?.AllowsDomain(user, DataModelPermissionDomain.Touch) == false)
            return false;

        return UserFilter.Value.Allows(this, user);
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

    // The delegate this control uses for the asked-for response, or null if it has no such thing.
    // Lets one component claim an action off any control without a type switch per control.
    public virtual SyncDelegate<TouchAction>? ResponseSlot(TouchResponse response) => null;

    // Whether this control's reaction is the TOUCHER's own business rather than the world's, in which
    // case it stays on the toucher's peer instead of being relayed to the authority. The default is
    // false: a control's state and its authored actions belong to whoever owns the control, and on
    // world content that is the host. A seat is the exception - it moves the toucher's own rig, which
    // is something no other peer is allowed to do at all. -xlinka
    public virtual bool RunsOnToucher => false;

    void ITouchTarget.OnTouch(in TouchContact contact) => RunTouch(in contact);

    // Every reaction a control has - its own state, its bound actions, everything those actions write -
    // runs here, and on the authority it runs AS THE CONTROL'S AUTHOR.
    //
    // A control the world author built runs as the world. The Social/Event lock exists to stop USERS
    // editing the world, not to freeze the switches the author screwed to the wall: the lock is a
    // whole-world floor that no role escapes, so without this it currently locks the host out of its
    // own light switches too, and every authored button in a social world is decoration. A control
    // some USER built runs scoped to that user instead, so their own gadget can never do more than
    // they could do by hand.
    //
    // This is the point of the whole seam: after this, a guest's touch produces host-authored writes
    // that the gate accepts, instead of guest-authored writes that it refuses. -xlinka
    internal void RunTouch(in TouchContact contact)
    {
        var permissions = World?.IsAuthority == true ? World.DataModelPermissions : null;
        if (permissions == null)
        {
            OnTouchContact(in contact);
            return;
        }

        if (ReferenceID.IsAuthorityID)
        {
            // An open world needs no scope: the authority already holds every right one would grant, so
            // only a locked world pays for the object.
            if (!permissions.SocialLock)
            {
                OnTouchContact(in contact);
                return;
            }

            // The authored-content scope, NOT a system bypass. Both ends are pinned: this control has
            // to be authority-authored (it is - we are in the IsAuthorityID branch) and so does
            // whatever the reaction writes, so the frozen world's own wiring keeps working and the
            // scope still cannot reach a single thing a visitor owns. -xlinka
            using (permissions.EnterAuthoredContentScope(this))
                OnTouchContact(in contact);
            return;
        }

        // An author we cannot resolve (they left mid-touch) gets no scope rather than the world's -
        // fail toward less authority, not more.
        var author = TouchUserFilterExtensions.ResolveOwner(this);
        if (author == null)
        {
            OnTouchContact(in contact);
            return;
        }

        using (permissions.EnterActor(author))
            OnTouchContact(in contact);
    }

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
