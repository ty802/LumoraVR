// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;
using Lumora.Warden;

namespace Lumora.Core;

public class Slot : ContainerWorker<Component>, IImplementable<IHook<Slot>>, IChangeable, IInitializable, IPermissionGrabSurface
{
    #region Fields

    private readonly List<Slot> _children = new();
    private readonly List<Slot> _localChildren = new();
    private readonly List<Component> _components = new();

    // Cached read-only views. List.AsReadOnly() returns a LIVE wrapper, so one cached instance per list stays
    // correct as the list changes - and we avoid allocating a fresh wrapper on every access. The UI hit-test
    // and the laser raycast both read Components/Children/LocalChildren per slot, recursively, every frame, so
    // these allocations were a major source of per-frame GC churn (and the resulting stutter). -xlinka
    private IReadOnlyList<Component>? _componentsReadOnly;
    private IReadOnlyList<Slot>? _childrenReadOnly;
    private IReadOnlyList<Slot>? _localChildrenReadOnly;
    private Slot _parent = null!;
    private bool _isRemoved;

    private int _transformDirty = 0;
    private float4x4 _cachedTRS = float4x4.Identity;
    private float4x4 _cachedLocalToWorld = float4x4.Identity;
    private float4x4 _cachedWorldToLocal = float4x4.Identity;
    private float3 _cachedGlobalPosition = float3.Zero;
    private floatQ _cachedGlobalRotation = floatQ.Identity;
    private float3 _cachedGlobalScale = float3.One;

    private const int DIRTY_TRS = 1;
    private const int DIRTY_LOCAL_TO_WORLD = 2;
    private const int DIRTY_WORLD_TO_LOCAL = 4;
    private const int DIRTY_GLOBAL_POSITION = 8;
    private const int DIRTY_GLOBAL_ROTATION = 16;
    private const int DIRTY_GLOBAL_SCALE = 32;
    private const int DIRTY_ALL_GLOBAL = DIRTY_LOCAL_TO_WORLD | DIRTY_WORLD_TO_LOCAL |
                                          DIRTY_GLOBAL_POSITION | DIRTY_GLOBAL_ROTATION | DIRTY_GLOBAL_SCALE;

    private readonly Queue<Action> _scheduledActions = new();
    private readonly object _scheduleLock = new();

    #endregion

    #region Events

    public event Action<IChangeable> Changed = null!;

    public event Action<Slot, Slot> OnChildAdded = null!;

    public event Action<Slot, Slot> OnChildRemoved = null!;

    public event Action<Slot, Component> OnComponentAdded = null!;

    public event Action<Slot, Component> OnComponentRemoved = null!;

    public event Action<Slot, Slot, Slot> OnParentChanged = null!;

    public event Action<Slot, bool> OnActiveChanged = null!;

    public event Action<Slot, string> OnNameChanged = null!;

    public event Action<Slot> ChildrenOrderInvalidated
    {
        add { }
        remove { }
    }

    // Simplified event aliases for UI compatibility
    public event Action<Slot> ActiveChanged = null!;
    public event Action<Slot> ParentChanged = null!;

    public event Action<Slot> PersistentChanged = null!;

    public event Action<Slot> OrderOffsetChanged = null!;

    // Before children/components are torn down.
    public event Action<Slot> OnPrepareDestroy = null!;

    // Fired once per frame (deferred) when this slot's world transform changed - either its own
    // local transform or that of an ancestor. Handlers run before hook/connector updates, so a
    // handler that re-drives a transform takes effect the same frame. Subscribe to react to
    // movement without polling every frame in OnUpdate.
    public event Action<Slot> WorldTransformChanged = null!;

    // Fired on this slot and on every ancestor of it when a slot or a component is added to or
    // removed from the subtree rooted here. The argument is the slot the change actually happened
    // on, which is a descendant (or this slot itself).
    // Structural only: transforms, field edits and asset swaps never fire it. Anything that caches
    // a measurement of a whole subtree (bounds, counts, budgets) needs to know when the shape of
    // that subtree changed without subscribing to every descendant individually, and re-subscribing
    // on every add/remove costs more than the walk. The bubble is O(depth) with a null-delegate
    // check per level, and it only runs on structural edits, never per frame. -xlinka
    public event Action<Slot> SubtreeStructureChanged = null!;

    // Bubble from the slot that changed up to the root. Ancestors with no listener cost one null
    // check each.
    private void RaiseSubtreeStructureChanged()
    {
        var current = this;
        while (current != null)
        {
            current.SubtreeStructureChanged?.Invoke(this);
            current = current._parent;
        }
    }

    #endregion

    #region Sync Fields

    public readonly Sync<string> Name = new();

    [NameOverride("Parent")]
    public readonly SyncRef<Slot> ParentSlotRef = new();

    public readonly Sync<string> Tag = new();

    [NameOverride("Active")]
    public readonly Sync<bool> ActiveSelf = new();

    [NameOverride("Persistent")]
    [NonPersistent]
    public readonly Sync<bool> Persistent = new();

    [NameOverride("Position")]
    public readonly Sync<float3> LocalPosition = new();

    [NameOverride("Rotation")]
    public readonly Sync<floatQ> LocalRotation = new();

    [NameOverride("Scale")]
    public readonly Sync<float3> LocalScale = new();

    [NameOverride("OrderOffset")]
    public readonly Sync<long> OrderOffset = new();

    // PERMISSION GATE VIEW
    // A grab writes the held object's parent and pose, which land on the SLOT, not on the grabbable.
    // Only those four members count as grab traffic, and only while the slot actually carries a
    // grabbable that allows grabbing - anything else here stays owner-gated. The component lookup is
    // deliberately behind the member test so it only runs for a transform write. -xlinka

    bool IPermissionGrabSurface.AllowsGrab => GetComponent<Components.Grabbable>()?.AllowGrab.Value == true;

    // Forwarded to the grabbable rather than answered here. These used to be hard-coded false/null, which
    // read as "unstealable and held by nobody" for every transform write in the engine - fine while the
    // gate let any user pose any grabbable, and wrong the moment it started asking who is holding it.
    // The slot is where the pose and parent writes LAND, but the grabbable is where the answer lives. -xlinka
    bool IPermissionGrabSurface.AllowsSteal
        => (GetComponent<Components.Grabbable>() as IPermissionGrabSurface)?.AllowsSteal ?? false;

    IPermissionActor? IPermissionGrabSurface.CurrentHolder
        => (GetComponent<Components.Grabbable>() as IPermissionGrabSurface)?.CurrentHolder;

    GrabWriteKind IPermissionGrabSurface.ClassifyGrabWrite(IPermissionTarget? member)
    {
        bool transformOrParent =
            ReferenceEquals(member, ParentSlotRef) ||
            ReferenceEquals(member, LocalPosition) ||
            ReferenceEquals(member, LocalRotation) ||
            ReferenceEquals(member, LocalScale);
        return transformOrParent ? GrabWriteKind.Transform : GrabWriteKind.None;
    }

    public Sync<string> SlotName => Name;

    #endregion

    #region Properties

    public ulong RefIdNumeric => (ulong)ReferenceID;

    public override bool IsPersistent => Persistent.Value;

    // Protected slots refuse Destroy / RemoveFromHierarchy calls. Set via
    // MarkProtected, propagates up the lineage so the whole chain to the
    // protected slot stays alive. Used for world root, user roots, dashboard
    // anchors, anything that should never be destroyed by gameplay code or
    // remote peers. - xlinka
    public bool IsProtected { get; private set; }

    // When true, Persistent flag is treated as forced-on. Set by passing
    // forcePersistent=true to MarkProtected. The Persistent sync field stays
    // writable but consumers should respect this when serializing.
    public bool ForcedPersistent { get; private set; }

    public void MarkProtected(bool forcePersistent = false)
    {
        IsProtected = true;
        if (forcePersistent)
        {
            ForcedPersistent = true;
            if (!Persistent.Value)
                Persistent.Value = true;
        }
        _parent?.MarkProtected(forcePersistent);
    }

    // Cached nearest-ancestor UserRoot for this slot. Components that need to
    // know "which user does this belong to" should read ActiveUserRoot
    // instead of climbing the hierarchy. UserRoot.OnAwake calls
    // Slot.RegisterUserRoot(this); the slot propagates the reference down to
    // its children. On reparent the inherited value re-resolves from the new
    // parent. Slots that hold their own UserRoot keep that ref and ignore
    // parent propagation. - xlinka
    public UserRoot ActiveUserRoot { get; private set; } = null!;

    public User ActiveUser => (ActiveUserRoot?.ActiveUser) ?? null!;

    public event Action<Slot> ActiveUserRootChanged = null!;

    internal void RegisterUserRoot(UserRoot userRoot)
    {
        if (userRoot == null || userRoot == ActiveUserRoot)
            return;

        if (ActiveUserRoot != null
            && ActiveUserRoot.Slot != null
            && userRoot.Slot != null
            && ActiveUserRoot.Slot != userRoot.Slot
            && ActiveUserRoot.Slot.IsDescendantOf(userRoot.Slot))
        {
            return;
        }

        ActiveUserRoot = userRoot;
        PropagateActiveUserRootToChildren();
        ActiveUserRootChanged?.Invoke(this);
    }

    internal void UnregisterUserRootHierarchy(UserRoot userRoot)
    {
        if (userRoot == null || userRoot != ActiveUserRoot)
            return;

        ActiveUserRoot = null!;
        PropagateActiveUserRootToChildren();
        ActiveUserRootChanged?.Invoke(this);
    }

    private void RefreshActiveUserRootFromParent()
    {
        // Slots holding their own UserRoot keep their ref; parent propagation skips them.
        if (ActiveUserRoot != null && ActiveUserRoot.Slot == this)
            return;

        var inherited = _parent?.ActiveUserRoot;
        if (inherited == ActiveUserRoot)
            return;

        ActiveUserRoot = inherited!;
        PropagateActiveUserRootToChildren();
        ActiveUserRootChanged?.Invoke(this);
    }

    private void PropagateActiveUserRootToChildren()
    {
        foreach (var child in _children)
            child.RefreshActiveUserRootFromParent();
        foreach (var child in _localChildren)
            child.RefreshActiveUserRootFromParent();
    }

    public override IEnumerable<IWorldElement> GetReferencedObjects(bool assetRefOnly, bool persistentOnly = true)
    {
        if (ParentSlotRef?.Target != null && (!persistentOnly || ParentSlotRef.Target.IsPersistent))
            yield return ParentSlotRef.Target;

        foreach (var component in _components)
        {
            if (!persistentOnly || component.IsPersistent)
                yield return component;
        }

        foreach (var child in _children)
        {
            if (!persistentOnly || child.IsPersistent)
                yield return child;
        }
    }

    public IHook<Slot> Hook { get; private set; } = null!;

    IHook IImplementable.Hook => Hook;

    Slot IImplementable.Slot => this;

    public override bool IsRemoved => _isRemoved;

    public IReadOnlyList<Slot> Children => _childrenReadOnly ??= _children.AsReadOnly();

    public IReadOnlyList<Slot> LocalChildren => _localChildrenReadOnly ??= _localChildren.AsReadOnly();

    public int ChildCount => _children.Count;

    public int LocalChildCount => _localChildren.Count;

    public new IReadOnlyList<Component> Components => _componentsReadOnly ??= _components.AsReadOnly();

    public new int ComponentCount => _components.Count;

    public bool IsRootSlot => World != null && ReferenceEquals(World.RootSlot, this);

    // True if ParentSlotRef has a RefID but the Target hasn't resolved yet. Used to distinguish between true
    // root slots and slots waiting for parent resolution.
    public bool HasPendingParent => _parent == null && ParentSlotRef != null && !ParentSlotRef.Value.IsNull;

    // True if ParentSlotRef hasn't been decoded yet (still in init phase). During network decode, sync members
    // are decoded separately from slots, so we can't know the parent until ParentSlotRef is decoded.
    public bool IsParentUnknown => _parent == null && ParentSlotRef != null && ParentSlotRef.IsInInitPhase;

    // Unlike IsRootSlot, this returns false for slots waiting for parent resolution AND for slots where
    // ParentSlotRef hasn't been decoded yet.
    public bool IsTrueRootSlot => _parent == null &&
        ParentSlotRef != null &&
        !ParentSlotRef.IsInInitPhase &&
        ParentSlotRef.Value.IsNull;

    public Slot Root
    {
        get
        {
            var current = this;
            while (current._parent != null)
                current = current._parent;
            return current;
        }
    }

    public int Depth
    {
        get
        {
            int depth = 0;
            var current = _parent;
            while (current != null)
            {
                depth++;
                current = current._parent;
            }
            return depth;
        }
    }

    public int SiblingIndex => _parent?._children.IndexOf(this) ?? 0;

    public Slot ObjectRoot
    {
        get
        {
            var current = this;
            while (current != null)
            {
                if (current.GetComponent<ObjectRoot>() != null)
                    return current;
                if (current._parent == null)
                    return current;
                current = current._parent;
            }
            return this;
        }
    }

    public bool IsUnderLocalUser
    {
        get
        {
            var userRoot = ActiveUserRoot;
            return userRoot != null && userRoot.ActiveUser == World?.LocalUser;
        }
    }

    // Cached owning-user lookup lives on ActiveUserRoot. Old GetUserRoot()
    // hierarchy walk was replaced once UserRoot.OnAwake started registering
    // into Slot.RegisterUserRoot. - xlinka
    public UserRoot GetUserRoot() => ActiveUserRoot;

    #endregion

    #region Parent Property

    public new Slot Parent
    {
        get => _parent;
        set => SetParent(value, preserveGlobalTransform: false);
    }

    // Writes ParentSlotRef, which drives the network sync and the internal state update.
    // Warns when the parent changes during the init phase.
    public void SetParent(Slot newParent, bool preserveGlobalTransform = false)
    {
        if (IsRemoved)
        {
            return;
        }

        if (_parent == newParent)
        {
            return;
        }

        if (IsInInitPhase && newParent != null && !newParent.IsInInitPhase)
        {
            throw new InvalidOperationException("Cannot change parent while in initialization phase.");
        }

        if (newParent == null && World != null)
        {
            newParent = World.RootSlot;
        }

        if (newParent != null && newParent.IsRemoved)
        {
            Logging.Logger.Warn($"Trying to assign a removed parent for slot '{Name?.Value}', resetting to root.");
            newParent = World?.RootSlot!;
        }

        // A slot can never be its own parent, and the descendant check below does NOT catch it (a slot
        // is not its own descendant). Without this the self-reference is WRITTEN to the synced
        // ParentSlotRef and only unwound afterwards, so peers decode a self-parent in between and the
        // slot briefly sits in its own child list - which renders as the same row twice. The world root
        // reaches here through the null-parent fallback above (null resolves to the root, i.e. itself),
        // so this also kills the pointless "tried to assign root slot parent" churn. -xlinka
        if (ReferenceEquals(newParent, this))
        {
            return;
        }

        if (newParent != null && newParent.IsDescendantOf(this))
        {
            return;
        }

        float3 globalPos = float3.Zero;
        floatQ globalRot = floatQ.Identity;
        float3 globalScale = float3.One;

        if (preserveGlobalTransform)
        {
            globalPos = GlobalPosition;
            globalRot = GlobalRotation;
            globalScale = GlobalScale;
        }

        // Update ParentSlotRef - this triggers OnParentSlotRefChanged which updates
        // _parent, child collections, and fires events
        ParentSlotRef.Target = newParent!;

        InvalidateGlobalTransforms();

        if (preserveGlobalTransform && newParent != null)
        {
            GlobalPosition = globalPos;
            GlobalRotation = globalRot;
            GlobalScale = globalScale;
        }
    }

    // Guarded reparent for tooling: answers instead of throwing. Every refusal SetParent makes silently
    // (removed slot, self-parent, a parent that is one of our own descendants, the init-phase rule) is
    // decided here first so the caller learns which way it went, and the permission gate is asked BEFORE
    // anything moves - a denial leaves this slot exactly where it was, still attached, never orphaned.
    //
    // A true reparent is one write to the synced parent reference, so there is no half-applied state to
    // unwind: either the reference takes the new value or it keeps the old one. Callers that hand-rolled
    // their own cycle refusal (the glue tool's ancestor walk) should come through here instead. -xlinka
    public bool TrySetParent(Slot newParent, bool preserveGlobalTransform = true)
    {
        if (IsRemoved || IsDestroyed)
            return false;

        newParent ??= World?.RootSlot!;
        if (newParent == null || newParent.IsRemoved || newParent.IsDestroyed)
            return false;

        if (ReferenceEquals(newParent, this) || newParent.IsDescendantOf(this))
            return false;

        if (ReferenceEquals(_parent, newParent))
            return true; // already there, nothing refused

        if (IsInInitPhase && !newParent.IsInInitPhase)
            return false;

        if (!AuthorizeStructuralChange(World, DataModelPermissionAction.ReferenceWrite,
                DataModelPermissionSurface.Slot, this, _parent, throwOnError: false))
        {
            return false;
        }

        try
        {
            SetParent(newParent, preserveGlobalTransform);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Slot.TrySetParent: '{Name?.Value}' -> '{newParent.Name?.Value}' refused: {ex.Message}");
            return false;
        }

        return ReferenceEquals(_parent, newParent);
    }

    // How many parents up it is from here to root, or -1 when root is not above this slot at all. Depth
    // measures the same thing against the world root; this measures it against any slot you name.
    public int ComputeHierarchyDepth(Slot root)
    {
        int depth = 0;
        var current = this;
        while (current != null && !ReferenceEquals(current, root))
        {
            depth++;
            current = current._parent;
        }
        return current == null ? -1 : depth;
    }

    #endregion

    #region Active State

    public bool IsActive
    {
        get
        {
            if (!ActiveSelf.Value) return false;
            return _parent?.IsActive ?? true;
        }
    }

    public bool IsActiveAndEnabled => IsActive && !IsDestroyed && !_isRemoved;

    // When this slot's ActiveSelf flips, every descendant whose own ActiveSelf is true had its
    // EFFECTIVE active state flip too - fire ActiveChanged on them so effective-active listeners
    // (UI RectTransforms) react. Descendants that are themselves inactive stay inactive regardless,
    // so their subtree is skipped. This is what makes ActiveChanged an EFFECTIVE-active signal.
    private void PropagateActiveChangedToDescendants()
    {
        foreach (var child in Children)
            PropagateActiveChangedToChild(child);
        foreach (var child in LocalChildren)
            PropagateActiveChangedToChild(child);
    }

    private static void PropagateActiveChangedToChild(Slot child)
    {
        if (child == null || child.IsDestroyed || !child.ActiveSelf.Value)
            return;
        child.OnActiveChanged?.Invoke(child, true);
        child.ActiveChanged?.Invoke(child);
        child.PropagateActiveChangedToDescendants();
    }

    public void SetActive(bool active, bool recursive = false)
    {
        bool wasActive = ActiveSelf.Value;
        ActiveSelf.Value = active;

        if (wasActive != active)
        {
            OnActiveChanged?.Invoke(this, active);
            ActiveChanged?.Invoke(this);
        }

        if (recursive)
        {
            foreach (var child in _children)
                child.SetActive(active, true);
            foreach (var child in _localChildren)
                child.SetActive(active, true);
        }
    }

    #endregion

    #region Transform Properties

    public float3 Position
    {
        get => LocalPosition.Value;
        set => LocalPosition.Value = value;
    }

    public floatQ Rotation
    {
        get => LocalRotation.Value;
        set => LocalRotation.Value = value;
    }

    public floatQ Quaternion
    {
        get => LocalRotation.Value;
        set => LocalRotation.Value = value;
    }

    public float3 Scale
    {
        get => LocalScale.Value;
        set => LocalScale.Value = value;
    }

    public float3 GlobalPosition
    {
        get
        {
            if ((_transformDirty & DIRTY_GLOBAL_POSITION) != 0)
            {
                EnsureValidLocalToWorld();
                _cachedGlobalPosition = new float3(_cachedLocalToWorld.c3.x, _cachedLocalToWorld.c3.y, _cachedLocalToWorld.c3.z);
                _transformDirty &= ~DIRTY_GLOBAL_POSITION;
            }
            return _cachedGlobalPosition;
        }
        set
        {
            if (_parent == null)
            {
                LocalPosition.Value = value;
                _cachedGlobalPosition = value;
                _transformDirty &= ~DIRTY_GLOBAL_POSITION;
                return;
            }

            var parentScale = _parent.GlobalScale;
            var invParentRot = _parent.GlobalRotation.Inverse;
            var delta = value - _parent.GlobalPosition;
            var unrotated = invParentRot * delta;

            float3 safeScale = new float3(
                parentScale.x == 0 ? 1 : parentScale.x,
                parentScale.y == 0 ? 1 : parentScale.y,
                parentScale.z == 0 ? 1 : parentScale.z
            );

            LocalPosition.Value = new float3(
                unrotated.x / safeScale.x,
                unrotated.y / safeScale.y,
                unrotated.z / safeScale.z
            );

            if ((_parent._transformDirty & DIRTY_ALL_GLOBAL) == 0)
            {
                _cachedGlobalPosition = value;
                _transformDirty &= ~DIRTY_GLOBAL_POSITION;
            }
        }
    }

    public floatQ GlobalRotation
    {
        get
        {
            if ((_transformDirty & DIRTY_GLOBAL_ROTATION) != 0)
            {
                _cachedGlobalRotation = _parent == null
                    ? LocalRotation.Value
                    : _parent.GlobalRotation * LocalRotation.Value;
                _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
            }
            return _cachedGlobalRotation;
        }
        set
        {
            if (_parent == null)
            {
                LocalRotation.Value = value;
                _cachedGlobalRotation = value;
                _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
                return;
            }
            LocalRotation.Value = _parent.GlobalRotation.Inverse * value;
            if ((_parent._transformDirty & DIRTY_GLOBAL_ROTATION) == 0)
            {
                _cachedGlobalRotation = value;
                _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
            }
        }
    }

    // Set GlobalPosition WITHOUT generating a field sync (delta). Use when a stream on this slot is the transport
    // for the position, so it isn't also replicated over the delta channel (mirrors how TrackedDevicePositioner
    // writes stream-shared body nodes). Still fires change events for rendering / transform-dirty / stream
    // sampling - it only skips the sync element going dirty. -xlinka
    public void SetGlobalPositionSilently(float3 value)
    {
        if (_parent == null)
        {
            LocalPosition.SetValueSilently(value, change: true);
            _cachedGlobalPosition = value;
            _transformDirty &= ~DIRTY_GLOBAL_POSITION;
            return;
        }

        var parentScale = _parent.GlobalScale;
        var invParentRot = _parent.GlobalRotation.Inverse;
        var delta = value - _parent.GlobalPosition;
        var unrotated = invParentRot * delta;

        float3 safeScale = new float3(
            parentScale.x == 0 ? 1 : parentScale.x,
            parentScale.y == 0 ? 1 : parentScale.y,
            parentScale.z == 0 ? 1 : parentScale.z
        );

        LocalPosition.SetValueSilently(new float3(
            unrotated.x / safeScale.x,
            unrotated.y / safeScale.y,
            unrotated.z / safeScale.z
        ), change: true);

        if ((_parent._transformDirty & DIRTY_ALL_GLOBAL) == 0)
        {
            _cachedGlobalPosition = value;
            _transformDirty &= ~DIRTY_GLOBAL_POSITION;
        }
    }

    // Set GlobalRotation WITHOUT generating a field sync (delta). See SetGlobalPositionSilently. -xlinka
    public void SetGlobalRotationSilently(floatQ value)
    {
        if (_parent == null)
        {
            LocalRotation.SetValueSilently(value, change: true);
            _cachedGlobalRotation = value;
            _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
            return;
        }
        LocalRotation.SetValueSilently(_parent.GlobalRotation.Inverse * value, change: true);
        if ((_parent._transformDirty & DIRTY_GLOBAL_ROTATION) == 0)
        {
            _cachedGlobalRotation = value;
            _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
        }
    }

    public float3 GlobalScale
    {
        get
        {
            if ((_transformDirty & DIRTY_GLOBAL_SCALE) != 0)
            {
                if (_parent == null)
                {
                    _cachedGlobalScale = LocalScale.Value;
                }
                else
                {
                    var parentScale = _parent.GlobalScale;
                    var local = LocalScale.Value;
                    _cachedGlobalScale = new float3(parentScale.x * local.x, parentScale.y * local.y, parentScale.z * local.z);
                }
                _transformDirty &= ~DIRTY_GLOBAL_SCALE;
            }
            return _cachedGlobalScale;
        }
        set
        {
            if (_parent == null)
            {
                LocalScale.Value = value;
                _cachedGlobalScale = value;
                _transformDirty &= ~DIRTY_GLOBAL_SCALE;
                return;
            }

            var parentScale = _parent.GlobalScale;
            float3 safeParentScale = new float3(
                parentScale.x == 0 ? 1 : parentScale.x,
                parentScale.y == 0 ? 1 : parentScale.y,
                parentScale.z == 0 ? 1 : parentScale.z
            );

            LocalScale.Value = new float3(
                value.x / safeParentScale.x,
                value.y / safeParentScale.y,
                value.z / safeParentScale.z
            );

            if ((_parent._transformDirty & DIRTY_GLOBAL_SCALE) == 0)
            {
                _cachedGlobalScale = value;
                _transformDirty &= ~DIRTY_GLOBAL_SCALE;
            }
        }
    }

    public float4x4 LocalTransform
    {
        get
        {
            EnsureValidTRS();
            return _cachedTRS;
        }
    }

    public float4x4 GlobalTransform
    {
        get
        {
            EnsureValidLocalToWorld();
            return _cachedLocalToWorld;
        }
    }

    public float4x4 LocalToWorld
    {
        get
        {
            EnsureValidLocalToWorld();
            return _cachedLocalToWorld;
        }
    }

    public float4x4 WorldToLocal
    {
        get
        {
            EnsureValidWorldToLocal();
            return _cachedWorldToLocal;
        }
    }

    public float3 Forward => GlobalRotation * float3.Forward;

    public float3 Right => GlobalRotation * float3.Right;

    public float3 Up => GlobalRotation * float3.Up;

    public float3 Backward => GlobalRotation * float3.Backward;

    public float3 Left => GlobalRotation * float3.Left;

    public float3 Down => GlobalRotation * float3.Down;

    #endregion

    #region Transform Methods

    private void InvalidateTransformCache()
    {
        _transformDirty |= DIRTY_TRS;
        InvalidateGlobalTransforms();
    }

    private void InvalidateGlobalTransforms()
    {
        // Queue the deferred WorldTransformChanged for any slot that actually has a listener (cheap
        // null check; the queue dedups). Done before the dirty early-return so this slot always
        // registers; descendants register as the cascade visits them.
        if (WorldTransformChanged != null)
            World?.UpdateManager?.RegisterMovedSlot(this);

        if ((_transformDirty & DIRTY_ALL_GLOBAL) == DIRTY_ALL_GLOBAL)
            return;

        _transformDirty |= DIRTY_ALL_GLOBAL;

        foreach (var child in _children)
            child.InvalidateGlobalTransforms();
        foreach (var child in _localChildren)
            child.InvalidateGlobalTransforms();
    }

    internal void FireWorldTransformChanged()
    {
        WorldTransformChanged?.Invoke(this);
    }

    private void EnsureValidTRS()
    {
        if ((_transformDirty & DIRTY_TRS) != 0)
        {
            _cachedTRS = float4x4.TRS(LocalPosition.Value, LocalRotation.Value, LocalScale.Value);
            _transformDirty &= ~DIRTY_TRS;
        }
    }

    private void EnsureValidLocalToWorld()
    {
        if ((_transformDirty & DIRTY_LOCAL_TO_WORLD) != 0)
        {
            if (_parent == null)
            {
                EnsureValidTRS();
                _cachedLocalToWorld = _cachedTRS;
            }
            else
            {
                _parent.EnsureValidLocalToWorld();
                EnsureValidTRS();
                _cachedLocalToWorld = _parent._cachedLocalToWorld * _cachedTRS;
            }
            _transformDirty &= ~DIRTY_LOCAL_TO_WORLD;
        }
    }

    private void EnsureValidWorldToLocal()
    {
        if ((_transformDirty & DIRTY_WORLD_TO_LOCAL) != 0)
        {
            EnsureValidLocalToWorld();
            _cachedWorldToLocal = _cachedLocalToWorld.Inverse;
            _transformDirty &= ~DIRTY_WORLD_TO_LOCAL;
        }
    }

    public float3 GlobalPointToLocal(float3 globalPoint)
    {
        EnsureValidWorldToLocal();
        return _cachedWorldToLocal.MultiplyPoint(globalPoint);
    }

    public float3 GlobalPointToLocal(in float3 globalPoint)
    {
        EnsureValidWorldToLocal();
        return _cachedWorldToLocal.MultiplyPoint(in globalPoint);
    }

    public float3 LocalPointToGlobal(float3 localPoint)
    {
        EnsureValidLocalToWorld();
        return _cachedLocalToWorld.MultiplyPoint(localPoint);
    }

    public float3 LocalPointToGlobal(in float3 localPoint)
    {
        EnsureValidLocalToWorld();
        return _cachedLocalToWorld.MultiplyPoint(in localPoint);
    }

    public float3 GlobalDirectionToLocal(float3 globalDirection)
    {
        EnsureValidWorldToLocal();
        return _cachedWorldToLocal.MultiplyVector(globalDirection);
    }

    public float3 LocalDirectionToGlobal(float3 localDirection)
    {
        EnsureValidLocalToWorld();
        return _cachedLocalToWorld.MultiplyVector(localDirection);
    }

    public floatQ GlobalRotationToLocal(floatQ globalRotation)
    {
        return GlobalRotation.Inverse * globalRotation;
    }

    public floatQ LocalRotationToGlobal(floatQ localRotation)
    {
        return GlobalRotation * localRotation;
    }

    // ── Vector conversions (rotation + scale, no translation) ───────────────────
    // Like the direction conversions but carry scale; the direction helpers above
    // already use MultiplyVector, so these are the explicitly-named "vector" forms.

    public float3 LocalVectorToGlobal(in float3 localVector)
        => IsRootSlot ? localVector : LocalToWorld.MultiplyVector(in localVector);

    public float3 GlobalVectorToLocal(in float3 globalVector)
        => IsRootSlot ? globalVector : WorldToLocal.MultiplyVector(in globalVector);

    // ── Scale conversions ───────────────────────────────────────────────────────

    public float3 LocalScaleToGlobal(in float3 localScale)
        => IsRootSlot ? localScale : localScale * GlobalScale;

    public float3 GlobalScaleToLocal(in float3 globalScale)
        => IsRootSlot ? globalScale : globalScale / GlobalScale;

    // Averages the components after conversion.
    public float LocalScaleToGlobal(float localScale)
        => IsRootSlot ? localScale : AvgComponent(LocalScaleToGlobal(new float3(localScale, localScale, localScale)));

    // Averages the components after conversion.
    public float GlobalScaleToLocal(float globalScale)
        => IsRootSlot ? globalScale : AvgComponent(GlobalScaleToLocal(new float3(globalScale, globalScale, globalScale)));

    // ── Parent-space conversions (this slot's local space ↔ its parent's space) ──

    public float3 LocalPointToParent(in float3 localPoint)
        => IsRootSlot ? localPoint : LocalTransform.MultiplyPoint(in localPoint);

    public float3 ParentPointToLocal(in float3 parentPoint)
        => IsRootSlot ? parentPoint : LocalTransform.Inverse.MultiplyPoint(in parentPoint);

    public float3 LocalDirectionToParent(in float3 localDirection)
        => IsRootSlot ? localDirection : LocalRotation.Value * localDirection;

    public float3 ParentDirectionToLocal(in float3 parentDirection)
        => IsRootSlot ? parentDirection : LocalRotation.Value.Inverse * parentDirection;

    public float3 LocalVectorToParent(in float3 localVector)
        => IsRootSlot ? localVector : LocalTransform.MultiplyVector(in localVector);

    public float3 ParentVectorToLocal(in float3 parentVector)
        => IsRootSlot ? parentVector : LocalTransform.Inverse.MultiplyVector(in parentVector);

    public floatQ LocalRotationToParent(in floatQ localRotation)
        => IsRootSlot ? localRotation : LocalRotation.Value * localRotation;

    public floatQ ParentRotationToLocal(in floatQ parentRotation)
        => IsRootSlot ? parentRotation : LocalRotation.Value.Inverse * parentRotation;

    public float3 LocalScaleToParent(in float3 localScale)
        => IsRootSlot ? localScale : LocalScale.Value * localScale;

    public float LocalScaleToParent(float localScale)
        => IsRootSlot ? localScale : AvgComponent(LocalScale.Value * new float3(localScale, localScale, localScale));

    // ── Arbitrary-space conversions (this slot's local space ↔ another slot's) ───
    // Composed through the cached Global↔Local primitives, with shortcuts when the
    // target space is this slot or its direct parent.

    public float3 LocalPointToSpace(in float3 localPoint, Slot space)
    {
        if (ReferenceEquals(space, this)) return localPoint;
        if (ReferenceEquals(space, Parent)) return LocalPointToParent(in localPoint);
        return space.GlobalPointToLocal(LocalPointToGlobal(in localPoint));
    }

    public float3 SpacePointToLocal(in float3 spacePoint, Slot space)
    {
        if (ReferenceEquals(space, this)) return spacePoint;
        if (ReferenceEquals(space, Parent)) return ParentPointToLocal(in spacePoint);
        return GlobalPointToLocal(space.LocalPointToGlobal(in spacePoint));
    }

    public float3 LocalDirectionToSpace(in float3 localDirection, Slot space)
        => ReferenceEquals(space, this) ? localDirection : space.GlobalDirectionToLocal(LocalDirectionToGlobal(localDirection));

    public float3 SpaceDirectionToLocal(in float3 spaceDirection, Slot space)
        => ReferenceEquals(space, this) ? spaceDirection : GlobalDirectionToLocal(space.LocalDirectionToGlobal(spaceDirection));

    public float3 LocalVectorToSpace(in float3 localVector, Slot space)
        => ReferenceEquals(space, this) ? localVector : space.GlobalVectorToLocal(LocalVectorToGlobal(in localVector));

    public float3 SpaceVectorToLocal(in float3 spaceVector, Slot space)
        => ReferenceEquals(space, this) ? spaceVector : GlobalVectorToLocal(space.LocalVectorToGlobal(in spaceVector));

    public float3 LocalScaleToSpace(in float3 localScale, Slot space)
        => ReferenceEquals(space, this) ? localScale : space.GlobalScaleToLocal(LocalScaleToGlobal(in localScale));

    public float LocalScaleToSpace(float localScale, Slot space)
        => ReferenceEquals(space, this) ? localScale : space.GlobalScaleToLocal(LocalScaleToGlobal(localScale));

    public float3 SpaceScaleToLocal(in float3 spaceScale, Slot space)
        => ReferenceEquals(space, this) ? spaceScale : GlobalScaleToLocal(space.LocalScaleToGlobal(in spaceScale));

    public float SpaceScaleToLocal(float spaceScale, Slot space)
        => ReferenceEquals(space, this) ? spaceScale : GlobalScaleToLocal(space.LocalScaleToGlobal(spaceScale));

    public floatQ LocalRotationToSpace(in floatQ localRotation, Slot space)
        => ReferenceEquals(space, this) ? localRotation : space.GlobalRotationToLocal(LocalRotationToGlobal(localRotation));

    public floatQ SpaceRotationToLocal(in floatQ spaceRotation, Slot space)
        => ReferenceEquals(space, this) ? spaceRotation : GlobalRotationToLocal(space.LocalRotationToGlobal(spaceRotation));

    public float4x4 GetLocalToSpaceMatrix(Slot space)
        => space.WorldToLocal * LocalToWorld;

    private static float AvgComponent(float3 v) => (v.x + v.y + v.z) / 3f;

    public void LookAt(float3 target, float3? up = null)
    {
        var upVec = up ?? float3.Up;
        var direction = (target - GlobalPosition).Normalized;
        if (direction.LengthSquared > 0.0001f)
        {
            GlobalRotation = floatQ.LookRotation(direction, upVec);
        }
    }

    // Euler angles in degrees.
    public void Rotate(float3 eulerAngles, bool worldSpace = false)
    {
        const float Deg2Rad = (float)(System.Math.PI / 180.0);
        var rotation = floatQ.FromEuler(eulerAngles * Deg2Rad);
        if (worldSpace)
        {
            GlobalRotation = rotation * GlobalRotation;
        }
        else
        {
            LocalRotation.Value = LocalRotation.Value * rotation;
        }
    }

    public void Translate(float3 translation, bool worldSpace = false)
    {
        if (worldSpace)
        {
            GlobalPosition += translation;
        }
        else
        {
            LocalPosition.Value += translation;
        }
    }

    public void SetLocalTransform(float3 position, floatQ rotation, float3 scale)
    {
        LocalPosition.Value = position;
        LocalRotation.Value = rotation;
        LocalScale.Value = scale;
    }

    public void SetGlobalTransform(float3 position, floatQ rotation, float3 scale)
    {
        GlobalPosition = position;
        GlobalRotation = rotation;
        GlobalScale = scale;
    }

    public void CopyTransformFrom(Slot source, bool global = false)
    {
        if (source == null) return;

        if (global)
        {
            GlobalPosition = source.GlobalPosition;
            GlobalRotation = source.GlobalRotation;
            GlobalScale = source.GlobalScale;
        }
        else
        {
            LocalPosition.Value = source.LocalPosition.Value;
            LocalRotation.Value = source.LocalRotation.Value;
            LocalScale.Value = source.LocalScale.Value;
        }
    }

    public void ResetTransform()
    {
        LocalPosition.Value = float3.Zero;
        LocalRotation.Value = floatQ.Identity;
        LocalScale.Value = float3.One;
    }

    // TRANSFORM BY ANOTHER
    // "Where would this slot end up if the OTHER slot moved to a given pose and dragged this one along?"
    // Take this slot's pose expressed in the other's space right now, then re-plant it against the virtual
    // pose. The other slot is not touched, and this slot does not become its child - only the resulting
    // world pose is handed over (or applied). Placing a grabbed object relative to a snap target and
    // previewing a drag both want exactly this. -xlinka
    //
    // Worked in decomposed TRS rather than through matrices on purpose: this transform model IS a TRS
    // triple, so composing matrices only to decompose them back would lose to shear the moment a
    // non-uniform scale sat under a rotation, and would round-trip every exact value. -xlinka

    public void GetTransformedByAnother(Slot other, in float3 globalPosition, in floatQ globalRotation,
        in float3 globalScale, out float3 position, out floatQ rotation, out float3 scale)
    {
        if (other == null)
        {
            position = GlobalPosition;
            rotation = GlobalRotation;
            scale = GlobalScale;
            return;
        }

        var otherRotation = other.GlobalRotation;
        var otherScale = NonZero(other.GlobalScale);
        var inverseOtherRotation = otherRotation.Inverse;

        var relativePosition = (inverseOtherRotation * (GlobalPosition - other.GlobalPosition)) / otherScale;
        var relativeRotation = inverseOtherRotation * GlobalRotation;
        var relativeScale = GlobalScale / otherScale;

        position = globalPosition + globalRotation * (globalScale * relativePosition);
        rotation = globalRotation * relativeRotation;
        scale = globalScale * relativeScale;
    }

    public float4x4 GetTransformedByAnother(Slot other, in float3 globalPosition, in floatQ globalRotation,
        in float3 globalScale)
    {
        GetTransformedByAnother(other, in globalPosition, in globalRotation, in globalScale,
            out var position, out var rotation, out var scale);
        return float4x4.TRS(position, rotation, scale);
    }

    public void TransformByAnother(Slot other, in float3 globalPosition, in floatQ globalRotation,
        in float3 globalScale)
    {
        if (other == null)
            return;

        GetTransformedByAnother(other, in globalPosition, in globalRotation, in globalScale,
            out var position, out var rotation, out var scale);
        SetGlobalTransform(position, rotation, scale);
    }

    // A zero on any axis makes the relative transform undefined. Substituting 1 keeps a flattened slot
    // from turning the whole result into NaN, which is the failure people actually notice.
    private static float3 NonZero(in float3 value) => new float3(
        value.x == 0f ? 1f : value.x,
        value.y == 0f ? 1f : value.y,
        value.z == 0f ? 1f : value.z);

    #endregion

    #region Constructor & Initialization

    public Slot()
    {
        InitializeSyncFields();

        ComponentAdded += HandleComponentAdded;
        ComponentRemoved += HandleComponentRemoved;
    }

    private void InitializeSyncFields()
    {
        Name.Value = "Slot";
        ActiveSelf.Value = true;
        LocalPosition.Value = float3.Zero;
        LocalRotation.Value = floatQ.Identity;
        LocalScale.Value = float3.One;
        Tag.Value = string.Empty;
        Persistent.Value = true;
        OrderOffset.Value = 0;

        ((ISyncMember)Name).Name = "Name";
        ((ISyncMember)ParentSlotRef).Name = "Parent";
        ((ISyncMember)Tag).Name = "Tag";
        ((ISyncMember)ActiveSelf).Name = "Active";
        ((ISyncMember)Persistent).Name = "Persistent";
        ((ISyncMember)LocalPosition).Name = "Position";
        ((ISyncMember)LocalRotation).Name = "Rotation";
        ((ISyncMember)LocalScale).Name = "Scale";
        ((ISyncMember)OrderOffset).Name = "OrderOffset";
        ((ISyncMember)componentCollection).Name = "Components";

        Persistent.MarkNonPersistent();

        LocalPosition.OnChanged += _ =>
        {
            InvalidateTransformCache();
            QueueHookUpdate();
        };
        LocalRotation.OnChanged += _ =>
        {
            InvalidateTransformCache();
            QueueHookUpdate();
        };
        LocalScale.OnChanged += _ =>
        {
            InvalidateTransformCache();
            QueueHookUpdate();
        };
        ActiveSelf.OnChanged += _ =>
        {
            OnActiveChanged?.Invoke(this, ActiveSelf.Value);
            ActiveChanged?.Invoke(this);
            // Effective active: descendants whose own ActiveSelf is true just had their effective
            // active flip with this ancestor, so fire ActiveChanged on them too (ActiveChanged
            // tracks EFFECTIVE active here, not just ActiveSelf). This is what lets a canvas re-render
            // content shown by reactivating an ancestor (e.g. the dashboard's parked render rig)
            // without any forced-rebuild hack. No listeners other than UI RectTransforms, so safe.
            PropagateActiveChangedToDescendants();
            OnChanged();
        };
        Name.OnChanged += _ =>
        {
            OnNameChanged?.Invoke(this, Name.Value);
            OnChanged();
        };
        Persistent.OnChanged += _ =>
        {
            PersistentChanged?.Invoke(this);
        };
        OrderOffset.OnChanged += _ =>
        {
            OrderOffsetChanged?.Invoke(this);
        };

        ParentSlotRef.OnTargetChange += OnParentSlotRefChanged;

        // When ParentSlotRef.Value changes (RefID decoded), trigger hook update
        // This handles the case when ParentSlotRef is decoded as RefID.Null (true root slot)
        // where OnTargetChange won't fire because there's no target to resolve
        ParentSlotRef.OnChanged += OnParentSlotRefValueChanged;
    }

    private void HandleComponentAdded(Component component)
    {
        if (component == null)
            return;

        if (!_components.Contains(component))
        {
            _components.Add(component);
        }

        World?.RegisterComponent(component);
        OnComponentAdded?.Invoke(this, component);
        RaiseSubtreeStructureChanged();
        OnChanged();
    }

    private void HandleComponentRemoved(Component component)
    {
        if (component == null)
            return;

        _components.Remove(component);
        World?.UnregisterComponent(component);
        OnComponentRemoved?.Invoke(this, component);
        RaiseSubtreeStructureChanged();
        OnChanged();
    }

    // Used to trigger hook updates when parent info is first decoded from network.
    private void OnParentSlotRefValueChanged(RefID newValue)
    {
        if (Hook != null && !ParentSlotRef.IsInInitPhase)
        {
            QueueHookUpdate();
        }
    }

    // Updates the internal _parent field and child collections. Don't apply null-to-RootSlot fallback during
    // batch decode, as the real parent may not be registered yet.
    private void OnParentSlotRefChanged(SyncRef<Slot> syncRef)
    {
        if (IsDestroyed)
        {
            return;
        }

        if (IsRootSlot)
        {
            Logging.Logger.Warn($"Tried to assign root slot parent. NewParent={syncRef.Target}");
            World?.RunSynchronously(() => syncRef.Target = null!);
            return;
        }

        var restoreParent = _parent;
        if (restoreParent == null || restoreParent.IsDestroyed)
        {
            restoreParent = World?.RootSlot;
        }

        var target = syncRef.Target;
        if (target == null)
        {
            if (World?.RootSlot != null)
            {
                Logging.Logger.Warn("New parent is null, resetting to the root slot.");
                World.RunSynchronously(() => syncRef.Target = World.RootSlot);
            }
            return;
        }

        if (target == _parent)
        {
            return;
        }

        if (target == this || target.IsDescendantOf(this))
        {
            Logging.Logger.Warn("New parent is a descendant of this slot, reverting to a safe parent.");
            var resetParent = World?.RootSlot;
            if (restoreParent != null && !restoreParent.IsDescendantOf(this))
            {
                resetParent = restoreParent;
            }

            if (resetParent != null)
            {
                World?.RunSynchronously(() => syncRef.Target = resetParent);
            }
            return;
        }

        var oldParent = _parent;
        if (_parent != null)
        {
            _parent.DetachChildInternal(this);
        }

        target.AttachChildInternal(this);
        _parent = target;

        InvalidateTransformCache();
        RefreshActiveUserRootFromParent();
        OnParentChanged?.Invoke(this, oldParent, target);
        ParentChanged?.Invoke(this);
        OnChanged();

        if (oldParent == null && Hook != null)
        {
            QueueHookUpdate();
        }
    }

	public void Initialize(World world)
	{
		if (world == null)
			throw new ArgumentNullException(nameof(world));

        IsInInitPhase = true;
		base.Initialize(world, _parent);
        EndInitializationStageForMembers();

        // CRITICAL: Mark sync members dirty AFTER registration
        // Values may have been set before Initialize() was called (e.g., Name.Value = "User X")
        // At that time, InvalidateSyncElement() did nothing because World was null.
        // Now that sync members are registered, we need to mark them dirty for network sync.
        if (World?.State == World.WorldState.Running)
        {
            InvalidateSyncMembersForNewSlot();
        }

        AttachToParentLists();

        if (IsRootSlot)
        {
            _transformDirty = 0;
            _cachedTRS = float4x4.TRS(LocalPosition.Value, LocalRotation.Value, LocalScale.Value);
            _cachedLocalToWorld = _cachedTRS;
            _cachedWorldToLocal = _cachedTRS.Inverse;
            _cachedGlobalPosition = LocalPosition.Value;
            _cachedGlobalRotation = LocalRotation.Value;
            _cachedGlobalScale = LocalScale.Value;
        }

        RegisterExistingComponents();
        InitializeHook();
        EndInitPhase();
	}

    // Initialize this Slot from network replication with a pre-assigned RefID.
    // Used by SlotCollection when creating slots from network data.
    // NOTE: This runs on the sync thread - no Godot operations allowed here!
    // Hook initialization is deferred to the main thread.
	internal void InitializeFromReplicator(World world, RefID assignedId)
	{
		if (world == null)
			throw new ArgumentNullException(nameof(world));

        var refController = world.ReferenceController;
        if (refController == null)
        {
            throw new InvalidOperationException("ReferenceController required for replicated slot initialization");
        }

        IsInInitPhase = true;
        refController.AllocationBlockBegin(assignedId);
        try
        {
            base.Initialize(world, _parent);
        }
        finally
        {
            refController.AllocationBlockEnd();
        }

        EndInitializationStageForMembers();

        AttachToParentLists();

        if (IsRootSlot)
        {
            _transformDirty = 0;
            _cachedTRS = float4x4.TRS(LocalPosition.Value, LocalRotation.Value, LocalScale.Value);
            _cachedLocalToWorld = _cachedTRS;
            _cachedWorldToLocal = _cachedTRS.Inverse;
            _cachedGlobalPosition = LocalPosition.Value;
            _cachedGlobalRotation = LocalRotation.Value;
            _cachedGlobalScale = LocalScale.Value;
        }

        // DO NOT call InitializeHook() here! This runs on sync thread.
        // Hook creation must be deferred to the main thread via World.RunSynchronously
        World?.RunSynchronously(() =>
        {
            if (IsDestroyed || World == null)
                return;

            // NOTE: Do NOT call RegisterExistingComponents() here!
            // Network-created slots get their components through WorkerCollection network sync,
            // not through pre-attached components. RegisterExistingComponents() is only
            // for locally-created slots that had components attached before Initialize().
            InitializeHook();
        });

        EndInitPhase();
    }

    // Mark all sync members as dirty after slot initialization: sync member values may have
    // been set BEFORE Initialize() was called (e.g., slot.Name.Value = "User X"), when the sync
    // member wasn't yet registered with SyncController, so InvalidateSyncElement() did nothing then.
    private void InvalidateSyncMembersForNewSlot()
    {
        Name?.InvalidateSyncElement();
        ActiveSelf?.InvalidateSyncElement();
        LocalPosition?.InvalidateSyncElement();
        LocalRotation?.InvalidateSyncElement();
        LocalScale?.InvalidateSyncElement();
        Tag?.InvalidateSyncElement();
        Persistent?.InvalidateSyncElement();
        OrderOffset?.InvalidateSyncElement();
        ParentSlotRef?.InvalidateSyncElement();
    }

    private void InitializeHook()
    {
        if (World == null) return;
        if (World.HookTypes.GetHookType(typeof(Slot)) == null) return;

        // Inside a load scope the platform node is BUILT LATER, in budgeted bites, while the world is
        // still held pre-Running. Queued in initialize order, which is parent-before-child, so a child's
        // node never looks for a parent node that does not exist yet. -xlinka
        if (World.DeferHookCreation)
        {
            World.QueueHookCreation(CreateHook);
            return;
        }

        CreateHook();
    }

    // A component's hook wants its slot's platform node the moment it starts. Inside a load scope that
    // node is queued rather than built, and in a world that is ALREADY RUNNING the component's startup
    // can reach it before the drain does - so pull it forward, ancestors first, because a node still
    // has to be parented under one that exists. The queued creation then no-ops on the Hook != null
    // guard below. Pure-transform slots (most of a big graph) are untouched and keep spreading. -xlinka
    internal void EnsureHookCreated()
    {
        if (Hook != null || IsDestroyed || _isRemoved || World == null)
            return;
        _parent?.EnsureHookCreated();
        CreateHook();
    }

    private void CreateHook()
    {
        if (IsDestroyed || _isRemoved || World == null || Hook != null)
            return;

        Type hookType = World.HookTypes.GetHookType(typeof(Slot));
        if (hookType == null) return;

        Hook = (IHook<Slot>)Activator.CreateInstance(hookType)!;
        Hook?.AssignOwner(this);
        Hook?.Initialize();
        QueueHookUpdate();
    }

    private void AttachToParentLists()
    {
        if (_parent != null)
        {
            _parent.AttachChildInternal(this);
        }
    }

    private void RegisterExistingComponents()
    {
        if (World == null || _components.Count == 0)
            return;

        var refController = World.ReferenceController;
        if (refController == null)
            return;

        bool startedLocalBlock = false;
        if (IsLocalElement && !refController.IsInLocalAllocation)
        {
            refController.LocalAllocationBlockBegin();
            startedLocalBlock = true;
        }

        var existing = _components.ToArray();
        _components.Clear();

        foreach (var component in existing)
        {
            if (component == null)
                continue;

            var key = refController.PeekID();
            componentCollection.Add(key, component, isNewlyCreated: true, skipSync: true);
        }

        if (startedLocalBlock)
        {
            refController.LocalAllocationBlockEnd();
        }
    }

    #endregion

    #region Scheduling

    public override void RunInUpdates(int updateCount, Action action)
    {
        if (action == null) return;

        if (updateCount <= 0)
        {
            action();
            return;
        }

        World?.RunInUpdates(updateCount, action);
    }

    public override void RunSynchronously(Action action)
    {
        if (action == null) return;
        World?.RunSynchronously(action);
    }

    public void RunNextFrame(Action action)
    {
        RunInUpdates(1, action);
    }

    internal void ProcessScheduledActions()
    {
        lock (_scheduleLock)
        {
            while (_scheduledActions.Count > 0)
            {
                var action = _scheduledActions.Dequeue();
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Logging.Logger.Error($"Error in scheduled action: {ex}");
                }
            }
        }
    }

    #endregion

    #region Component Methods

	public T AttachComponent<T>() where T : Component, new()
	{
        if (World == null)
        {
            var component = new T();
            if (!_components.Contains(component))
            {
                _components.Add(component);
            }
            return component;
        }

        return base.AttachComponent<T>();
    }

	public Component AttachComponent(Type componentType)
	{
		if (!typeof(Component).IsAssignableFrom(componentType))
			throw new ArgumentException("Type must derive from Component", nameof(componentType));

        if (World == null)
        {
            var component = (Component)Activator.CreateInstance(componentType)!;
            if (!_components.Contains(component))
            {
                _components.Add(component);
            }
            return component;
        }

        return base.AttachComponent(componentType);
	}

    public T GetOrAttachComponent<T>() where T : Component, new()
    {
        var existing = GetComponent<T>();
        return existing ?? AttachComponent<T>();
    }

    public T GetComponent<T>() where T : Component
    {
        // Hot path: the UI hit-test calls this per slot, twice per laser, every frame. A plain loop avoids
        // the iterator allocation LINQ's OfType<T>().FirstOrDefault() makes on EVERY call - that allocation,
        // multiplied across the whole UI tree each frame, was a big chunk of the UI's per-frame GC churn. -xlinka
        for (int i = 0; i < _components.Count; i++)
        {
            if (_components[i] is T match)
                return match;
        }
        return null!;
    }

    public Component GetComponent(Type type)
    {
        for (int i = 0; i < _components.Count; i++)
        {
            if (type.IsInstanceOfType(_components[i]))
                return _components[i];
        }
        return null!;
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    public IEnumerable<Component> GetAllComponents()
    {
        return _components;
    }

    // Unlike GetComponents, this works with interface types.
    public IEnumerable<T> GetComponentsImplementing<T>() where T : class
    {
        return _components.OfType<T>();
    }

    public bool TryGetComponent<T>(out T component) where T : Component
    {
        component = GetComponent<T>();
        return component != null;
    }

    public T GetComponent<T>(Func<T, bool> predicate) where T : Component
    {
        for (int i = 0; i < _components.Count; i++)
        {
            if (_components[i] is T match && predicate(match))
                return match;
        }
        return null!;
    }

    public T GetComponentInParent<T>(bool includeSelf = true) where T : Component
    {
        if (includeSelf)
        {
            var comp = GetComponent<T>();
            if (comp != null) return comp;
        }

        return (_parent?.GetComponentInParent<T>(true)) ?? null!;
    }

    public T GetComponentInParents<T>(bool includeSelf = true) where T : Component
        => GetComponentInParent<T>(includeSelf);

    public T GetComponentInChildren<T>(bool includeSelf = true) where T : Component
    {
        if (includeSelf)
        {
            var comp = GetComponent<T>();
            if (comp != null) return comp;
        }

        foreach (var child in EnumerateAllChildren())
        {
            var comp = child.GetComponentInChildren<T>(true);
            if (comp != null) return comp;
        }

        return null!;
    }

    public IEnumerable<T> GetComponentsInChildren<T>(bool includeSelf = true) where T : Component
    {
        if (includeSelf)
        {
            foreach (var comp in _components.OfType<T>())
                yield return comp;
        }

        foreach (var child in EnumerateAllChildren())
        {
            foreach (var comp in child.GetComponentsInChildren<T>(true))
                yield return comp;
        }
    }

    // Allocation-free variant of GetComponentsInChildren{T}: fills a caller-provided
    // (ideally reused) list instead of allocating an iterator state machine + an OfType wrapper at EVERY
    // slot. The per-frame laser raycast walks the whole world with this, so those allocations were real. -xlinka
    public void GetComponentsInChildren<T>(List<T> results, bool includeSelf = true) where T : Component
    {
        if (includeSelf)
        {
            for (int i = 0; i < _components.Count; i++)
                if (_components[i] is T match) results.Add(match);
        }
        for (int i = 0; i < _children.Count; i++)
            _children[i].GetComponentsInChildren(results, true);
        for (int i = 0; i < _localChildren.Count; i++)
            _localChildren[i].GetComponentsInChildren(results, true);
    }

    public IEnumerable<T> GetComponentsInParent<T>(bool includeSelf = true) where T : Component
    {
        if (includeSelf)
        {
            foreach (var comp in _components.OfType<T>())
                yield return comp;
        }

        if (_parent != null)
        {
            foreach (var comp in _parent.GetComponentsInParent<T>(true))
                yield return comp;
        }
    }

    public T GetComponentInParentsOrChildren<T>() where T : Component
    {
        var comp = GetComponentInParents<T>();
        return comp != null ? comp : GetComponentInChildren<T>(includeSelf: false);
    }

    public T GetComponentInChildrenOrParents<T>() where T : Component
    {
        var comp = GetComponentInChildren<T>();
        return comp != null ? comp : GetComponentInParents<T>(includeSelf: false);
    }

    public void ForeachComponentInParents<T>(Action<T> action, bool includeSelf = true) where T : Component
    {
        foreach (var comp in GetComponentsInParent<T>(includeSelf))
            action(comp);
    }

    // Filtered query overloads (predicate + skip-disabled). The filter is required so the
    // parameterless calls above stay unambiguous; pass null to match any component.
    // "Disabled" = the owning slot is inactive or the component's Enabled flag is false.

    public T GetComponentInChildren<T>(Predicate<T>? filter, bool excludeDisabled = false) where T : Component
    {
        if (excludeDisabled && !IsActive)
            return null!;
        foreach (var comp in _components.OfType<T>())
        {
            if (excludeDisabled && !comp.Enabled.Value) continue;
            if (filter == null || filter(comp)) return comp;
        }
        foreach (var child in EnumerateAllChildren())
        {
            var found = child.GetComponentInChildren<T>(filter, excludeDisabled);
            if (found != null) return found;
        }
        return null!;
    }

    public IEnumerable<T> GetComponentsInChildren<T>(Predicate<T>? filter, bool excludeDisabled = false) where T : Component
    {
        if (excludeDisabled && !IsActive)
            yield break;
        foreach (var comp in _components.OfType<T>())
        {
            if (excludeDisabled && !comp.Enabled.Value) continue;
            if (filter == null || filter(comp)) yield return comp;
        }
        foreach (var child in EnumerateAllChildren())
            foreach (var comp in child.GetComponentsInChildren<T>(filter, excludeDisabled))
                yield return comp;
    }

    public T GetComponentInParents<T>(Predicate<T>? filter, bool excludeDisabled = false) where T : Component
    {
        var current = this;
        while (current != null)
        {
            if (!excludeDisabled || current.IsActive)
            {
                foreach (var comp in current._components.OfType<T>())
                {
                    if (excludeDisabled && !comp.Enabled.Value) continue;
                    if (filter == null || filter(comp)) return comp;
                }
            }
            current = current._parent;
        }
        return null!;
    }

    public IEnumerable<T> GetComponentsInParent<T>(Predicate<T>? filter, bool excludeDisabled = false) where T : Component
    {
        var current = this;
        while (current != null)
        {
            if (!excludeDisabled || current.IsActive)
            {
                foreach (var comp in current._components.OfType<T>())
                {
                    if (excludeDisabled && !comp.Enabled.Value) continue;
                    if (filter == null || filter(comp)) yield return comp;
                }
            }
            current = current._parent;
        }
    }

	public new void RemoveComponent(Component component)
	{
        if (component == null)
            return;

        base.RemoveComponent(component);
	}

    public void RemoveComponents<T>() where T : Component
    {
        var toRemove = _components.OfType<T>().ToArray();
        foreach (var comp in toRemove)
            RemoveComponent(comp);
    }

    public bool HasComponent<T>() where T : Component
    {
        return _components.OfType<T>().Any();
    }

    public void ForeachComponent(Action<Component> action)
    {
        foreach (var comp in _components)
            action(comp);
    }

    public void ForeachComponentInChildren<T>(Action<T> action, bool includeSelf = true) where T : Component
    {
        if (includeSelf)
        {
            foreach (var comp in _components.OfType<T>())
                action(comp);
        }

        foreach (var child in EnumerateAllChildren())
            child.ForeachComponentInChildren(action, true);
    }

    #endregion

    #region Persistence

    // Components and child slots are polymorphic/recursive, so they're serialized explicitly here
    // rather than through the generic member loop; the "Components" member collection is excluded from it.
    // Still chains to base so [NonPersistent] fields (Persistent itself among them) get the same
    // treatment as everywhere else - a slot that made it into a save had Persistent=true already
    // (its parent filters on IsPersistent before calling this), so a loaded slot's constructor default
    // of true is exactly what would have round-tripped anyway.
    protected override bool ShouldSerializeMember(ISyncMember member)
        => !ReferenceEquals(member, componentCollection) && base.ShouldSerializeMember(member);

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = (DataTreeDictionary)base.Save(control);

        // Components are type-tagged so the loader knows what to instantiate.
        var componentList = new DataTreeList();
        foreach (var component in Components)
        {
            if (component == null || !component.IsPersistent)
                continue;
            componentList.Add(WorkerSaveLoad.SaveWorker(component, control));
        }
        dictionary.Add("Components", componentList);

        // Child slots recurse (always Slot type, so no tag needed).
        var childList = new DataTreeList();
        foreach (var child in Children)
        {
            if (child == null || !child.IsPersistent)
                continue;
            childList.Add(child.Save(control));
        }
        dictionary.Add("Children", childList);

        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;

        // Members first (associates this slot's RefID with its saved GUID).
        base.Load(dictionary, control);

        if (dictionary.TryGetList("Components") is { } componentList)
        {
            foreach (var entry in componentList.Children)
            {
                var (typeName, data) = WorkerSaveLoad.ExtractWorker(entry, control);
                var type = WorkerManager.GetType(typeName);
                if (type == null || !typeof(Component).IsAssignableFrom(type))
                {
                    Lumora.Core.Logging.Logger.Warn($"Slot.Load: unknown component type '{typeName}', skipping.");
                    continue;
                }
                // runOnAttachBehavior: false so the loaded state isn't overwritten by attach defaults.
                var component = AttachComponent(type, runOnAttachBehavior: false);
                component.Load(data, control);
            }
        }

        if (dictionary.TryGetList("Children") is { } childList)
        {
            foreach (var entry in childList.Children)
            {
                var child = AddSlot(string.Empty);
                child.Load(entry, control);
            }
        }
    }

    // GRAPH SAVE / LOAD - serialize this slot's subtree as a self-contained graph (optionally with
    // its asset dependencies). Lets an object be written to a file/record and loaded into ANY
    // world (spawning, inventory, copy-paste).

    public void GenerateHierarchy(HashSet<Slot> set)
    {
        if (set == null || !set.Add(this))
            return;
        foreach (var child in EnumerateAllChildren())
            child.GenerateHierarchy(set);
    }

    // DEPENDENCIES
    // What this subtree points AT that does not live inside it. Both walks are transitive - a collected
    // dependency is itself scanned, since an asset can reference another asset and a referenced slot can
    // reference a third. This is the same collection the graph save runs; exposed so duplication, a
    // "would this travel intact?" check, or an inventory pack can ask the question without saving.
    //
    // Two shapes because callers want two different answers. The SLOT walk answers "what else has to come
    // with this for a full deep copy" and returns the outermost slot of each dependency subtree; the ASSET
    // walk answers "what has to come with this to still LOOK right" and returns the individual provider
    // components. References pointing INTO the subtree are not dependencies and are never listed. -xlinka

    public List<Slot> CollectDependencies(bool includeNonPersistent = false)
    {
        var rootHierarchy = new HashSet<Slot>();
        GenerateHierarchy(rootHierarchy);

        var output = new List<Slot>();
        CollectDependencySlots(rootHierarchy, new HashSet<Slot>(), output, includeNonPersistent);
        return output;
    }

    public List<Component> CollectAssetDependencies(bool includeNonPersistent = false)
    {
        var rootHierarchy = new HashSet<Slot>();
        GenerateHierarchy(rootHierarchy);

        var output = new List<Component>();
        CollectAssetDependencies(rootHierarchy, new HashSet<Component>(), output, includeNonPersistent);
        return output;
    }

    // References that point outside the subtree (and its collected dependencies) are nulled so the graph stands
    // alone.
    public SavedGraph SaveObject(DependencyHandling dependencyHandling = DependencyHandling.BreakExternal,
                                 bool saveNonPersistent = false, ReferenceTranslator? refTranslator = null)
    {
        if (World == null)
            throw new InvalidOperationException("Cannot SaveObject a slot that isn't in a world.");

        refTranslator ??= new ReferenceTranslator();
        var control = new SaveControl(this, refTranslator) { SaveNonPersistent = saveNonPersistent };

        var rootHierarchy = new HashSet<Slot>();
        GenerateHierarchy(rootHierarchy);

        // CollectAssets brings along referenced asset-provider components; CollectAll brings along
        // every referenced slot (deep copy). dependencyHierarchy = all slots covered by collected
        // slot-dependencies (used for the allow-set + dedup).
        List<Component>? assetDependencies = null;
        List<Slot>? slotDependencies = null;
        var dependencyHierarchy = new HashSet<Slot>();

        if (dependencyHandling == DependencyHandling.CollectAssets)
        {
            assetDependencies = new List<Component>();
            CollectAssetDependencies(rootHierarchy, new HashSet<Component>(), assetDependencies, saveNonPersistent);
        }
        else if (dependencyHandling == DependencyHandling.CollectAll)
        {
            slotDependencies = new List<Slot>();
            CollectDependencySlots(rootHierarchy, dependencyHierarchy, slotDependencies, saveNonPersistent);
        }

        // References may target anything inside the subtree or a collected dependency (slots, their
        // components, and all their sync members); everything else is nulled for self-containment.
        var allowed = new HashSet<RefID>();
        foreach (var slot in rootHierarchy)
            AddSlotReferenceableIds(slot, allowed);
        foreach (var slot in dependencyHierarchy)
            AddSlotReferenceableIds(slot, allowed);
        if (assetDependencies != null)
            foreach (var dependency in assetDependencies)
                AddReferenceableIds(dependency, allowed);
        control.ReferenceFilter = r => (r == RefID.Null || allowed.Contains(r)) ? r : RefID.Null;

        // Claim identities for the members this graph points at, after the filter is in place so
        // references leaving the subtree are skipped. Ordering inside the graph then stops mattering:
        // a ref to a field resolves whether its holder was written before or after it.
        control.ReserveSubtreeIdentities(this);
        if (slotDependencies != null)
        {
            foreach (var dependency in slotDependencies)
                control.ReserveSubtreeIdentities(dependency);
        }
        if (assetDependencies != null)
        {
            foreach (var dependency in assetDependencies)
                control.ReserveWorkerIdentities(dependency);
        }

        var dictionary = new DataTreeDictionary();
        dictionary.Add("Object", Save(control));

        if (slotDependencies != null && slotDependencies.Count > 0)
        {
            var dependencyList = new DataTreeList();
            foreach (var dependency in slotDependencies)
                dependencyList.Add(dependency.Save(control));
            dictionary.Add("Dependencies", dependencyList);
        }

        if (assetDependencies != null && assetDependencies.Count > 0)
        {
            control.SaveNonPersistent = true; // dependency components save regardless of slot persistence
            var assetList = new DataTreeList();
            foreach (var dependency in assetDependencies)
                assetList.Add(WorkerSaveLoad.SaveWorker(dependency, control));
            dictionary.Add("Assets", assetList);
        }

        var typeVersions = new DataTreeDictionary();
        control.StoreTypeVersions(typeVersions);
        dictionary.Add("TypeVersions", typeVersions);

        return new SavedGraph(dictionary);
    }

    private static void AddReferenceableIds(Worker worker, HashSet<RefID> set)
    {
        set.Add(worker.ReferenceID);
        for (int i = 0; i < worker.SyncMemberCount; i++)
        {
            var member = worker.GetSyncMember(i);
            if (member != null)
                set.Add(member.ReferenceID);
        }
    }

    // Gather asset-provider (or PreserveWithAssets) components referenced from inside the subtree
    // but living outside it, recursively (assets can reference other assets).
    private static void CollectAssetDependencies(HashSet<Slot> rootHierarchy, HashSet<Component> seen,
                                                 List<Component> output, bool saveNonPersistent)
    {
        foreach (var slot in rootHierarchy)
        {
            CollectWorkerAssetRefs(slot, rootHierarchy, seen, output, saveNonPersistent);
            foreach (var component in slot.Components)
                CollectWorkerAssetRefs(component, rootHierarchy, seen, output, saveNonPersistent);
        }
    }

    private static void CollectWorkerAssetRefs(Worker worker, HashSet<Slot> rootHierarchy,
        HashSet<Component> seen, List<Component> output, bool saveNonPersistent)
    {
        for (int i = 0; i < worker.SyncMemberCount; i++)
        {
            if (worker.GetSyncMember(i) is not ISyncRef syncRef)
                continue;
            if (syncRef.Target is not Component target || target.IsDestroyed)
                continue;
            if (target is not IAssetProvider && !target.PreserveWithAssets)
                continue;
            if (target.Slot != null && rootHierarchy.Contains(target.Slot))
                continue; // already inside the saved subtree
            if (!saveNonPersistent && !target.IsPersistent)
                continue;
            if (!seen.Add(target))
                continue;
            output.Add(target);
            CollectWorkerAssetRefs(target, rootHierarchy, seen, output, saveNonPersistent);
        }
    }

    private static void AddSlotReferenceableIds(Slot slot, HashSet<RefID> set)
    {
        AddReferenceableIds(slot, set);
        foreach (var component in slot.Components)
            AddReferenceableIds(component, set);
    }

    // Gather every slot referenced from inside the subtree but living outside it, recursively
    // (each collected dependency subtree is itself scanned for further external references).
    private static void CollectDependencySlots(HashSet<Slot> rootHierarchy, HashSet<Slot> dependencyHierarchy,
                                               List<Slot> output, bool saveNonPersistent)
    {
        var toScan = new Queue<Slot>(rootHierarchy);
        while (toScan.Count > 0)
        {
            var slot = toScan.Dequeue();
            ScanForDependencySlots(slot, rootHierarchy, dependencyHierarchy, output, toScan, saveNonPersistent);
            foreach (var component in slot.Components)
                ScanForDependencySlots(component, rootHierarchy, dependencyHierarchy, output, toScan, saveNonPersistent);
        }
    }

    private static void ScanForDependencySlots(Worker worker, HashSet<Slot> rootHierarchy,
        HashSet<Slot> dependencyHierarchy, List<Slot> output, Queue<Slot> toScan, bool saveNonPersistent)
    {
        // A slot's own parent is not something it DEPENDS on - it is where it currently happens to hang,
        // and a loaded graph gets re-parented by whoever loads it. Following it would make the subtree
        // root's parent a dependency, and since a dependency drags its whole subtree along, one slot's
        // parent ref would pull in the entire world. -xlinka
        var parentRef = (worker as Slot)?.ParentSlotRef;

        for (int i = 0; i < worker.SyncMemberCount; i++)
        {
            if (worker.GetSyncMember(i) is not ISyncRef syncRef)
                continue;
            if (parentRef != null && ReferenceEquals(syncRef, parentRef))
                continue;
            var target = syncRef.Target;
            if (target == null || target.IsDestroyed)
                continue;
            var slot = target as Slot ?? (target as Component)?.Slot;
            if (slot == null || slot.IsDestroyed)
                continue;
            if (rootHierarchy.Contains(slot) || dependencyHierarchy.Contains(slot))
                continue;
            if (!saveNonPersistent && !slot.IsPersistent)
                continue;

            // New top-level dependency: save the whole subtree it roots, drop any earlier-collected
            // root that turns out to live inside it, and queue its (new) slots for further scanning.
            var subtree = new HashSet<Slot>();
            slot.GenerateHierarchy(subtree);
            output.RemoveAll(existing => subtree.Contains(existing));
            output.Add(slot);
            foreach (var s in subtree)
                if (dependencyHierarchy.Add(s))
                    toScan.Enqueue(s);
        }
    }

    // Any collected "Assets" are attached under assetsRoot (or a new child of the world root) so their
    // references resolve. Intended for an empty target slot (e.g. parent.AddSlot().LoadObject(…)).
    public void LoadObject(DataTreeDictionary node, Slot? assetsRoot = null, ReferenceTranslator? refTranslator = null)
    {
        if (IsDestroyed)
            throw new InvalidOperationException("Cannot LoadObject onto a destroyed slot.");
        if (World == null)
            throw new InvalidOperationException("Cannot LoadObject onto a slot that isn't in a world.");

        refTranslator ??= new ReferenceTranslator();
        var control = new LoadControl(World, refTranslator);

        // A big item lands as one frame of platform-node construction, which is the spawn spike. Past the
        // threshold the nodes get COLLECTED instead and the world builds them in budgeted bites from its
        // own update, so a full room drops in over a few frames instead of one stall. Small spawns are
        // left strictly alone: a prop is fully built by the time this returns, which is what every
        // spawn-then-configure caller in the codebase assumes. -xlinka
        bool deferred = World.State == World.WorldState.Running
            && CountSavedSlots(node) >= DeferredSpawnSlotThreshold;
        if (deferred)
            World.BeginDeferredHookCreation();

        try
        {
            if (node.TryGetDictionary("TypeVersions") is { } typeVersions)
                control.LoadTypeVersions(typeVersions);

            // The saved root's own parent reference was nulled by the filter; remember where this
            // slot lives so loading the (parentless) object doesn't detach it from the hierarchy.
            var originalParent = _parent;

            if (node.TryGetNode("Object") is { } objectNode)
                Load(objectNode, control);

            if (originalParent != null && !ReferenceEquals(_parent, originalParent))
                SetParent(originalParent, preserveGlobalTransform: false);

            if (node.TryGetList("Dependencies") is { } dependencyList && dependencyList.Children.Count > 0)
            {
                var dependencyHost = World.RootSlot.AddSlot(Name + " - Dependencies");
                foreach (var entry in dependencyList.Children)
                    dependencyHost.AddSlot(string.Empty).Load(entry, control);
            }

            if (node.TryGetList("Assets") is { } assetList && assetList.Children.Count > 0)
            {
                var host = assetsRoot ?? World.RootSlot.AddSlot(Name + " - Assets");
                foreach (var entry in assetList.Children)
                {
                    var (typeName, data) = WorkerSaveLoad.ExtractWorker(entry, control);
                    var type = WorkerManager.GetType(typeName);
                    if (type == null || !typeof(Component).IsAssignableFrom(type))
                    {
                        Lumora.Core.Logging.Logger.Warn($"Slot.LoadObject: unknown asset component type '{typeName}', skipping.");
                        continue;
                    }
                    var component = host.AttachComponent(type, runOnAttachBehavior: false);
                    component.Load(data, control);
                }
            }
        }
        finally
        {
            control.FinishLoad();
            if (deferred)
                World.EndDeferredHookCreation();
        }
    }

    // Slots big enough that building every platform node inline is a visible hitch. Tuned to sit well
    // above anything the spawn-inline paths (and their probes) rely on and well below a real prop. -xlinka
    public const int DeferredSpawnSlotThreshold = 96;

    // Counts the slots a saved graph will produce, without loading it: the object node plus every
    // descendant in its Children lists. Cheap - it walks the parsed tree, it does not build anything.
    public static int CountSavedSlots(DataTreeDictionary node)
    {
        var objectNode = node.TryGetNode("Object") as DataTreeDictionary;
        int total = objectNode != null ? CountSlotSubtree(objectNode) : 0;

        if (node.TryGetList("Dependencies") is { } dependencies)
        {
            foreach (var entry in dependencies.Children)
            {
                if (entry is DataTreeDictionary dependency)
                    total += CountSlotSubtree(dependency);
            }
        }
        return total;
    }

    private static int CountSlotSubtree(DataTreeDictionary slotNode)
    {
        int total = 1;
        if (slotNode.TryGetList("Children") is { } children)
        {
            foreach (var child in children.Children)
            {
                if (child is DataTreeDictionary childDictionary)
                    total += CountSlotSubtree(childDictionary);
            }
        }
        return total;
    }

    // Set encrypt to store it AES-GCM encrypted at rest (inventory items / saved objects).
    public void SaveObjectToFile(string path, DependencyHandling dependencyHandling = DependencyHandling.CollectAssets,
                                 bool saveNonPersistent = false, bool encrypt = false)
    {
        var graph = SaveObject(dependencyHandling, saveNonPersistent);
        var bytes = graph.SaveToBytes();
        if (encrypt)
            bytes = LocalEncryption.Encrypt(bytes);
        File.WriteAllBytes(path, bytes);
    }

    public void LoadObjectFromFile(string path, Slot? assetsRoot = null, ReferenceTranslator? refTranslator = null)
    {
        if (DataTreeConverter.LoadFromBytes(LocalEncryption.Decrypt(File.ReadAllBytes(path))) is DataTreeDictionary dictionary)
            LoadObject(dictionary, assetsRoot, refTranslator);
        else
            throw new InvalidDataException("File does not contain a saved object graph.");
    }

    // Asynchronously load a graph from a local file: the bytes are read and decrypted off-thread, then the
    // load itself runs on the world update thread (data-model mutations must happen there). Awaitable from
    // anywhere - it opens its own context rather than needing a world task around it.
    //
    // This slot owns the context, so there is no destroyed check on the far side: a slot deleted while the
    // file is still being read cancels the load on the way back instead of loading into a corpse. The
    // caller sees that as a cancelled task rather than a silent no-op. -xlinka
    public async Task LoadObjectAsync(string path, Slot? assetsRoot = null, ReferenceTranslator? refTranslator = null)
    {
        WorldContext.Enter(World, this);
        await WorldContext.ToBackground();

        var bytes = await File.ReadAllBytesAsync(path);
        var node = DataTreeConverter.LoadFromBytes(LocalEncryption.Decrypt(bytes));

        await WorldContext.ToWorld();
        if (node is DataTreeDictionary dictionary)
            LoadObject(dictionary, assetsRoot, refTranslator);
    }

    #endregion

    #region Child Slot Methods

    public Slot AddSlot(string name = "Slot")
    {
        if (World == null)
        {
            var uninitializedSlot = new Slot();
            uninitializedSlot.Name.Value = name;
            uninitializedSlot.Parent = this;
            return uninitializedSlot;
        }

        var refController = World.ReferenceController;
        if (refController == null)
            throw new InvalidOperationException("ReferenceController required for slot allocation");

        if (IsLocalElement)
        {
            return AddLocalSlot(name);
        }

        var slot = new Slot();
        slot.Name.Value = name;
        slot.Parent = this;
        slot.Initialize(World);
        World.RegisterSlot(slot);

        return slot;
    }

    // Not synchronized.
    public Slot AddLocalSlot(string name = "LocalSlot")
    {
        if (World == null)
            return AddSlot(name);

        var refController = World.ReferenceController;
        refController?.LocalAllocationBlockBegin();

        var slot = new Slot();
        slot.Name.Value = name;
        slot.Parent = this;
        slot.Initialize(World);
        World.RegisterSlot(slot);

        refController?.LocalAllocationBlockEnd();

        return slot;
    }

    private bool ShouldStoreInLocalChildren(Slot child)
    {
        return child != null && child.IsLocalElement && !IsLocalElement;
    }

    private void AttachChildInternal(Slot child)
    {
        if (child == null)
            return;

        // A locally-allocated child gets its local RefID only during Initialize, AFTER the first
        // parent attach already filed it under _children. Re-file on the post-init attach so a
        // local slot lives in exactly one list; otherwise it double-registers and leaks into
        // Children (and from there into saves, duplication and the inspector tree). - xlinka
        var list = ShouldStoreInLocalChildren(child) ? _localChildren : _children;
        var other = ReferenceEquals(list, _children) ? _localChildren : _children;
        bool refiled = other.Remove(child);

        if (!list.Contains(child))
        {
            list.Add(child);
            if (IsInInitPhase && !refiled)
            {
                childInitializables.Add(child);
            }
            if (!refiled)
            {
                OnChildAdded?.Invoke(this, child);
                RaiseSubtreeStructureChanged();
            }
        }
    }

    private void DetachChildInternal(Slot child)
    {
        if (child == null)
            return;

        // Check both lists: the child's local flag can differ between attach and detach. - xlinka
        bool removed = _children.Remove(child);
        removed |= _localChildren.Remove(child);
        if (removed)
        {
            OnChildRemoved?.Invoke(this, child);
            RaiseSubtreeStructureChanged();
        }
    }

    private IEnumerable<Slot> EnumerateAllChildren()
    {
        foreach (var child in _children)
            yield return child;
        foreach (var child in _localChildren)
            yield return child;
    }

    public Slot GetChild(int index)
    {
        if (index < 0 || index >= _children.Count)
            return null!;
        return _children[index];
    }

    public Slot this[int index] => GetChild(index);

    public Slot this[string name] => GetChild(name);

    public Slot GetChild(string name)
    {
        return _children.FirstOrDefault(c => c.Name.Value == name)!;
    }

    public Slot FindChild(string name, bool recursive = false, int maxDepth = -1)
    {
        return FindChild(s => s.Name.Value == name, recursive, maxDepth);
    }

    public Slot FindChild(Predicate<Slot> predicate, bool recursive = false, int maxDepth = -1)
    {
        foreach (var child in EnumerateAllChildren())
        {
            if (predicate(child))
                return child;

            if (recursive && maxDepth != 0)
            {
                var found = child.FindChild(predicate, true, maxDepth > 0 ? maxDepth - 1 : -1);
                if (found != null)
                    return found;
            }
        }
        return null!;
    }

    public IEnumerable<Slot> FindChildren(Predicate<Slot> predicate, bool recursive = false)
    {
        foreach (var child in EnumerateAllChildren())
        {
            if (predicate(child))
                yield return child;

            if (recursive)
            {
                foreach (var found in child.FindChildren(predicate, true))
                    yield return found;
            }
        }
    }

    public IEnumerable<Slot> FindChildrenByTag(string tag, bool recursive = false)
    {
        return FindChildren(s => s.Tag.Value == tag, recursive);
    }

    public void TagHierarchy(string tag)
    {
        Tag.Value = tag;
        foreach (var child in EnumerateAllChildren())
            child.TagHierarchy(tag);
    }

    public List<Slot> GetChildrenWithTag(string tag)
    {
        var result = new List<Slot>();
        GetChildrenWithTag(tag, result);
        return result;
    }

    public void GetChildrenWithTag(string tag, List<Slot> children)
    {
        foreach (var child in EnumerateAllChildren())
        {
            if (child.Tag.Value == tag)
                children.Add(child);
            child.GetChildrenWithTag(tag, children);
        }
    }

    public Slot FindChildOrAdd(string name, bool recursive = false)
    {
        var found = FindChild(name, recursive);
        return found ?? AddSlot(name);
    }

    public Slot GetSlotAtPath(string path, bool createIfMissing = false)
    {
        if (string.IsNullOrEmpty(path))
            return this;

        var parts = path.Split('/');
        var current = this;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || part == ".")
                continue;

            if (part == "..")
            {
                current = current._parent ?? current;
                continue;
            }

            var child = current.GetChild(part);
            if (child == null)
            {
                if (!createIfMissing)
                    return null!;
                child = current.AddSlot(part);
            }
            current = child;
        }

        return current;
    }

    public void ForeachChild(Action<Slot> action)
    {
        foreach (var child in EnumerateAllChildren())
            action(child);
    }

    public void ForeachChildRecursive(Action<Slot> action)
    {
        foreach (var child in EnumerateAllChildren())
        {
            action(child);
            child.ForeachChildRecursive(action);
        }
    }

    public void MoveChildToIndex(Slot child, int index)
    {
        if (!_children.Contains(child))
            return;

        _children.Remove(child);
        index = System.Math.Clamp(index, 0, _children.Count);
        _children.Insert(index, child);
    }

    public void InsertAtIndex(int index)
    {
        _parent?.MoveChildToIndex(this, index);
    }

    public static void SwapChildren(Slot a, Slot b)
    {
        if (a == null || b == null || ReferenceEquals(a, b))
            return;
        var parent = a._parent;
        if (parent == null || !ReferenceEquals(parent, b._parent))
            return;

        int indexA = a.SiblingIndex;
        int indexB = b.SiblingIndex;
        // Move the lower-indexed slot's target first; each MoveChildToIndex re-clamps
        // against the current list, so doing them in this order lands both correctly.
        if (indexA < indexB)
        {
            parent.MoveChildToIndex(b, indexA);
            parent.MoveChildToIndex(a, indexB);
        }
        else
        {
            parent.MoveChildToIndex(a, indexB);
            parent.MoveChildToIndex(b, indexA);
        }
    }

    public Slot InsertSlot(int index, string name = "Slot")
    {
        var slot = AddSlot(name);
        MoveChildToIndex(slot, index);
        return slot;
    }

    public void SortChildren()
    {
        _children.Sort((a, b) => a.OrderOffset.Value.CompareTo(b.OrderOffset.Value));
    }

    public void SortChildrenByName()
    {
        _children.Sort((a, b) => string.Compare(a.Name.Value, b.Name.Value, StringComparison.Ordinal));
    }

    public IEnumerable<Slot> GetAncestors(bool includeSelf = false)
    {
        if (includeSelf)
            yield return this;

        var current = _parent;
        while (current != null)
        {
            yield return current;
            current = current._parent;
        }
    }

    public IEnumerable<Slot> GetDescendants(bool includeSelf = false)
    {
        if (includeSelf)
            yield return this;

        foreach (var child in EnumerateAllChildren())
        {
            yield return child;
            foreach (var desc in child.GetDescendants(false))
                yield return desc;
        }
    }

    public bool IsDescendantOf(Slot slot)
    {
        if (slot == null) return false;
        var current = _parent;
        while (current != null)
        {
            if (current == slot)
                return true;
            current = current._parent;
        }
        return false;
    }

    public bool IsAncestorOf(Slot slot)
    {
        return slot?.IsDescendantOf(this) ?? false;
    }

    public Slot FindCommonAncestor(Slot other)
    {
        if (other == null) return null!;

        var ancestors = new HashSet<Slot>(GetAncestors(true));
        foreach (var ancestor in other.GetAncestors(true))
        {
            if (ancestors.Contains(ancestor))
                return ancestor;
        }
        return null!;
    }

    public int CountDescendants()
    {
        int count = _children.Count + _localChildren.Count;
        foreach (var child in _children)
            count += child.CountDescendants();
        foreach (var child in _localChildren)
            count += child.CountDescendants();
        return count;
    }

    #endregion

    #region Duplication

    public Slot Duplicate(Slot newParent = null!, bool preserveGlobalTransform = false)
    {
        newParent ??= _parent;
        if (newParent == null || World == null)
            return null!;
        if (IsRootSlot)
            return null!;
        if (newParent == this || newParent.IsDescendantOf(this))
            return null!;

        // Three phases:
        //  1. Clone the slot/component tree and map every source element's RefID
        //     to its clone.
        //  2. Copy member values - leaves copy immediately; every member registers
        //     its RefID in the map and every reference is deferred (a reference
        //     can't bind before its target's clone exists).
        //  3. Transfer references against the now-complete map, so a ref that
        //     pointed inside the source tree points at its clone instead of the
        //     original. This is what makes the copy a self-contained instance -
        //     drives, color drivers and any field-targeting ref follow the clone.
        var elementMap = new Dictionary<RefID, RefID>();
        var slotPairs = new List<(Slot source, Slot clone)>();
        var componentPairs = new List<(Component source, Component target)>();
        var deferredRefs = new List<(ISyncRef cloneRef, ISyncRef sourceRef)>();

        var clone = DuplicateStructure(newParent, elementMap, slotPairs, componentPairs);
        if (clone == null)
            return null!;

        foreach (var (source, target) in slotPairs)
            CopySlotMembers(source, target, elementMap, deferredRefs);

        foreach (var (source, target) in componentPairs)
            CopyComponentData(source, target, elementMap, deferredRefs);

        foreach (var (cloneRef, sourceRef) in deferredRefs)
            TransferReference(cloneRef, sourceRef, elementMap);

        if (preserveGlobalTransform)
        {
            clone.GlobalPosition = GlobalPosition;
            clone.GlobalRotation = GlobalRotation;
            clone.GlobalScale = GlobalScale;
        }

        // Post-fixup hook: runs after all data and references are in place so
        // components can rebind runtime state.
        foreach (var (_, target) in componentPairs)
        {
            try
            {
                target.OnDuplicate();
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"OnDuplicate failed for {target.GetType().Name}: {ex.Message}");
            }
        }

        return clone;
    }

    private Slot? DuplicateStructure(
        Slot newParent,
        Dictionary<RefID, RefID> elementMap,
        List<(Slot source, Slot clone)> slotPairs,
        List<(Component source, Component target)> componentPairs)
    {
        // An active user's root - their body, tracking and input rig - must
        // never be duplicated.
        if (ActiveUserRoot != null && ActiveUserRoot.Slot == this)
            return null;

        var clone = newParent.AddSlot(Name.Value);
        elementMap[ReferenceID] = clone.ReferenceID;
        slotPairs.Add((this, clone));

        foreach (var component in _components)
        {
            if (component.DontDuplicate)
                continue;

            // Attach behaviors stay off for clones: the copied data IS the
            // state, and OnAttach side effects (auto-attached helpers, default
            // setup) would fight it.
            var cloneComp = clone.AttachComponent(component.GetType(), runOnAttachBehavior: false);
            elementMap[component.ReferenceID] = cloneComp.ReferenceID;
            componentPairs.Add((component, cloneComp));
        }

        foreach (var child in _children)
        {
            child.DuplicateStructure(clone, elementMap, slotPairs, componentPairs);
        }

        return clone;
    }

    // All slot-level sync members copied generically (transform, name, tag,
    // active state, and anything added later); only hierarchy plumbing is
    // excluded.
    private void CopySlotMembers(Slot source, Slot clone, Dictionary<RefID, RefID> elementMap,
        List<(ISyncRef cloneRef, ISyncRef sourceRef)> deferredRefs)
    {
        foreach (var field in typeof(Slot).GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .Where(f => typeof(ISyncMember).IsAssignableFrom(f.FieldType)))
        {
            if (field.Name == nameof(ParentSlotRef))
                continue;
            try
            {
                CopyMemberPair(field.GetValue(source) as ISyncMember, field.GetValue(clone) as ISyncMember, elementMap, deferredRefs);
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"Failed to copy slot member {field.Name}: {ex.Message}");
            }
        }

        foreach (var prop in typeof(Slot).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => typeof(ISyncMember).IsAssignableFrom(p.PropertyType) && p.GetIndexParameters().Length == 0))
        {
            try
            {
                CopyMemberPair(prop.GetValue(source) as ISyncMember, prop.GetValue(clone) as ISyncMember, elementMap, deferredRefs);
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"Failed to copy slot member {prop.Name}: {ex.Message}");
            }
        }
    }

    // Cached per-type member layout for duplication. Reflecting GetProperties/
    // GetFields + LINQ filtering on every component of every duplicate was the
    // main duplication cost; resolve the member list once per type and reuse it.
    private sealed class CopyPlan
    {
        public MemberInfo[] SyncMembers = Array.Empty<MemberInfo>();
        public MemberInfo[] RefLists = Array.Empty<MemberInfo>();
    }

    private static readonly Dictionary<Type, CopyPlan> _copyPlans = new();

    private static CopyPlan GetCopyPlan(Type type)
    {
        if (_copyPlans.TryGetValue(type, out var cached))
            return cached;

        var sync = new List<MemberInfo>();
        var refLists = new List<MemberInfo>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length != 0)
                continue;
            if (prop.GetCustomAttribute<DontCopyAttribute>() != null)
                continue;
            if (typeof(ISyncMember).IsAssignableFrom(prop.PropertyType))
                sync.Add(prop);
            else if (IsSyncRefList(prop.PropertyType))
                refLists.Add(prop);
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.GetCustomAttribute<DontCopyAttribute>() != null)
                continue;
            if (typeof(ISyncMember).IsAssignableFrom(field.FieldType))
                sync.Add(field);
            else if (IsSyncRefList(field.FieldType))
                refLists.Add(field);
        }

        var plan = new CopyPlan { SyncMembers = sync.ToArray(), RefLists = refLists.ToArray() };
        _copyPlans[type] = plan;
        return plan;
    }

    private static object? GetMemberValue(MemberInfo member, object instance)
        => member is PropertyInfo p ? p.GetValue(instance) : ((FieldInfo)member).GetValue(instance);

    private void CopyComponentData(Component source, Component target, Dictionary<RefID, RefID> elementMap,
        List<(ISyncRef cloneRef, ISyncRef sourceRef)> deferredRefs)
    {
        var plan = GetCopyPlan(source.GetType());

        foreach (var member in plan.SyncMembers)
        {
            try
            {
                CopyMemberPair(GetMemberValue(member, source) as ISyncMember, GetMemberValue(member, target) as ISyncMember, elementMap, deferredRefs);
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"Failed to copy member {member.Name}: {ex.Message}");
            }
        }

        // SyncRefList<T> predates the sync-member contract, so it isn't an
        // ISyncMember - copy it explicitly or bone/avatar lists vanish on duplicate.
        foreach (var member in plan.RefLists)
        {
            try
            {
                CopySyncRefList(GetMemberValue(member, source), GetMemberValue(member, target), elementMap);
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"Failed to copy ref list {member.Name}: {ex.Message}");
            }
        }
    }

    private static bool IsSyncRefList(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SyncRefList<>);

    private void CopySyncRefList(object? sourceList, object? targetList, Dictionary<RefID, RefID> refMap)
    {
        if (sourceList == null || targetList == null)
            return;
        if (sourceList is not System.Collections.IEnumerable sourceElements)
            return;

        var addMethod = targetList.GetType().GetMethod("Add");
        if (addMethod == null)
            return;

        foreach (var element in sourceElements)
        {
            object? resolved = element;
            // Elements pointing inside the duplicated tree follow their clones;
            // external elements stay shared with the original.
            if (element is IWorldElement worldElement
                && refMap.TryGetValue(worldElement.ReferenceID, out var remapped))
            {
                resolved = World?.FindElement(remapped) ?? element;
            }
            addMethod.Invoke(targetList, new[] { resolved });
        }
    }

    private void CopyMemberPair(ISyncMember? sourceMember, ISyncMember? targetMember,
        Dictionary<RefID, RefID> elementMap, List<(ISyncRef cloneRef, ISyncRef sourceRef)> deferredRefs)
    {
        if (sourceMember != null && targetMember != null)
        {
            CopySyncMemberValue(sourceMember, targetMember, elementMap, deferredRefs);
        }
    }

    // Every member registers its RefID in the map so references aimed at it can be remapped; references
    // themselves are deferred and resolved once the whole tree exists.
    private void CopySyncMemberValue(ISyncMember source, ISyncMember target,
        Dictionary<RefID, RefID> elementMap, List<(ISyncRef cloneRef, ISyncRef sourceRef)> deferredRefs)
    {
        var sourceType = source.GetType();
        var targetType = target.GetType();

        if (sourceType != targetType) return;

        // Register this member (field, ref, or list element) so any reference in
        // the tree that targets it can be relocated to the clone's member. Without
        // this a ref aimed at a field stays bound to the original's field.
        elementMap[source.ReferenceID] = target.ReferenceID;

        // References (SyncRef and subclasses like AssetRef) are deferred, not bound
        // here: the target's clone may not exist yet. The transfer phase resolves
        // them against the complete map.
        if (source is ISyncRef sourceRef && target is ISyncRef targetRef)
        {
            // Delegates carry a method name (and possibly a static type) beside the
            // target; copy that now so the clone keeps the same handler once its
            // target is remapped in the transfer phase.
            if (source is ISyncDelegate sourceDelegate && target is ISyncDelegate targetDelegate)
                targetDelegate.SetMethod(sourceDelegate.MethodName, sourceDelegate.StaticType);

            deferredRefs.Add((targetRef, sourceRef));
            return;
        }

        // Lists (SyncList/SyncAssetList): grow the clone's list and copy
        // element-wise, recursing so elements register and nested refs defer.
        if (source is ISyncList sourceList && target is ISyncList targetList)
        {
            int count = sourceList.Count;
            for (int i = 0; i < count; i++)
            {
                var sourceElement = sourceList.GetElement(i);
                var targetElement = i < targetList.Count ? targetList.GetElement(i) : targetList.AddElement();
                if (sourceElement != null && targetElement != null)
                {
                    CopySyncMemberValue(sourceElement, targetElement, elementMap, deferredRefs);
                }
            }
            return;
        }

        // Flat value collections and composite entries have no Value property and no elements to walk,
        // so they copy themselves; the callback keeps nested references on the deferred path.
        if (source is ISyncMemberCopy && target is ISyncMemberCopy targetCopy)
        {
            targetCopy.CopyFromSource(source, (s, t) => CopySyncMemberValue(s, t, elementMap, deferredRefs));
            return;
        }

        var sourceVal = source.GetValueAsObject();
        if (sourceVal != null)
        {
            var valueProperty = targetType.GetProperty("Value");
            if (valueProperty != null && valueProperty.CanWrite)
            {
                try
                {
                    valueProperty.SetValue(target, sourceVal);
                }
                catch
                {
                }
            }
        }
    }

    // Resolve a deferred reference now that the whole clone tree and its member
    // map exist. A ref whose target lived inside the source tree points at the
    // clone of that target; an external non-link ref stays shared with the
    // original (materials, fonts); an external link is left null - a forked link
    // would fight the original over the same target.
    private void TransferReference(ISyncRef cloneRef, ISyncRef sourceRef, Dictionary<RefID, RefID> elementMap)
    {
        var sourceTarget = sourceRef.Value;
        if (sourceTarget == RefID.Null)
            return;

        if (elementMap.TryGetValue(sourceTarget, out var clonedTarget))
            cloneRef.Value = clonedTarget;
        else if (sourceRef is not ILinkRef)
            cloneRef.Value = sourceTarget;
    }

    public Slot DeepCopy(Slot newParent = null!)
    {
        return Duplicate(newParent, false);
    }

    #endregion

    #region Path & Hierarchy

    public string GetPath()
    {
        if (_parent == null)
            return Name.Value;
        return _parent.GetPath() + "/" + Name.Value;
    }

    public string GetRelativePath(Slot relativeTo)
    {
        if (relativeTo == null || relativeTo == this)
            return ".";

        if (IsDescendantOf(relativeTo))
        {
            var path = new List<string>();
            var current = this;
            while (current != relativeTo)
            {
                path.Insert(0, current.Name.Value);
                current = current._parent;
            }
            return string.Join("/", path);
        }

        var ancestor = FindCommonAncestor(relativeTo);
        if (ancestor == null)
            return GetPath();

        var upCount = 0;
        var check = relativeTo;
        while (check != ancestor)
        {
            upCount++;
            check = check._parent;
        }

        var relativePath = GetRelativePath(ancestor);
        var ups = string.Join("/", Enumerable.Repeat("..", upCount));
        return ups + "/" + relativePath;
    }

    public override string ParentHierarchyToString()
    {
        return GetPath();
    }

    public string PrintHierarchy(int indent = 0)
    {
        var sb = new System.Text.StringBuilder();
        var prefix = new string(' ', indent * 2);
        sb.AppendLine($"{prefix}{Name.Value} [{_components.Count} components]");
        foreach (var child in _children)
        {
            sb.Append(child.PrintHierarchy(indent + 1));
        }
        foreach (var child in _localChildren)
        {
            sb.Append(child.PrintHierarchy(indent + 1));
        }
        return sb.ToString();
    }

    #endregion

    #region Destroy & Cleanup

    public void Destroy()
    {
        if (IsDestroyed) return;
        if (IsProtected)
        {
            Logging.Logger.Warn($"Slot.Destroy: refusing to destroy protected slot '{Name.Value}' (RefID {ReferenceID})");
            return;
        }

        IsDestroyed = true;
        OnPrepareDestroy?.Invoke(this);

        foreach (var child in _children.ToArray())
            child.Destroy();
        foreach (var child in _localChildren.ToArray())
            child.Destroy();

			foreach (var component in _components.ToArray())
			{
				RemoveComponent(component);
			}

		_children.Clear();
		_localChildren.Clear();

        _parent?.DetachChildInternal(this);

        World?.ReferenceController?.UnregisterObject(this);
        World?.UnregisterSlot(this);

        Hook?.Dispose();
    }

    // Surviving providers keep their slots, which get reparented under relocateAssets (created under the world
    // root as "<name> - Assets" when null, the same shape the object loaders use); everything else is
    // destroyed.
    public void DestroyPreservingAssets(Slot? relocateAssets = null, ISlotStripRecorder? recorder = null)
    {
        if (IsDestroyed || IsProtected || World == null)
            return;
        if (relocateAssets != null && (relocateAssets == this || relocateAssets.IsDescendantOf(this)))
            return;

        var hierarchy = new HashSet<Slot>();
        GenerateHierarchy(hierarchy);

        Slot? target = relocateAssets;
        Slot GetRelocationTarget()
        {
            if (target != null)
                return target;
            target = World.RootSlot.AddSlot(Name.Value + " - Assets");
            recorder?.HolderCreated(target);
            return target;
        }

        var externallyReferenced = new Dictionary<IAssetProvider, bool>();
        StripNonAssetComponents(hierarchy, externallyReferenced, recorder);
        RelocateOrDestroyEmpty(GetRelocationTarget, recorder);
    }

    // Bottom-up: drop every component that is not an externally-referenced asset provider.
    // Support components marked PreserveWithAssets ride along only next to a surviving provider.
    private void StripNonAssetComponents(HashSet<Slot> hierarchy, Dictionary<IAssetProvider, bool> memo,
        ISlotStripRecorder? recorder)
    {
        for (int i = _children.Count - 1; i >= 0; i--)
            _children[i].StripNonAssetComponents(hierarchy, memo, recorder);
        for (int i = _localChildren.Count - 1; i >= 0; i--)
            _localChildren[i].StripNonAssetComponents(hierarchy, memo, recorder);

        for (int i = _components.Count - 1; i >= 0; i--)
        {
            var component = _components[i];
            if (component.IsDestroyed)
                continue;
            if (component.PreserveWithAssets)
                continue;
            if (component is IAssetProvider provider && IsExternallyReferenced(provider, hierarchy, memo))
                continue;
            recorder?.ComponentStripped(component);
            component.Destroy();
        }

        bool hasProvider = false;
        foreach (var component in _components)
        {
            if (component is IAssetProvider)
            {
                hasProvider = true;
                break;
            }
        }
        if (!hasProvider)
        {
            for (int i = _components.Count - 1; i >= 0; i--)
            {
                if (_components[i].PreserveWithAssets)
                {
                    recorder?.ComponentStripped(_components[i]);
                    _components[i].Destroy();
                }
            }
        }
    }

    // Bottom-up: slots stripped bare are destroyed; slots that kept components move (flattened)
    // under the relocation target. A recorder can claim an emptied slot instead of losing it.
    private void RelocateOrDestroyEmpty(Func<Slot> getRelocationTarget, ISlotStripRecorder? recorder)
    {
        for (int i = _children.Count - 1; i >= 0; i--)
            _children[i].RelocateOrDestroyEmpty(getRelocationTarget, recorder);
        for (int i = _localChildren.Count - 1; i >= 0; i--)
            _localChildren[i].RelocateOrDestroyEmpty(getRelocationTarget, recorder);

        if (_components.Count == 0)
        {
            if (recorder?.SlotEmptied(this) != true)
                Destroy();
            return;
        }

        var holder = getRelocationTarget();
        recorder?.SlotRelocating(this, holder);
        SetParent(holder);
    }

    // A provider survives when a reference to it lives outside the doomed subtree, directly or
    // through a chain of providers (a mesh referenced by a preserved material is preserved too).
    // The memo doubles as the cycle guard: a provider currently being evaluated reads as
    // not-external, so reference loops cannot recurse forever. - xlinka
    private static bool IsExternallyReferenced(IAssetProvider provider, HashSet<Slot> hierarchy,
        Dictionary<IAssetProvider, bool> memo)
    {
        if (memo.TryGetValue(provider, out bool cached))
            return cached;
        memo[provider] = false;

        bool external = false;
        foreach (var reference in provider.References)
        {
            var element = reference as IWorldElement;
            var owner = FindOwningSlot(element);
            if (owner == null || owner.IsDestroyed)
                continue;
            if (!hierarchy.Contains(owner))
            {
                external = true;
                break;
            }
            var owningProvider = FindOwningProvider(element);
            if (owningProvider != null && !ReferenceEquals(owningProvider, provider)
                && IsExternallyReferenced(owningProvider, hierarchy, memo))
            {
                external = true;
                break;
            }
        }
        memo[provider] = external;
        return external;
    }

    private static Slot? FindOwningSlot(IWorldElement? element)
    {
        while (element != null)
        {
            if (element is Slot slot)
                return slot;
            if (element is Component component)
                return component.Slot;
            element = (element as SyncElement)?.Parent;
        }
        return null;
    }

    private static IAssetProvider? FindOwningProvider(IWorldElement? element)
    {
        while (element != null)
        {
            if (element is Component component)
                return component as IAssetProvider;
            element = (element as SyncElement)?.Parent;
        }
        return null;
    }

    public void RemoveFromHierarchy()
    {
        if (IsProtected)
        {
            Logging.Logger.Warn($"Slot.RemoveFromHierarchy: refusing on protected slot '{Name.Value}' (RefID {ReferenceID})");
            return;
        }

        _isRemoved = true;
        _parent?.DetachChildInternal(this);
        _parent = null!;
    }

    // Protected children are skipped; their Destroy() no-ops with a warning.
    public void DestroyChildren()
    {
        foreach (var child in _children.ToArray())
            child.Destroy();
        foreach (var child in _localChildren.ToArray())
            child.Destroy();
    }

    public void DestroyChildren(Predicate<Slot> predicate)
    {
        foreach (var child in _children.ToArray())
        {
            if (predicate(child))
                child.Destroy();
        }
        foreach (var child in _localChildren.ToArray())
        {
            if (predicate(child))
                child.Destroy();
        }
    }

    public void DestroyDelayed(float seconds)
    {
        int updates = (int)(seconds * 60); // Approximate 60 fps
        RunInUpdates(updates, Destroy);
    }

    #endregion

    #region Update

    public void UpdateComponents(float delta)
    {
        ProcessScheduledActions();

        foreach (var component in _components)
        {
            if (component.Enabled.Value)
                component.OnUpdate(delta);
        }
    }

    private void OnChanged()
    {
        QueueHookUpdate();
        Changed?.Invoke(this);
    }

    private void QueueHookUpdate()
    {
        if (Hook == null || World == null || IsDestroyed)
        {
            return;
        }

        World.UpdateManager?.RegisterHookUpdate(this);
    }

    #endregion

    #region Network Serialization

    public void Encode(BinaryWriter writer)
    {
        writer.Write((ulong)ReferenceID);
        writer.Write((ulong)(_parent?.ReferenceID ?? RefID.Null));

        EncodeSyncField(writer, Name);
        EncodeSyncField(writer, ActiveSelf);
        EncodeSyncField(writer, LocalPosition);
        EncodeSyncField(writer, LocalRotation);
        EncodeSyncField(writer, LocalScale);
        EncodeSyncField(writer, Tag);
        EncodeSyncField(writer, Persistent);
        EncodeSyncField(writer, OrderOffset);

        writer.Write(_components.Count);
        foreach (var comp in _components)
        {
            writer.Write(comp.GetType().AssemblyQualifiedName ?? comp.GetType().FullName ?? "");
            writer.Write((ulong)comp.ReferenceID);
            comp.Encode(writer);
        }

        // Write children count (children encoded separately)
        writer.Write(_children.Count);
        foreach (var child in _children)
        {
            writer.Write((ulong)child.ReferenceID);
        }
    }

    public void Decode(BinaryReader reader, Dictionary<RefID, object> refLookup)
    {
        var refId = new RefID(reader.ReadUInt64());
        var parentRefId = new RefID(reader.ReadUInt64());

        DecodeSyncField(reader, Name);
        DecodeSyncField(reader, ActiveSelf);
        DecodeSyncField(reader, LocalPosition);
        DecodeSyncField(reader, LocalRotation);
        DecodeSyncField(reader, LocalScale);
        DecodeSyncField(reader, Tag);
        DecodeSyncField(reader, Persistent);
        DecodeSyncField(reader, OrderOffset);

        int compCount = reader.ReadInt32();
        for (int i = 0; i < compCount; i++)
        {
            var typeName = reader.ReadString();
            var compRefId = new RefID(reader.ReadUInt64());

            var type = Type.GetType(typeName);
            if (type != null)
            {
                // Decoded data is the component's state; attach behaviors would
                // overwrite it with defaults and auto-attach duplicate helpers.
                var comp = AttachComponent(type, runOnAttachBehavior: false);
                comp.Decode(reader);
                refLookup[compRefId] = comp;
            }
        }

        // Read children refs (resolved after all slots decoded)
        int childCount = reader.ReadInt32();
        for (int i = 0; i < childCount; i++)
        {
            var childRefId = new RefID(reader.ReadUInt64());
            // Children are resolved in a second pass
        }
    }

    private void EncodeSyncField<T>(BinaryWriter writer, Sync<T> field)
    {
        field?.Encode(writer);
    }

    private void DecodeSyncField<T>(BinaryReader reader, Sync<T> field)
    {
        field?.Decode(reader);
    }

    #endregion

    #region Binary Serialization (File Save/Load)

    public void SaveToBinary(BinaryWriter writer)
    {
        writer.Write("SLOT"); // Magic
        writer.Write(1); // Version

        writer.Write(Name.Value ?? "");
        writer.Write(ActiveSelf.Value);
        writer.Write(Tag.Value ?? "");
        writer.Write(Persistent.Value);
        writer.Write(OrderOffset.Value);

        WriteFloat3(writer, LocalPosition.Value);
        WriteFloatQ(writer, LocalRotation.Value);
        WriteFloat3(writer, LocalScale.Value);

        writer.Write(_components.Count);
        foreach (var comp in _components)
        {
            writer.Write(comp.GetType().AssemblyQualifiedName ?? comp.GetType().FullName ?? "");
            SaveComponentToBinary(writer, comp);
        }

        writer.Write(_children.Count);
        foreach (var child in _children)
            child.SaveToBinary(writer);
    }

    public void LoadFromBinary(BinaryReader reader)
    {
        var magic = reader.ReadString();
        if (magic != "SLOT")
            throw new InvalidDataException("Invalid slot data format");

        var version = reader.ReadInt32();
        if (version > 1)
            throw new InvalidDataException($"Unsupported slot version: {version}");

        Name.Value = reader.ReadString();
        ActiveSelf.Value = reader.ReadBoolean();
        Tag.Value = reader.ReadString();
        Persistent.Value = reader.ReadBoolean();
        OrderOffset.Value = reader.ReadInt64();

        LocalPosition.Value = ReadFloat3(reader);
        LocalRotation.Value = ReadFloatQ(reader);
        LocalScale.Value = ReadFloat3(reader);

        int compCount = reader.ReadInt32();
        for (int i = 0; i < compCount; i++)
        {
            var typeName = reader.ReadString();
            var type = Type.GetType(typeName);
            if (type != null)
            {
                // Same as decode: loaded data is the state, attach behaviors off.
                var comp = AttachComponent(type, runOnAttachBehavior: false);
                LoadComponentFromBinary(reader, comp);
            }
            else
            {
                SkipComponentData(reader);
            }
        }

        int childCount = reader.ReadInt32();
        for (int i = 0; i < childCount; i++)
        {
            var child = AddSlot();
            child.LoadFromBinary(reader);
        }
    }

    private void SaveComponentToBinary(BinaryWriter writer, Component comp)
    {
        var type = comp.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ISyncMember).IsAssignableFrom(p.PropertyType))
            .ToArray();

        writer.Write(properties.Length);

        foreach (var prop in properties)
        {
            writer.Write(prop.Name);
            var syncMember = prop.GetValue(comp) as ISyncMember;
            if (syncMember != null)
            {
                var value = syncMember.GetValueAsObject();
                WriteValue(writer, value!);
            }
            else
            {
                WriteValue(writer, null!);
            }
        }
    }

    private void LoadComponentFromBinary(BinaryReader reader, Component comp)
    {
        var type = comp.GetType();
        int propCount = reader.ReadInt32();

        for (int i = 0; i < propCount; i++)
        {
            var propName = reader.ReadString();
            var value = ReadValue(reader);

            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && typeof(ISyncMember).IsAssignableFrom(prop.PropertyType))
            {
                var syncMember = prop.GetValue(comp) as ISyncMember;
                if (syncMember != null)
                {
                    SetSyncMemberValue(syncMember, value);
                }
            }
        }
    }

    private void SkipComponentData(BinaryReader reader)
    {
        int propCount = reader.ReadInt32();
        for (int i = 0; i < propCount; i++)
        {
            reader.ReadString(); // Property name
            ReadValue(reader); // Value (discarded)
        }
    }

    private void SetSyncMemberValue(ISyncMember member, object value)
    {
        if (value == null) return;

        var type = member.GetType();
        var valueProp = type.GetProperty("Value");
        if (valueProp != null && valueProp.CanWrite)
        {
            try
            {
                var convertedValue = Convert.ChangeType(value, valueProp.PropertyType);
                valueProp.SetValue(member, convertedValue);
            }
            catch
            {
                try
                {
                    valueProp.SetValue(member, value);
                }
                catch
                {
                }
            }
        }
    }

    private void WriteValue(BinaryWriter writer, object value)
    {
        if (value == null)
        {
            writer.Write((byte)0); // Null marker
            return;
        }

        var type = value.GetType();

        if (type == typeof(bool))
        {
            writer.Write((byte)1);
            writer.Write((bool)value);
        }
        else if (type == typeof(int))
        {
            writer.Write((byte)2);
            writer.Write((int)value);
        }
        else if (type == typeof(float))
        {
            writer.Write((byte)3);
            writer.Write((float)value);
        }
        else if (type == typeof(double))
        {
            writer.Write((byte)4);
            writer.Write((double)value);
        }
        else if (type == typeof(string))
        {
            writer.Write((byte)5);
            writer.Write((string)value ?? "");
        }
        else if (type == typeof(float3))
        {
            writer.Write((byte)6);
            WriteFloat3(writer, (float3)value);
        }
        else if (type == typeof(floatQ))
        {
            writer.Write((byte)7);
            WriteFloatQ(writer, (floatQ)value);
        }
        else if (type == typeof(float2))
        {
            writer.Write((byte)8);
            var v = (float2)value;
            writer.Write(v.x);
            writer.Write(v.y);
        }
        else if (type == typeof(float4))
        {
            writer.Write((byte)9);
            var v = (float4)value;
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
            writer.Write(v.w);
        }
        else if (type == typeof(color))
        {
            writer.Write((byte)10);
            var c = (color)value;
            writer.Write(c.r);
            writer.Write(c.g);
            writer.Write(c.b);
            writer.Write(c.a);
        }
        else if (type == typeof(long))
        {
            writer.Write((byte)11);
            writer.Write((long)value);
        }
        else if (type == typeof(RefID))
        {
            writer.Write((byte)12);
            writer.Write((ulong)(RefID)value);
        }
        else if (type.IsEnum)
        {
            writer.Write((byte)13);
            writer.Write(type.AssemblyQualifiedName ?? type.FullName ?? "");
            writer.Write(Convert.ToInt32(value));
        }
        else
        {
            writer.Write((byte)255);
            writer.Write(value.ToString() ?? "");
        }
    }

    private object ReadValue(BinaryReader reader)
    {
        var typeCode = reader.ReadByte();

        return typeCode switch
        {
            0 => null!,
            1 => reader.ReadBoolean(),
            2 => reader.ReadInt32(),
            3 => reader.ReadSingle(),
            4 => reader.ReadDouble(),
            5 => reader.ReadString(),
            6 => ReadFloat3(reader),
            7 => ReadFloatQ(reader),
            8 => new float2(reader.ReadSingle(), reader.ReadSingle()),
            9 => new float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            10 => new color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            11 => reader.ReadInt64(),
            12 => new RefID(reader.ReadUInt64()),
            13 => ReadEnumValue(reader),
            255 => reader.ReadString(), // String fallback
            _ => null!
        };
    }

    private object ReadEnumValue(BinaryReader reader)
    {
        var typeName = reader.ReadString();
        var intValue = reader.ReadInt32();

        var type = Type.GetType(typeName);
        if (type != null && type.IsEnum)
        {
            return Enum.ToObject(type, intValue);
        }
        return intValue;
    }

    private static void WriteFloat3(BinaryWriter writer, float3 v)
    {
        writer.Write(v.x);
        writer.Write(v.y);
        writer.Write(v.z);
    }

    private static float3 ReadFloat3(BinaryReader reader)
    {
        return new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static void WriteFloatQ(BinaryWriter writer, floatQ q)
    {
        writer.Write(q.x);
        writer.Write(q.y);
        writer.Write(q.z);
        writer.Write(q.w);
    }

    private static floatQ ReadFloatQ(BinaryReader reader)
    {
        return new floatQ(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    #endregion

    #region ToString

    public override string ToString()
    {
        return $"Slot({Name.Value}, RefID={ReferenceID}, Children={_children.Count}, Components={_components.Count})";
    }

    #endregion
}
