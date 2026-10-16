// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// Base class for every physics collider. The platform hook builds the real collision shape from the
// typed sync fields; this side owns the data model and the registry entry.
[ComponentCategory("Physics/Colliders")]
public abstract class Collider : ImplementableComponent
{
    // SYNC FIELDS

    public readonly Sync<float3> Offset;
    public readonly Sync<ColliderType> Type;
    public readonly Sync<float> Mass;
    // Does a walking body stop against this, or pass through it?
    //
    // This existed, replicated, appeared in the inspector and was read by NOTHING. Every collider in
    // a world went onto one collision bit and the character's capsule masked that same bit, so a
    // walking body stopped against every collider in the world without exception - including the
    // mesh colliders the importer hangs off an imported model, which is why importing a chair gave
    // you a chair-shaped wall. The flag that was supposed to decide it had never been wired.
    //
    // It is true by default on purpose. There are seventy-six places in this codebase that attach a
    // collider, most of them floors, walls and props in the world templates, and a false default would
    // drop everybody through the ground until all seventy-six were audited. So the default keeps every
    // existing collider behaving exactly as it does now, and the model importer - the one place that was
    // demonstrably wrong - turns it off for the geometry it generates. -xlinka
    public readonly Sync<bool> CharacterCollider;
    public readonly Sync<bool> IgnoreRaycasts;

    // STATE

    protected IColliderOwner _owner = null!;
    protected bool _shapeChanged = true;
    private int _ownerSearches;

    // The component that manages this collider, e.g. a CharacterController.
    public IColliderOwner ColliderOwner => _owner;

    // The rigid body whose simulated body carries this shape: the nearest one at or above this slot, for a
    // solid collider. Null leaves the collider a body of its own.
    public RigidBody? Body
    {
        get => _body;
        internal set
        {
            _body = value;
            WatchMoves();
        }
    }

    private RigidBody? _body;

    // Something rests on it, pushes against it, or is pushed by it. Triggers and NoCollision only answer
    // queries, and a character collider belongs to its controller.
    public bool IsSolid => Type.Value is ColliderType.Static or ColliderType.Active;

    // Local bounds offset after the owner has post-processed it.
    public float3 LocalBoundsOffset
    {
        get
        {
            float3 offset = Offset.Value;
            _owner?.PostprocessBoundsOffset(ref offset);
            return offset;
        }
    }

    // INITIALIZATION

    public Collider()
    {
        Offset = new Sync<float3>(this, float3.Zero);
        Type = new Sync<ColliderType>(this, ColliderType.Static);
        Mass = new Sync<float>(this, 1.0f);
        CharacterCollider = new Sync<bool>(this, true);
        IgnoreRaycasts = new Sync<bool>(this, false);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        // Join this world's collider registry so raycasts don't have to walk the whole tree to find us. -xlinka
        World?.RegisterCollider(this);

        // Keep hook in sync when collider properties change
        Offset.OnChanged += _ => RunApplyChanges();
        Type.OnChanged += _ =>
        {
            // Solid or not decides which body carries it, so the rigid body above looks again.
            RigidBody.Above(Slot)?.MarkCollidersDirty();
            RunApplyChanges();
        };
        Mass.OnChanged += _ => RunApplyChanges();
        CharacterCollider.OnChanged += _ => RunApplyChanges();
        IgnoreRaycasts.OnChanged += _ => RunApplyChanges();
        // Re-sync whenever this slot's WORLD transform changes - its own local move, an ancestor
        // moving, or a scale change - coalesced to once per frame. (The old per-field local hooks
        // only caught this slot's own position/rotation; ancestor moves and scale were missed.)
        WatchMoves();
        // Its body hangs under the world root, not the slot, so a slot switched off stays solid and still
        // takes a laser until the hook looks again. -xlinka
        Slot.ActiveChanged += OnSlotActiveChanged;

        // A CharacterController collider tells the controller on its own slot (not a parent's) that it is
        // there; every other collider goes straight to the physics hook.
        SearchForOwner();
        RunApplyChanges();
    }

    // OWNER

    // The controller may not be awake yet, so the search is tried again over the next three updates. It
    // used to ride the per-frame update for that, which kept every collider on the dispatch until its
    // fourth update: never, for a collider on a switched-off slot, which is dispatched but not updated.
    // 65536 of those in one package cost most of a minute of empty calls. -xlinka
    private void SearchForOwner()
    {
        if (IsDestroyed || _owner != null || Type.Value != ColliderType.CharacterController)
            return;

        foreach (var component in Slot.Components)
        {
            if (component is IColliderOwner owner)
            {
                _owner = owner;
                _owner.OnColliderAdded(this);
                LumoraLogger.Debug($"Collider: found owner {_owner.GetType().Name} on its slot after {_ownerSearches} retries");
                return;
            }
        }

        if (_ownerSearches++ < 3)
            World?.RunInUpdates(1, SearchForOwner);
        else
            LumoraLogger.Warn($"Collider: No IColliderOwner found in same slot '{Slot.SlotName.Value}' for CharacterController collider");
    }

    private bool _watchingMoves;

    // A collider on its rigid body's own slot moves with the body by construction: the body carries the shape,
    // and the body's own hook hears of moves that come from outside it. Listening there anyway queued a notice
    // per body per physics step for nothing.
    private void WatchMoves()
    {
        bool watch = !IsDestroyed && Slot != null && !(_body != null && ReferenceEquals(_body.Slot, Slot));
        if (watch == _watchingMoves || Slot == null)
            return;
        _watchingMoves = watch;
        if (watch)
            Slot.WorldTransformChanged += OnSlotTransformChanged;
        else
            Slot.WorldTransformChanged -= OnSlotTransformChanged;
    }

    private void OnSlotTransformChanged(Slot _)
    {
        // A part of a body further down its model: the body re-poses the shape, the collider's own hook has no
        // body to move.
        if (_body != null && !_body.IsDestroyed)
            _body.MarkPartMoved();
        else
            RunApplyChanges();
    }

    // ABSTRACT METHODS

    // Shape-tight local bounding box, centred on Offset. The platform hook builds the actual collision
    // shape from the typed sync fields, so this is purely the engine-side answer used for culling,
    // fitting and overlap pre-tests.
    public abstract BoundingBox GetLocalBounds();

    private void OnSlotActiveChanged(Slot _) => RunApplyChanges();

    // CLEANUP

    public override void OnDestroy()
    {
        World?.UnregisterCollider(this);

        if (Slot != null)
        {
            if (_watchingMoves)
                Slot.WorldTransformChanged -= OnSlotTransformChanged;
            _watchingMoves = false;
            Slot.ActiveChanged -= OnSlotActiveChanged;
        }

        _owner?.OnColliderRemoved(this);
        _owner = null!;
        _body?.MarkCollidersDirty();
        _body = null;

        base.OnDestroy();
        LumoraLogger.Debug($"Collider: Destroyed {GetType().Name}");
    }
}

