// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Components;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

// A link from one worker to a linkable element - any sync member: a value field, a reference, a list, a
// dictionary - expressed as a normal synchronized reference member.
//
// The link IS the ref. It is discovered by WorkerInitializer like any other readonly sync member, gets
// its own RefID, replicates as a RefID, and persists as a stable reference in the owner's member list.
// Linking is an EFFECT of that ref resolving: whenever the target becomes available - set by hand,
// arrived over the wire, or restored from a save - the link asks LinkManager for the target, and the
// manager grants or refuses it. Nothing about a link is re-derived per peer, so a drive authored once
// exists everywhere and survives a save.
//
// Why it matters that this is a member and not a loose object: a loose link only exists where some code
// path happened to construct it, so every consumer had to re-bind on every peer in OnStart, a drive could
// never be saved, and a remote peer could not even see that a field was driven. As a member, the target
// RefID travels with the component and each peer independently reaches the same granted state. -xlinka
public abstract class LinkBase<T> : SyncRef<T>, ILinkRef where T : class, ILinkable
{
    // SyncRef already claims flag 16 (preassigned). 17 is the next free derived-class bit.
    private const int LinkGrantedFlag = 17;

    private User? _grantedTo;

    protected LinkBase()
    {
        MarkInboundRules();
    }

    protected LinkBase(IWorldElement? owner) : base(owner)
    {
        MarkInboundRules();
    }

    private bool IsLinkGranted
    {
        get => GetFlag(LinkGrantedFlag);
        set => SetFlag(LinkGrantedFlag, value);
    }

    public abstract bool IsDriving { get; }

    public abstract bool IsHooking { get; }

    // Drives say yes here on purpose - see FieldDrive.
    public virtual bool IsModificationAllowed => IsDriving;

    public bool WasLinkGranted => IsLinkGranted;

    // False for a link whose target has not resolved yet, and false for a link the manager REFUSED because
    // another driver already owns the target - a refused link is inert, it never writes.
    public bool IsLinkValid
    {
        get
        {
            if (State != ReferenceState.Available || !IsLinkGranted)
                return false;
            var target = Target;
            return target != null && ReferenceEquals(target.ActiveLink, this);
        }
    }

    public bool HasTarget => !Value.IsNull;

    // Whether a consumer should install its attach-time default on this link.
    //
    // A drive a user broke is saved as an explicit empty reference, and a component that re-wires
    // itself on the strength of "no target" alone would rebuild that drive on the next load and every
    // load after it - the edit is impossible to make stick. So the guard also asks whether the member
    // actually came back from data: an empty link the save wrote is left alone, an empty link nothing
    // ever spoke for gets the default. That second half is what keeps saves written before the link
    // member existed working, and what lets a component gain a drive without a migration. -xlinka
    public bool ShouldApplyDefault => !HasTarget && !ValueCameFromData;

    ILinkable? ILinkRef.Target => Target;

    // Authority-side ledger entry: which user's write established the grant this link is currently
    // holding. Null when the link is not granted, or when it was authored on this machine.
    public User? GrantedTo => _grantedTo;

    // False when something else already holds the target. Nothing is stolen and nothing is logged - the
    // caller decides what to do about it.
    public bool TryLink(T? target)
    {
        if (target == null)
        {
            ReleaseLink();
            return true;
        }
        if (target.IsDestroyed)
            return false;

        var holder = target.DirectLink;
        if (holder != null && !ReferenceEquals(holder, this))
            return false;

        return ForceLink(target);
    }

    // Takes the target whatever is already on it: the incumbent is released first, so it sees its drive
    // end through the normal path (its ref clears, the target re-broadcasts its real value) instead of
    // being silently displaced and left pointing at something it no longer drives.
    //
    // Undoable steals record BOTH halves as one step, because undoing only our claim would leave the
    // target with no driver at all rather than the one it had. -xlinka
    public bool ForceLink(T? target, bool undoable = false)
    {
        if (target == null)
        {
            ReleaseLink(undoable);
            return true;
        }
        if (target.IsDestroyed)
            return false;
        if (ReferenceEquals(Target, target))
            return true;

        IUndoBatch? holderStep = null;
        var holder = target.DirectLink;
        if (holder != null && !ReferenceEquals(holder, this))
        {
            var holderField = undoable ? holder as IField : null;
            object? holderBefore = holderField?.BoxedValue;
            holder.ReleaseLink();
            if (holderField != null && !holderField.IsDestroyed)
                holderStep = new FieldEditUndoBatch(holderField, holderBefore, holderField.BoxedValue, UndoLocale.ReleaseDrive);
        }

        // A link's undo value is the RefID it points at, not the driven value: undo re-points the ref and
        // the restored target asks for its grant back through the normal path.
        object? before = undoable ? Value : null;
        Target = target;
        bool linked = ReferenceEquals(Target, target);

        if (undoable && linked)
        {
            InspectorUndo.Record(World, CompositeUndoBatch.Combine(
                UndoLocale.TakeDrive,
                holderStep,
                new FieldEditUndoBatch(this, before, Value, UndoLocale.TakeDrive)));
        }
        return linked;
    }

    // The ref is cleared, so the release replicates and persists like any other reference write - and
    // that is exactly why undo works by re-pointing the ref: the restored target re-requests its grant.
    public void ReleaseLink(bool undoable = false)
    {
        if (!undoable)
        {
            Target = null!;
            return;
        }

        object before = Value;
        Target = null!;
        if (Value.IsNull)
        {
            InspectorUndo.Record(World, new FieldEditUndoBatch(this, before, Value, UndoLocale.BreakDrive));
        }
    }

    // Called by LinkManager once it has confirmed nothing else is driving the target; a link never grants
    // itself.
    public void GrantLink()
    {
        if (IsLinkGranted)
            return;
        IsLinkGranted = true;

        // Credit the grant to whoever's write produced it, so the authority can tell a peer releasing its
        // own drive from a peer tearing down somebody else's. LastModifyingUser is null for a link
        // authored on this machine, and that null is meaningful: host-authored drives are governed by the
        // permission gate's roles, not by link ownership. -xlinka
        if (World?.IsAuthority == true)
            _grantedTo = LastModifyingUser;

        SyncElementChanged();
    }

    // The reference is deliberately left alone: the link keeps pointing where the author put it, it just
    // stops claiming a hold it lost. Clearing the ref here would turn an arbitration decision into a
    // replicated, persisted data change on somebody else's component. -xlinka
    public void RevokeLink()
    {
        if (!IsLinkGranted)
            return;
        IsLinkGranted = false;
        _grantedTo = null;
        SyncElementChanged();
    }

    protected override bool InternalSetRefID(in RefID id, T prevTarget)
        => SetLinkValue(in id, prevTarget);

    protected override bool InternalSetValue(in RefID value, bool sync = true, bool change = true)
        => SetLinkValue(in value, Target, sync, change);

    // Every way the ref value can move - hand assignment, network decode, save load, clear - funnels here,
    // so the old target is always let go before the new RefID lands. Release BEFORE dropping the granted
    // flag: SyncField.UpdateLinkHierarchy only registers the field for a value re-broadcast while it can
    // still see that the departing link was granted and driving. -xlinka
    private bool SetLinkValue(in RefID value, T? prevTarget, bool sync = true, bool change = true)
    {
        prevTarget?.ReleaseLink(this);
        bool wasGranted = IsLinkGranted;
        var prevGrantee = _grantedTo;
        IsLinkGranted = false;
        _grantedTo = null;
        World?.LinkManager?.CancelLink(this);

        if (base.InternalSetValue(in value, sync, change))
            return true;

        // The write was refused (permissions, or the ref itself is under a drive). Put the old link back
        // exactly as it was rather than leaving the target silently unlinked.
        if (prevTarget != null)
        {
            prevTarget.Link(this);
            IsLinkGranted = wasGranted;
            _grantedTo = prevGrantee;
        }
        return false;
    }

    protected override void RunObjectAvailable()
    {
        // A member flagged non-drivable refuses to be driven at all; treat it as an invalid target rather
        // than half-linking it.
        if (IsDriving && RawTarget is SyncElement { IsDrivable: false })
        {
            InvalidateTarget();
            return;
        }

        World?.LinkManager?.RequestLink(this);
        base.RunObjectAvailable();
    }

    // A link member is the one place where a single ref write decides who controls something else, so the
    // authority checks the claim against the arbitration ledger before it lands. Everything a hostile
    // client could try here - claiming a target another peer's drive already holds, or tearing that drive
    // off - is a legal-looking field write that the coarse gates would wave through, and refusing it
    // afterwards in LinkManager is too late: the forged value is already the authority's state, already
    // relayed, and already queued to take the target the moment the real holder lets go. -xlinka
    protected override MessageValidity ValidateInboundWrite(
        BinaryMessageBatch inboundMessage, BinaryReader reader, List<ValidationGroup.Rule> rules)
    {
        var host = base.ValidateInboundWrite(inboundMessage, reader, rules);
        if (host != MessageValidity.Valid)
            return host;

        var manager = World?.LinkManager;
        if (manager == null)
            return MessageValidity.Valid;

        var claimed = new RefID(reader.ReadUInt64());
        return manager.ValidateLinkWrite(this, in claimed, inboundMessage?.SenderUser, rules);
    }

    public override void Dispose()
    {
        World?.LinkManager?.CancelLink(this);
        if (Target != null && World is { IsDisposed: false })
        {
            Target.ReleaseLink(this);
        }
        IsLinkGranted = false;
        _grantedTo = null;
        base.Dispose();
    }
}

// A drive over a WHOLE member, whatever kind of member it is: a value field, a reference, a list, a
// dictionary. Declare it as a readonly member on the driving worker and point it at the target.
//
// The two specialised drives are the ones to reach for when they fit - FieldDrive<T> carries the typed
// value plumbing for a value field, DriveRef<T> the same for a reference field - and this is the general
// form for everything else. It exists because link storage lives on SyncElement now, so a collection is
// a legal target: taking it out of value sync in both directions and marking it non-writable by hand is
// all a driver needs to own one, and the driver fills it however it likes. -xlinka
public class MemberDrive<T> : LinkBase<T> where T : class, ILinkable
{
    public MemberDrive()
    {
    }

    public MemberDrive(IWorldElement? owner) : base(owner)
    {
    }

    public override bool IsDriving => true;

    public override bool IsHooking => false;

    // Unlike FieldDrive and DriveRef, this one REFUSES hand writes to its target while it holds it.
    // Those two allow them for a concrete reason - reparenting writes LocalPosition on driven avatar
    // slots and refusing it would break equip - and the drive just wins again on its next pass. A member
    // taken whole has no equivalent: a hand insert into a driven list is not a value the driver will
    // overwrite next frame, it is a structural edit that quietly survives, and the collection then
    // disagrees with the thing that is supposed to own it. -xlinka
    public override bool IsModificationAllowed => false;

    // Passing null releases the current target.
    public void DriveTarget(T? target)
    {
        Target = target!;
    }
}
