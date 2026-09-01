// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Touch;

namespace Lumora.Core.Components.Utility;

// Shared plumbing for the components that do something when a physical control is worked.
//
// Same shape as ButtonAction, one layer down: a touch control carries its actions as SyncDelegates
// naming a world element and a method, which is what makes an action survive duplication and a save,
// and it is also why several of these cannot each hold the same delegate. One claims it and fans the
// touch out to the rest: the holder runs every enabled responder sitting on its own slot or on the
// control's slot that wants the SAME response. Put the responders on either of those two slots and
// they all fire.
//
// Response is what makes the four controls interchangeable here. A plunger calls it Pressed, a flip
// calls it Toggled, a pad calls it Touched; TouchControl.ResponseSlot translates, so a responder can
// be moved from one control to another without being rewritten. A control with nothing for the asked
// response hands back null and the responder simply sits idle rather than grabbing the wrong event.
// -xlinka
//
// The claim is re-checked each frame instead of only at start. The holder can be destroyed, the
// control can be retargeted, and the delegate can be rebound by something else - none of those raise
// a change event on the components waiting to take over, so a one-shot bind leaves a control that has
// silently stopped doing anything. The check is a couple of reference compares.
//
// The response runs on the AUTHORITY, whoever did the touching. A touch on a non-authority peer travels
// as a request (TouchRelay) and the authority runs the control's reaction itself, so every write a
// response makes is authored by the host and the ownership gate accepts it. It used to run on the
// toucher's peer, which meant a guest's response wrote host-owned fields as the guest and was refused -
// on the guest's own client first, and again on the host. -xlinka
public abstract class TouchResponder : Component
{
    // Defaults to a control on this slot.
    public readonly SyncRef<TouchControl> SourceControl;

    // Which of the control's actions to hook.
    public readonly Sync<TouchResponse> Response;

    protected TouchResponder()
    {
        SourceControl = new SyncRef<TouchControl>(this);
        Response = new Sync<TouchResponse>(this, TouchResponse.Pressed);
    }

    public override void OnAttach()
    {
        base.OnAttach();
        if (SourceControl.Target == null)
            SourceControl.Target = Slot.GetComponent<TouchControl>();
    }

    public override void OnUpdate(float delta)
    {
        // Only the authority claims. The delegate is a replicated field, so the host's write reaches
        // every peer and they all resolve to the same holder. Letting clients claim too would put every
        // peer in a permission-denied write loop over a field one of them already owns.
        if (World?.IsAuthority != true)
            return;

        var control = SourceControl.Target;
        if (control == null || control.IsDestroyed)
            return;

        var action = control.ResponseSlot(Response.Value);
        if (action == null)
            return;

        var holder = ((ISyncRef)action).RawTarget as TouchResponder;
        if (holder != null && !holder.IsDestroyed
            && holder.SourceControl.Target == control
            && holder.Response.Value == Response.Value)
        {
            return;
        }

        // Nothing usable is holding the delegate. Take it, unless something that is not a responder has
        // it - that is a deliberately wired action and stealing it would break whatever set it.
        if (action.Target != null && holder == null)
            return;

        action.Target = OnTouch;
    }

    // Bound as the control's action by whichever responder claims it. A responder only ever claims the
    // one delegate its own Response names, so this firing means that response fired.
    [SyncMethod]
    public void OnTouch(TouchControl source, TouchContact contact)
    {
        var actor = contact.User;
        Dispatch(Slot, source, in contact, actor);
        var controlSlot = source?.Slot;
        // A component belongs to exactly one slot, so two distinct slots cannot yield the same responder
        // twice and nothing has to be de-duplicated.
        if (controlSlot != null && controlSlot != Slot)
            Dispatch(controlSlot, source!, in contact, actor);
    }

    private void Dispatch(Slot? slot, TouchControl? control, in TouchContact contact, User? actor)
    {
        if (slot == null || slot.IsDestroyed || control == null)
            return;

        var response = Response.Value;
        byte controlByte = control.ReferenceID.GetUserByte();
        foreach (var responder in slot.GetComponents<TouchResponder>())
        {
            if (responder.IsDestroyed || !responder.Enabled.Value
                || responder.SourceControl.Target != control
                || responder.Response.Value != response)
            {
                continue;
            }

            // The authority runs the control's reaction as the CONTROL's author (TouchControl.RunTouch).
            // A responder someone else bolted onto that control is not part of the rig its author wired,
            // and running it inside that scope would hand its owner the control author's authority -
            // which in a world that allows building is a real escalation, not a hypothetical one. Same
            // author or it does not run. -xlinka
            if (responder.ReferenceID.GetUserByte() != controlByte)
                continue;

            responder.Fire(contact, actor);
        }
    }

    // Runs on the authority, scoped to the control's author. The actor is the user who touched, for
    // responses that care who worked the control - it is NOT what the writes below are gated by.
    protected abstract void Fire(TouchContact contact, User? actor);
}
