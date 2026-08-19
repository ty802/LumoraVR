// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Warden;

// The whole view Warden has of the engine's element ids. Warden decides WHAT ownership means; the
// engine supplies only the bit layout of its own ids, so no engine type crosses the boundary. -xlinka
public interface IPermissionIdSpace
{
    // An unset id. Never owned by anyone.
    bool IsNull(ulong id);

    // An id minted by the authority. Never owned by a user, which is what freezes world content.
    bool IsAuthority(ulong id);

    // The allocation byte an id was minted in.
    byte OwnerByte(ulong id);

    // Whether a byte names a real per-user allocation domain (not authority, local, or reserved).
    bool IsValidOwnerByte(byte ownerByte);

    // Whether a collection key names an element, and which. Used for the own-byte registry add: the
    // key is the new element's id, so it tells us who minted it.
    bool TryGetElementId(object? key, out ulong id);
}

// A user, as the gate sees one. Nothing here is trusted from a client: ids and allocation bytes are
// host-assigned, and IsHost is cross-checked against the world's authority flag.
public interface IPermissionActor
{
    ulong Id { get; }

    // The allocation byte this user mints ids in. 0 until the host-authored value syncs across.
    byte AllocationByte { get; }

    // Name for denial logging; falls back to the id when there is no name yet.
    string? DisplayName { get; }

    bool IsHost { get; }

    // The element everything this user structurally owns hangs under (their user root's slot).
    // Null before the body is built.
    IPermissionTarget? RootElement { get; }
}

// An element the gate can be asked about. Every member is a FACT about the element, never a decision -
// the decisions all live in PermissionEngine.
public interface IPermissionTarget
{
    ulong Id { get; }

    PermissionTargetKind PermissionKind { get; }

    // Whether this element belongs to the local-only allocation space (never replicated).
    bool IsLocalElement { get; }

    // Whether the element is mid-construction or mid-decode, so a write to it is engine plumbing.
    bool IsInitializing { get; }

    // The element this one inherits ownership from (a component's slot, a sync element's or worker's
    // parent). Null where the chain stops.
    IPermissionTarget? OwnershipParent { get; }

    // The user a slot or component currently reads as belonging to. This is the grab-flippable signal:
    // it moves when an object is parented under someone's hand, so it can grant interaction but never
    // destruction. -xlinka
    IPermissionActor? StructuralOwner { get; }

    // Whether this element sits at or under the actor's own root.
    bool IsUnderActorRoot(IPermissionActor actor);

    // Human-readable path, for denial logging only.
    string HierarchyPath { get; }
}

// An object that opts into being picked up and moved by any user. Implemented by the grabbable itself
// and by the slot it sits on, because a grab writes to both.
public interface IPermissionGrabSurface
{
    // Whether this object may be picked up at all.
    bool AllowsGrab { get; }

    // Whether it may be taken out of another user's hands.
    bool AllowsSteal { get; }

    // The user recorded as holding it right now, or null.
    IPermissionActor? CurrentHolder { get; }

    // Classify a write against this surface as part of the grab protocol, or not.
    GrabWriteKind ClassifyGrabWrite(IPermissionTarget? member);
}

// The world state the decision reads. All host-authoritative: a client cannot make its own world
// claim to be the authority, because the flag comes from who is actually running the session.
public interface IPermissionWorldFacts
{
    bool IsAuthority { get; }

    // Whether the world is live. A world still starting up or already torn down is not gated.
    bool IsRunning { get; }

    bool IsDisposed { get; }

    IPermissionActor? LocalActor { get; }

    // The flat world element registry. Exposed so the gate can tell a user's own-byte REGISTRATION
    // (allowed) from an own-byte add onto a host-owned per-element collection (denied).
    IPermissionTarget? SlotRegistry { get; }
}

// A pluggable decision that runs before roles. Abstain to defer to the built-in policy. Rules run
// BEFORE the Social/Event floor, so an Allow here escapes that floor - only register a rule you
// trust as much as the gate itself. -xlinka
public interface IPermissionRule
{
    PermissionResult Evaluate(in PermissionRequest request, out string? reason);
}
