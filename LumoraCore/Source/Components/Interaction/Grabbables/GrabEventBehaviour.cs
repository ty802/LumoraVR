// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Base for components that do something when the object they sit on is picked up or let go. Finds
// the Grabbable that governs this slot and hands its grip edges to OnGrabbed and OnReleased.
//
// Both edges run ONLY on the peer whose hand did it, because that is where the grabbable raises
// them. Anything written from a handler replicates from there like any other datamodel edit, and
// is refused the same way when that user lacks permission.
//
// The binding is left alone for as long as the object is in a hand, and that matters more than it
// looks: mid-carry the object hangs under the grabber's holder slot, so a re-walk would find
// whatever grabbable the hand rig itself carries instead of the one we started with, and the
// release would be reported by the wrong object. The held check reads the REPLICATED holder ref, so
// remote peers hold their binding for exactly as long as the carrying peer does.
//
// A failed walk is rate limited rather than retried every frame. A behaviour on something nobody
// can pick up never finds a grabbable, and there is no event to tell it when one is finally
// attached, so the choice is between polling slowly and never binding at all. -xlinka
public abstract class GrabEventBehaviour : Component
{
    private const float RebindInterval = 0.5f;

    private Grabbable? _carrier;
    private bool _hooked;
    private bool _heldLocally;
    private float _rebindTimer;

    public Grabbable? Carrier => _carrier;

    // True between the grab and release edges on THIS machine.
    public bool IsHeldLocally => _heldLocally;

    // Read from the replicated holder ref.
    public bool IsCarried => _carrier != null && !_carrier.IsDestroyed && _carrier.IsGrabbed;

    public override void OnStart()
    {
        base.OnStart();
        BindCarrier(0f);
    }

    public sealed override void OnUpdate(float delta)
    {
        BindCarrier(delta);
        OnBehaviourUpdate(delta);
    }

    public override void OnDestroy()
    {
        UnhookCarrier();
        base.OnDestroy();
    }

    // Runs after the carrier binding is refreshed.
    protected virtual void OnBehaviourUpdate(float delta) { }

    protected virtual void OnGrabbed(Grabbable carrier) { }

    // The local user's hand let the object go, or had it taken.
    protected virtual void OnReleased(Grabbable carrier) { }

    private void BindCarrier(float delta)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;

        if (_carrier != null && !_carrier.IsDestroyed)
        {
            if (_carrier.IsGrabbed || _heldLocally)
                return;
            var carrierSlot = _carrier.Slot;
            if (carrierSlot != null && !carrierSlot.IsDestroyed
                && (ReferenceEquals(carrierSlot, slot) || slot.IsDescendantOf(carrierSlot)))
                return;
        }

        _rebindTimer -= delta;
        if (_carrier == null && _rebindTimer > 0f)
            return;
        _rebindTimer = RebindInterval;

        UnhookCarrier();
        var found = slot.GetComponentInParents<Grabbable>();
        if (found == null || found.IsDestroyed)
            return;

        _carrier = found;
        found.OnLocalGrabbed += HandleGrabbed;
        found.OnLocalReleased += HandleReleased;
        _hooked = true;
    }

    private void UnhookCarrier()
    {
        if (_hooked && _carrier != null)
        {
            _carrier.OnLocalGrabbed -= HandleGrabbed;
            _carrier.OnLocalReleased -= HandleReleased;
        }
        _hooked = false;
        _heldLocally = false;
        _carrier = null;
    }

    private void HandleGrabbed(IGrabbable grabbable)
    {
        if (IsDestroyed || !Enabled.Value || grabbable is not Grabbable carrier)
            return;
        _heldLocally = true;
        Run(carrier, released: false);
    }

    private void HandleReleased(IGrabbable grabbable)
    {
        if (grabbable is not Grabbable carrier)
            return;
        // The flag is cleared even when this component is off, or a behaviour disabled mid-carry
        // would stay stuck as "held here" and never rebind.
        bool wasHeld = _heldLocally;
        _heldLocally = false;
        if (IsDestroyed || !Enabled.Value || !wasHeld)
            return;
        Run(carrier, released: true);
    }

    // A throwing handler must not take the rest of the release with it: the grabbable raises this
    // edge to every listener in turn, and the next one along has no idea it was skipped.
    private void Run(Grabbable carrier, bool released)
    {
        try
        {
            if (released)
                OnReleased(carrier);
            else
                OnGrabbed(carrier);
        }
        catch (System.Exception ex)
        {
            Logging.Logger.Error(
                $"{GetType().Name} on {ParentHierarchyToString()} threw on {(released ? "release" : "grab")}: {ex}");
        }
    }
}
