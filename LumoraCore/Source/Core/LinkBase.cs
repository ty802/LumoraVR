// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

// A link from one worker to a linkable element (a field, a reference), expressed as a normal
// synchronized reference member.
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

    protected LinkBase()
    {
    }

    protected LinkBase(IWorldElement? owner) : base(owner)
    {
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

    // The ref is cleared, so the release replicates and persists like any other reference write.
    public void ReleaseLink(bool undoable = false)
    {
        Target = null!;
    }

    // Called by LinkManager once it has confirmed nothing else is driving the target; a link never grants
    // itself.
    public void GrantLink()
    {
        if (IsLinkGranted)
            return;
        IsLinkGranted = true;
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
        IsLinkGranted = false;
        World?.LinkManager?.CancelLink(this);

        if (base.InternalSetValue(in value, sync, change))
            return true;

        // The write was refused (permissions, or the ref itself is under a drive). Put the old link back
        // exactly as it was rather than leaving the target silently unlinked.
        if (prevTarget != null)
        {
            prevTarget.Link(this);
            IsLinkGranted = wasGranted;
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

    public override void Dispose()
    {
        World?.LinkManager?.CancelLink(this);
        if (Target != null && World is { IsDisposed: false })
        {
            Target.ReleaseLink(this);
        }
        IsLinkGranted = false;
        base.Dispose();
    }
}
