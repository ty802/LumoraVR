// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using LumoraLogger = Lumora.Core.Logging.Logger;
using Lumora.Core;

namespace Lumora.Core.Networking.Sync;

public abstract class SyncElement : IWorldElement, IDisposable, IInitializable, ISyncMember
{
    protected int _flags;

    private int _modificationLevel;

    protected World _world = null!;
    protected RefID _referenceID;

    private int _memberIndex;
    private string? _memberName;
    private ulong _version;

    protected IWorldElement? _parent;

    public IWorldElement? Parent
    {
        get => _parent;
        protected set => _parent = value;
    }

    protected SyncElement()
    {
        IsDrivable = true;
        IsInInitPhase = true;
    }

    // Overridden by the concrete member types (fields, references, lists); the base throws so an unhandled type
    // is caught at save.
    public virtual Persistence.DataTreeNode Save(Persistence.SaveControl control)
        => throw new NotSupportedException($"{GetType().Name} does not support persistence.");

    public virtual void Load(Persistence.DataTreeNode node, Persistence.LoadControl control)
        => throw new NotSupportedException($"{GetType().Name} does not support persistence.");

    // Allocates the RefID and registers with ReferenceController.
    public virtual void Initialize(World world, IWorldElement? parent)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        Parent = parent;
        IsInInitPhase = true;

        // Allocate RefID before setting World
        ReferenceID = world.ReferenceController.AllocateID();
        if (ReferenceID.IsLocalID)
            IsLocalElement = true;

        World = world;
        world.ReferenceController.RegisterObject(this);
        
        world.SyncController?.RegisterSyncElement(this);

        WasChanged = true;
    }

    protected enum InternalFlags
    {
        IsInitialized = 0,
        IsDisposed,
        IsLocalElement,
        IsSyncDirty,
        WasChanged,
        IsInInitPhase,
        HasInitializableChildren,
        NonPersistent,
        IsDrivable,
        IsLoading,
        IsWithinHookCallback,
        ModificationBlocked,
        DriveErrorLogged,
        // 13 through 17 are NOT free, whatever a "reserved" note used to claim here: 13-15 belong to
        // ConflictingSyncElement (IsValid/IsHostOnly/DirectAccessOnly), 16 to SyncRef, 17 to LinkBase.
        // A base-class flag landing on one of those reads back as another class's state, which is a
        // very quiet way to break every field in the engine. Keep this map current. -xlinka
        ValueCameFromData = 18,
        // 19+ available for derived classes
    }

    public World World
    {
        get => _world;
        protected set
        {
            _world = value;
            SetFlag((int)InternalFlags.IsInitialized, value != null);
        }
    }

    public RefID ReferenceID
    {
        get => _referenceID;
        protected set => _referenceID = value;
    }

    public ulong RefIdNumeric => (ulong)ReferenceID;

    internal void SetWorldAndReference(World world, RefID id)
    {
        ReferenceID = id;
        World = world;
        if (ReferenceID.IsLocalID)
        {
            MarkLocalElement();
        }
    }

    // Does NOT allocate a new RefID; used when decoding a FullBatch.
    internal void InitializeFromReplicator(World world, IWorldElement? parent, RefID assignedId)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        Parent = parent;
        IsInInitPhase = true;

        // Use the assigned RefID from the network, don't allocate
        ReferenceID = assignedId;
        if (ReferenceID.IsLocalID)
            IsLocalElement = true;

        World = world;
        world.ReferenceController.RegisterObject(this);

        world.SyncController?.RegisterSyncElement(this);

        WasChanged = true;
    }

    protected bool GetFlag(int flag) => (_flags & (1 << flag)) != 0;

    protected void SetFlag(int flag, bool value)
    {
        if (value)
            _flags |= (1 << flag);
        else
            _flags &= ~(1 << flag);
    }

    public bool IsInitialized => GetFlag((int)InternalFlags.IsInitialized);
    public bool IsDisposed { get => GetFlag((int)InternalFlags.IsDisposed); protected set => SetFlag((int)InternalFlags.IsDisposed, value); }
    public bool IsLocalElement { get => GetFlag((int)InternalFlags.IsLocalElement); protected set => SetFlag((int)InternalFlags.IsLocalElement, value); }
    public bool IsSyncDirty { get => GetFlag((int)InternalFlags.IsSyncDirty); protected set => SetFlag((int)InternalFlags.IsSyncDirty, value); }
    public bool WasChanged { get => GetFlag((int)InternalFlags.WasChanged); protected set => SetFlag((int)InternalFlags.WasChanged, value); }
    public bool IsInInitPhase { get => GetFlag((int)InternalFlags.IsInInitPhase); protected set => SetFlag((int)InternalFlags.IsInInitPhase, value); }
    public bool HasInitializableChildren { get => GetFlag((int)InternalFlags.HasInitializableChildren); protected set => SetFlag((int)InternalFlags.HasInitializableChildren, value); }
    public bool NonPersistent { get => GetFlag((int)InternalFlags.NonPersistent); protected set => SetFlag((int)InternalFlags.NonPersistent, value); }
    public bool IsDrivable { get => GetFlag((int)InternalFlags.IsDrivable); protected set => SetFlag((int)InternalFlags.IsDrivable, value); }
    public bool IsLoading { get => GetFlag((int)InternalFlags.IsLoading); internal set => SetFlag((int)InternalFlags.IsLoading, value); }
    public bool IsWithinHookCallback { get => GetFlag((int)InternalFlags.IsWithinHookCallback); protected set => SetFlag((int)InternalFlags.IsWithinHookCallback, value); }
    public bool ModificationBlocked { get => GetFlag((int)InternalFlags.ModificationBlocked); protected set => SetFlag((int)InternalFlags.ModificationBlocked, value); }
    public bool DriveErrorLogged { get => GetFlag((int)InternalFlags.DriveErrorLogged); protected set => SetFlag((int)InternalFlags.DriveErrorLogged, value); }

    // Whether this member's value was handed to it by a save or by a peer, rather than being whatever
    // the owning worker constructed it with.
    //
    // It separates "the file/peer said empty" from "nobody ever said anything". A component that
    // installs an attach-time default has to know which of those it is looking at: a user who cleared
    // a drive link wrote a real answer, and re-installing the default on the next load would undo the
    // edit every single time. A save written before the member existed says nothing, and there the
    // default is exactly right. Set per member so a worker can gain members without invalidating old
    // saves. -xlinka
    public bool ValueCameFromData { get => GetFlag((int)InternalFlags.ValueCameFromData); internal set => SetFlag((int)InternalFlags.ValueCameFromData, value); }

    public bool IsPersistent => !NonPersistent;
    public bool IsDestroyed => IsDisposed;
    public bool GenerateSyncData => !IsLocalElement && World?.State == World.WorldState.Running;

    public virtual bool IsValid => true;

    // The link currently held on this element, or null when it carries none. Overridden by the
    // element types that actually have link machinery (fields).
    //
    // A METHOD on purpose: SyncField must expose ActiveLink PUBLICLY to satisfy ILinkable,
    // and C# won't let a public property override a protected one, so the old code declared
    // `public new ILinkRef? ActiveLink` - which HID the base member instead of overriding it. Every
    // drive gate down here then kept binding to the base's always-null property, so all three of
    // them were silently dead for every field in the engine: driven fields still generated outbound
    // deltas, still accepted inbound ones, and IsBlockedByDrive never fired. Routing the gates
    // through a virtual method that the public property forwards to is what keeps them honest, and
    // no `new` can quietly detach them again. -xlinka
    protected virtual ILinkRef? ResolveActiveLink() => null;

    public bool IsLinked => ResolveActiveLink() != null;

    // Whether this element's value is produced by a driving link. A driven value is DERIVED, so it
    // is excluded from value sync in both directions - see InvalidateSyncElement and
    // ConflictingSyncElement.Validate.
    public bool IsDriven
    {
        get
        {
            var link = ResolveActiveLink();
            return link != null && link.IsDriving && IsDrivable;
        }
    }

    public bool IsHooked => ResolveActiveLink()?.IsHooking ?? false;

    protected virtual string Name => GetType().Name;
    
    public abstract SyncMemberType MemberType { get; }

    public virtual string ParentHierarchyToString() => Name;

    // A HOOKED link is the passthrough escape: it intercepts the write instead of rejecting it, so it never
    // blocks.
    public bool IsBlockedByDrive
    {
        get
        {
            var link = ResolveActiveLink();
            return IsDriven && link != null && link.WasLinkGranted && !link.IsModificationAllowed && !IsHooked;
        }
    }

    protected bool AuthorizeDataModelAccess(
        DataModelPermissionAction action,
        DataModelPermissionSurface surface = DataModelPermissionSurface.SyncElement,
        User? actor = null,
        bool isNetwork = false,
        bool isFullState = false,
        int? index = null,
        object? key = null,
        bool throwOnError = true)
    {
        var permissions = World?.DataModelPermissions;
        if (permissions == null)
        {
            return true;
        }

        var request = new DataModelPermissionRequest(
            World,
            actor,
            this,
            Parent,
            this,
            surface,
            action,
            isNetwork,
            isFullState,
            index,
            key);

        if (permissions.Authorize(request, out var reason))
        {
            return true;
        }

        if (throwOnError)
        {
            throw new UnauthorizedAccessException(reason ?? "datamodel mutation denied");
        }

        return false;
    }

    protected bool AuthorizeDataModelMutation(
        DataModelPermissionAction action,
        DataModelPermissionSurface surface = DataModelPermissionSurface.SyncElement,
        User? actor = null,
        bool isNetwork = false,
        bool isFullState = false,
        int? index = null,
        object? key = null,
        bool throwOnError = true)
    {
        return AuthorizeDataModelAccess(action, surface, actor, isNetwork, isFullState, index, key, throwOnError);
    }

    protected void BeginHook()
    {
        if (IsWithinHookCallback)
            throw new InvalidOperationException("Already within a hook callback!");
        IsWithinHookCallback = true;
    }

    protected void EndHook()
    {
        if (!IsWithinHookCallback)
            throw new InvalidOperationException("Not within a hook callback!");
        IsWithinHookCallback = false;
    }

    protected void BlockModification()
    {
        if (ModificationBlocked)
            throw new InvalidOperationException("Modification already blocked!");
        ModificationBlocked = true;
    }

    protected void UnblockModification()
    {
        if (!ModificationBlocked)
            throw new InvalidOperationException("Modification not blocked!");
        ModificationBlocked = false;
    }

    public void EndInitPhase()
    {
        if (!IsInInitPhase)
            throw new InvalidOperationException("Initialization phase already ended");

        if (HasInitializableChildren)
        {
            World?.UpdateManager?.EndInitPhaseInChildren(this);
            HasInitializableChildren = false;
        }

        IsInInitPhase = false;
    }

    protected void RegisterNewInitializable(IInitializable initializable)
    {
        if (initializable == null || World == null)
            return;

        HasInitializableChildren = true;
        World.UpdateManager?.AddInitializableChild(this, initializable);
    }

    protected bool BeginModification(bool throwOnError = true)
    {
        if (ModificationBlocked)
        {
            throw new InvalidOperationException("Modification blocked during callback");
        }

        if (_modificationLevel == 0)
        {
            if (IsDisposed)
            {
                var msg = $"Cannot modify disposed element: {this.ParentHierarchyToString()}";
                if (throwOnError) throw new InvalidOperationException(msg);
                LumoraLogger.Error(msg);
                return false;
            }

            World?.HookManager?.ThreadCheck();

            if (IsBlockedByDrive && !IsLoading && !IsWithinHookCallback && !IsInInitPhase)
            {
                var msg = $"Element {Name} is driven and cannot be modified directly";
                if (throwOnError) throw new InvalidOperationException(msg);
                if (!DriveErrorLogged)
                {
                    DriveErrorLogged = true;
                    LumoraLogger.Warn(msg);
                }
                return false;
            }

            if (!IsLoading && !IsWithinHookCallback && !IsInInitPhase &&
                !AuthorizeDataModelMutation(
                    DataModelPermissionAction.Write,
                    this is IField ? DataModelPermissionSurface.Field : DataModelPermissionSurface.SyncElement,
                    throwOnError: throwOnError))
            {
                return false;
            }
        }

        _modificationLevel++;
        return true;
    }

    protected void EndModification()
    {
        if (_modificationLevel == 0)
            throw new InvalidOperationException("Not in modification state");
        _modificationLevel--;
    }

    // Checks IsInInitPhase and IsLoading so elements created during network decode aren't marked dirty.
    public void InvalidateSyncElement()
    {
        // IsInInitPhase and IsLoading checks prevent spurious dirty marking during decode
        if (IsLocalElement || IsDisposed || IsSyncDirty || IsInInitPhase || IsLoading || !GenerateSyncData)
            return;

        // A field under a GRANTED drive is DERIVED, not authored: the driver component replicates,
        // every peer runs its own copy of it, and every peer computes the same value locally. Sending
        // the result would double the traffic AND fight the remote peer's own computation, so the
        // value is excluded from sync for as long as the drive holds it. The other half of this pair
        // is ConflictingSyncElement.Validate, which ignores INBOUND deltas on a driven field; and
        // LinkManager, which re-broadcasts the real value once the drive is released (peers are
        // otherwise stuck on whatever the drive last pushed, since a released field stops changing
        // and generates no delta of its own). All three were dead until ResolveActiveLink. -xlinka
        if (IsDriven && ResolveActiveLink()!.WasLinkGranted)
            return;

        if (World?.SyncController == null)
            return;

        // Don't sync elements that don't have valid RefIDs yet
        if (ReferenceID.IsNull)
        {
            LumoraLogger.Warn($"SyncElement: Skipping sync for element with null RefID [{GetType().Name}] Parent={Parent?.GetType().Name ?? "null"}");
            return;
        }

        IsSyncDirty = true;
        World.SyncController.AddDirtySyncElement(this);
    }

    public void MarkNonPersistent()
    {
        NonPersistent = true;
    }

    public void MarkNonDrivable()
    {
        IsDrivable = false;
    }

    public void MarkLocalElement()
    {
        IsLocalElement = true;
    }

    #region Encoding/Decoding

    // Incremental collections ship OPS, not values, so an op run only means anything against the exact
    // state it was computed from. A receiver one op behind applies the rest to the wrong base and
    // diverges with nothing to notice it - and an index op against a shorter list is worse than wrong,
    // it hits the neighbouring element or throws. So every incremental delta carries the sender's
    // element count from BEFORE the ops in it, and the receiver refuses the whole record when its own
    // count disagrees rather than half-applying it.
    //
    // The base is captured the first time a mutation opens a delta window and held until the window
    // closes at encode, so a batch covering several ops still reports the count the receiver is
    // expected to be sitting at, not the count after.
    //
    // INDEX-addressed collections only (lists, arrays). A key-addressed op names the entry it acts on
    // and lands correctly whatever the counts are, and the replicated slot/component/user collections
    // change on every peer concurrently by design - requiring a shared count there would reject a
    // client creating a slot any time the authority created one first. Lost ops on those are the
    // sequencing layer's job, not this one's. -xlinka
    private int _deltaBaseCount;
    private bool _deltaBaseCaptured;

    // Call BEFORE mutating, on every path that can produce a delta record; repeat calls within the same window
    // are ignored.
    protected void CaptureDeltaBase(int currentCount)
    {
        if (_deltaBaseCaptured)
            return;
        _deltaBaseCaptured = true;
        _deltaBaseCount = currentCount;
    }

    protected void ResetDeltaBase()
    {
        _deltaBaseCaptured = false;
        _deltaBaseCount = 0;
    }

    // Count this delta window started from, falling back to the live count when no mutation opened a window (an
    // element can be encoded dirty without any op having been recorded).
    protected int GetDeltaBaseCount(int currentCount) => _deltaBaseCaptured ? _deltaBaseCount : currentCount;

    protected void WriteDeltaBaseCount(BinaryWriter writer, int currentCount)
        => writer.Write7BitEncoded((ulong)GetDeltaBaseCount(currentCount));

    // Throws before a single op is applied; the session layer turns it into a full re-encode of this one
    // element.
    protected void ReadAndCheckDeltaBaseCount(BinaryReader reader, int localCount)
    {
        int senderCount = (int)reader.Read7BitEncoded();
        if (senderCount != localCount)
        {
            throw new ElementResyncRequiredException(
                ReferenceID,
                $"{GetType().Name} {ReferenceID}: delta base count {senderCount} but local count is {localCount}");
        }
    }

    // Used on the authority, where a mismatching client delta is rejected through the conflict path and
    // answered with an authoritative full record instead of a resync request.
    protected bool ReadDeltaBaseCountMatches(BinaryReader reader, int localCount)
        => (int)reader.Read7BitEncoded() == localCount;


    public virtual void EncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        EncodeFull(writer, outboundMessage, forFullBatch: false);
    }

    // forFullBatch skips the authority check.
    public virtual void EncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage, bool forFullBatch)
    {
        if (!forFullBatch)
        {
            if (World == null || !World.IsAuthority)
                throw new InvalidOperationException("Non-authority shouldn't do a full encode!");
            if (IsSyncDirty)
                throw new InvalidOperationException("Cannot do a full encode on a dirty element!");
        }

        // This is an outbound network encode. On a NON-AUTHORITY peer mark it as a network send so the
        // non-authority escape in Authorize lets the peer serialize its own optimistic change out (e.g. a
        // grab) - the HOST re-validates the change on receive (ConflictingSyncElement.Validate, sender as
        // actor) and arbitrates, so a forged write is still rejected there. Without this, a joiner's own
        // legitimate optimistic writes get encode-denied and silently dropped.
        // On the AUTHORITY this must NOT be a network send: the authority's own outbound encode has no
        // network actor, and the network-actor resolution skips the LocalUser fallback (DataModelPermissions
        // actor resolution), so a network-flagged authority encode resolves to a null actor and gets denied -
        // which would drop every host-originated delta AND every new-joiner full-state record. Authorize as
        // the host (LocalUser -> HostRole) instead. -xlinka
        bool nonAuthority = World == null || !World.IsAuthority;
        AuthorizeDataModelAccess(
            DataModelPermissionAction.Serialize | DataModelPermissionAction.Replicate,
            actor: nonAuthority ? null : World?.LocalUser,
            isNetwork: nonAuthority,
            isFullState: forFullBatch);
        InternalEncodeFull(writer, outboundMessage);
    }

    public virtual void DecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        DecodeFull(reader, inboundMessage, forFullBatch: false);
    }

    // forFullBatch skips the authority check.
    public virtual void DecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage, bool forFullBatch)
    {
        if (!forFullBatch)
        {
            if (World == null || World.IsAuthority)
                throw new InvalidOperationException("Authority shouldn't do a full decode!");
        }

        // try/finally so a decode that throws part-way cannot leave IsLoading stuck on. A stuck flag is
        // silent and permanent: it suppresses this element's own dirty marking and skips its permission
        // gate for the rest of the session. -xlinka
        IsLoading = true;
        try
        {
            InternalDecodeFull(reader, inboundMessage);
            InternalClearDirty();
            ResetDeltaBase();
        }
        finally
        {
            IsLoading = false;
        }
        ValueCameFromData = true;
    }

    public virtual void EncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        // Outbound network encode - see EncodeFull. A non-authority peer may serialize its own optimistic
        // delta out (network escape); the host re-validates on receive and arbitrates. The AUTHORITY must
        // authorize as the host (LocalUser) instead, NOT as a network send - a network-flagged authority
        // encode resolves to a null actor and is denied, dropping every host-originated delta. -xlinka
        bool nonAuthority = World == null || !World.IsAuthority;
        AuthorizeDataModelAccess(
            DataModelPermissionAction.Serialize | DataModelPermissionAction.Replicate,
            actor: nonAuthority ? null : World?.LocalUser,
            isNetwork: nonAuthority);
        InternalEncodeDelta(writer, outboundMessage);
        IsSyncDirty = false;
        InternalClearDirty();
        ResetDeltaBase();
    }

    public virtual void DecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        if (IsSyncDirty)
        {
            // For clients, authority's data wins - clear dirty state and apply the delta.
            // This handles edge cases where elements get marked dirty during initialization.
            if (World != null && !World.IsAuthority)
            {
                IsSyncDirty = false;
                InternalClearDirty();
            }
            else
            {
                throw new InvalidOperationException("Cannot apply delta to a dirty element!");
            }
        }

        // Same reasoning as DecodeFull: a collection that refuses its delta on a base-count mismatch
        // throws out of here by design, and must not strand the flag.
        IsLoading = true;
        try
        {
            InternalDecodeDelta(reader, inboundMessage);
        }
        finally
        {
            IsLoading = false;
        }
        ValueCameFromData = true;
    }

    protected abstract void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage);
    protected abstract void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage);
    protected abstract void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage);
    protected abstract void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage);
    protected abstract void InternalClearDirty();

    #endregion

    #region Validation

    public virtual MessageValidity Validate(BinaryMessageBatch syncMessage, BinaryReader reader, List<ValidationGroup.Rule> rules)
    {
        return MessageValidity.Valid;
    }

    public virtual void Invalidate()
    {
        InvalidateSyncElement();
    }

    public virtual void Confirm(ulong confirmSyncTime)
    {
        IsSyncDirty = false;
        WasChanged = false;
        DriveErrorLogged = false;
    }

    #endregion

    #region ISyncMember Implementation

    public int MemberIndex
    {
        get => _memberIndex;
        set => _memberIndex = value;
    }

    string? ISyncMember.Name
    {
        get => _memberName ?? Name;
        set => _memberName = value;
    }

    bool ISyncMember.IsDirty
    {
        get => IsSyncDirty;
        set => IsSyncDirty = value;
    }

    public ulong Version
    {
        get => _version;
        set => _version = value;
    }

    void ISyncMember.Encode(BinaryWriter writer)
    {
        AuthorizeDataModelAccess(DataModelPermissionAction.Serialize);
        InternalEncodeDelta(writer, null!);
    }

    void ISyncMember.Decode(BinaryReader reader)
    {
        InternalDecodeDelta(reader, null!);
    }

    public virtual object? GetValueAsObject() => null;

    #endregion

    #region Trash Support

    // Used when deleting elements that may need to be restored if authority rejects.
    public void MoveToTrash(ulong tick)
    {
        World?.ReferenceController?.MoveToTrash(this, tick);
    }

    public static bool RestoreFromTrash(World world, RefID id)
    {
        return world?.ReferenceController?.RestoreFromTrash(id) ?? false;
    }

    public static IWorldElement TryRetrieveFromTrash(World world, ulong tick, RefID id)
    {
        return (world?.ReferenceController?.TryRetrieveFromTrash(tick, id)) ?? null!;
    }

    public static void DeleteFromTrash(World world, RefID id)
    {
        world?.ReferenceController?.DeleteFromTrash(id);
    }

    #endregion

    #region Disposal

    public virtual void Dispose()
    {
        World?.ReferenceController?.UnregisterObject(this);
        World?.SyncController?.UnregisterSyncElement(this);

        IsDisposed = true;
        _parent = null;
        World = null!;
    }

    public void Destroy()
    {
        Dispose();
    }

    #endregion
}

public enum MessageValidity
{
    Valid,
    Invalid,
    Conflict,
    Ignore
}

