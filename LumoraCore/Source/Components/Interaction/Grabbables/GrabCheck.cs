// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.Interaction;

// user is null when nobody holds it.
public delegate void GrabStateEvent(GrabCheck check, Grabbable carrier, User? user);

// Publishes the grip state of the object it sits on as readable fields, so a build can drive
// something off "while held" without any code: point a value driver at IsGrabbed and it follows the
// hand. The fields read correctly on every peer, not just the one holding the object.
//
// These three are DERIVED, not replicated, and that is deliberate. Who holds a grabbable already
// travels the wire as the grabbable's own holder reference, which the host arbitrates; every peer
// therefore has the same input and computes the same answer with no traffic, no write race between
// peers each claiming to be right, and no round trip before the holder's own machine sees its grip
// take effect. Sending them again would be paying for an answer we are already given.
//
// That is why the writes go in silently and through the permission bypass. Silent because a delta
// for a value every peer already derived is pure noise on the wire, and bypassed because the write
// happens on all peers including ones with no right to edit this object - which is fine precisely
// because nothing leaves the machine. Non-persistent for the same reason: a saved world reloads
// with nothing in anyone's hand, and a stored true would light up every "while held" effect in the
// scene on load. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[SingleInstancePerSlot]
public class GrabCheck : GrabEventBehaviour, ICustomInspectorUI
{
    [NonPersistent]
    public readonly Sync<bool> IsGrabbed;

    // True while the holder is the user sitting at THIS machine.
    [NonPersistent]
    public readonly Sync<bool> GrabbedByLocalUser;

    [NonPersistent]
    public readonly SyncRef<User> GrabbingUser;

    // Runs on the holder's peer when the grip closes.
    public readonly SyncDelegate<GrabStateEvent> GrabbedAction;

    // Runs on the holder's peer when the grip opens, or when the object is taken.
    public readonly SyncDelegate<GrabStateEvent> ReleasedAction;

    // Local handler for the grab edge, for code that is not a world element.
    public event GrabStateEvent? Grabbed;

    public event GrabStateEvent? Released;

    public GrabCheck()
    {
        IsGrabbed = new Sync<bool>(this, false);
        GrabbedByLocalUser = new Sync<bool>(this, false);
        GrabbingUser = new SyncRef<User>(this);
        GrabbedAction = new SyncDelegate<GrabStateEvent>(this);
        ReleasedAction = new SyncDelegate<GrabStateEvent>(this);
    }

    // Resolved from the carrier's replicated holder ref.
    public Grabber? Holder => Carrier?.Grabber;

    // What was last written, rather than what the reference currently reads back as. A silent write
    // hands the reference an id and lets it resolve the element the way a remote update would, so
    // reading Target back as the "did it change" test can be a beat behind and spin the write every
    // frame until it catches up.
    private User? _mirrored;

    protected override void OnBehaviourUpdate(float delta)
    {
        var holder = Holder;
        var user = holder?.OwningUser;
        bool grabbed = holder != null;
        bool local = user != null && ReferenceEquals(user, World?.LocalUser);

        if (grabbed == IsGrabbed.Value && local == GrabbedByLocalUser.Value
            && ReferenceEquals(user, _mirrored))
            return;

        _mirrored = user;
        using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
        IsGrabbed.SetValueSilently(grabbed);
        GrabbedByLocalUser.SetValueSilently(local);
        GrabbingUser.SetValueSilently(user?.ReferenceID ?? RefID.Null);
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        // A disabled check stops updating, so whatever it was reading when it was switched off would
        // stay on the fields forever and keep every "while held" effect lit.
        _mirrored = null;
        using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
        IsGrabbed.SetValueSilently(false);
        GrabbedByLocalUser.SetValueSilently(false);
        GrabbingUser.SetValueSilently(RefID.Null);
    }

    protected override void OnGrabbed(Grabbable carrier)
    {
        var user = carrier.Grabber?.OwningUser;
        Grabbed?.Invoke(this, carrier, user);
        GrabbedAction.Target?.Invoke(this, carrier, user);
    }

    protected override void OnReleased(Grabbable carrier)
    {
        var user = carrier.Grabber?.OwningUser;
        Released?.Invoke(this, carrier, user);
        ReleasedAction.Target?.Invoke(this, carrier, user);
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Carrier", Carrier?.Slot?.SlotName.Value ?? "none");
        InspectorStats.AddRow(ui, "Holder", Holder?.Slot?.SlotName.Value ?? "nobody");
        InspectorStats.AddRow(ui, "User", GrabbingUser.Target?.UserName.Value ?? "nobody");
        InspectorStats.AddRow(ui, "Local hand", GrabbedByLocalUser.Value ? "yes" : "no");
    }
}
