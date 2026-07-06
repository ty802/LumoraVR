// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Rigid body physics component.
/// Enables physics simulation on a slot - responds to forces, gravity, and collisions.
/// Requires a Collider component on the same slot to define the collision shape.
/// </summary>
[ComponentCategory("Physics")]
public class RigidBody : ImplementableComponent
{
    // SYNC FIELDS

    /// <summary>Mass in kilograms</summary>
    public readonly Sync<float> Mass;

    /// <summary>Linear velocity</summary>
    public readonly Sync<float3> LinearVelocity;

    /// <summary>Angular velocity (radians per second)</summary>
    public readonly Sync<float3> AngularVelocity;

    /// <summary>Whether gravity affects this body</summary>
    public readonly Sync<bool> UseGravity;

    /// <summary>Whether the body is kinematic (driven by animation, not physics)</summary>
    public readonly Sync<bool> IsKinematic;

    /// <summary>Linear damping (drag)</summary>
    public readonly Sync<float> LinearDamping;

    /// <summary>Angular damping</summary>
    public readonly Sync<float> AngularDamping;

    /// <summary>Freeze position on specific axes</summary>
    public readonly Sync<bool> FreezePositionX;
    public readonly Sync<bool> FreezePositionY;
    public readonly Sync<bool> FreezePositionZ;

    /// <summary>Freeze rotation on specific axes</summary>
    public readonly Sync<bool> FreezeRotationX;
    public readonly Sync<bool> FreezeRotationY;
    public readonly Sync<bool> FreezeRotationZ;

    // RUNTIME STATE

    // These are LOCAL runtime state, not synced. Only the owner runs the simulation, so only the owner's
    // values are meaningful; a non-owner doesn't simulate and leaves them at their defaults. They are not
    // replicated - peers that need a body's sleep/collision state should drive off the synced velocity/pose
    // the owner writes, not these. If a future need requires them network-wide, promote to Sync fields the
    // owner writes. -xlinka

    /// <summary>Whether the body is sleeping (not moving). Owner-local; only valid on the simulating peer.</summary>
    public bool IsSleeping { get; set; }

    /// <summary>Whether the body is colliding with something. Owner-local; only valid on the simulating peer.</summary>
    public bool IsColliding { get; set; }

    // INITIALIZATION

    public RigidBody()
    {
        Mass = new Sync<float>(this, 1f);
        LinearVelocity = new Sync<float3>(this, float3.Zero);
        AngularVelocity = new Sync<float3>(this, float3.Zero);
        UseGravity = new Sync<bool>(this, true);
        IsKinematic = new Sync<bool>(this, false);
        LinearDamping = new Sync<float>(this, 0.05f);
        AngularDamping = new Sync<float>(this, 0.05f);
        FreezePositionX = new Sync<bool>(this, false);
        FreezePositionY = new Sync<bool>(this, false);
        FreezePositionZ = new Sync<bool>(this, false);
        FreezeRotationX = new Sync<bool>(this, false);
        FreezeRotationY = new Sync<bool>(this, false);
        FreezeRotationZ = new Sync<bool>(this, false);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        LumoraLogger.Log($"RigidBody: Initialized on '{Slot.SlotName.Value}' with Mass={Mass.Value}kg");
    }

    public override void OnInit()
    {
        base.OnInit();

        // Attach RespawnData here, NOT OnAwake. OnAwake runs inside the ReferenceController's allocation
        // soft-block, so creating a component (+ its sync members) there logs a "RefID allocation during
        // OnAwake" warning per member - 8 lines per body. OnInit runs after the block is released. -xlinka
        var respawnData = Slot.GetComponent<RespawnData>();
        if (respawnData == null)
        {
            respawnData = Slot.AttachComponent<RespawnData>();
            respawnData.StoreCurrentPosition();
        }
    }

    // AUTHORITY

    /// <summary>
    /// Whether THIS peer owns the simulation of this body. Exactly one peer integrates forces and replicates
    /// the resulting pose/velocity; every other peer follows that replicated transform instead of running its
    /// own divergent sim. Ownership is host-authoritative: a body parented under a user's root belongs to that
    /// user's peer; everything else is world content owned by the world authority (host). A body under a REMOTE
    /// user's root is owned by them, not us.
    /// </summary>
    public bool IsSimulationOwner
    {
        get
        {
            var world = World;
            if (world == null)
                return false;

            // Under a user's root -> that user's peer simulates it. IsUnderLocalUser is the structural,
            // reliable ownership signal (it tolerates the allocation-byte/ownership-link lag at join). -xlinka
            var userRoot = Slot?.ActiveUserRoot;
            if (userRoot != null)
                return Slot!.IsUnderLocalUser;

            // World content (no owning user): the world authority simulates it; clients follow.
            return world.IsAuthority;
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (IsSimulationOwner)
        {
            // Owner drives the sim: queue the hook so it integrates forces and writes the synced pose/velocity. -xlinka
            RunApplyChanges();
        }
        else
        {
            // Non-owner: never run a local sim that would fight the replicated transform. Drop any forces that
            // got queued locally so they can't accumulate and fire if ownership later transfers to us. The body
            // simply follows the Slot transform the owner replicates (SlotHook positions the visual from it). -xlinka
            ClearPendingForces();
        }
    }

    // Sync-change handler. The base queues the hook to ApplyChanges on every replicated field write; a non-owner
    // must NOT be driven that way (an incoming velocity/pose write would otherwise kick off a local sim that
    // diverges from the owner). Owners flush normally. The startup hook flush in ImplementableComponent.OnStart
    // is separate, so the body is still created on every peer. -xlinka
    public override void OnChanges()
    {
        if (IsSimulationOwner)
            base.OnChanges();
    }

    // FORCE METHODS

    /// <summary>
    /// Apply a force at the center of mass. No-op on a non-owner (forces are integrated only by the simulating peer).
    /// </summary>
    public void AddForce(float3 force)
    {
        if (!IsSimulationOwner)
            return;
        PendingForce += force;
    }

    /// <summary>
    /// Apply an impulse at the center of mass (instantaneous velocity change). No-op on a non-owner.
    /// </summary>
    public void AddImpulse(float3 impulse)
    {
        if (!IsSimulationOwner)
            return;
        PendingImpulse += impulse;
    }

    /// <summary>
    /// Apply torque to rotate the body. No-op on a non-owner.
    /// </summary>
    public void AddTorque(float3 torque)
    {
        if (!IsSimulationOwner)
            return;
        PendingTorque += torque;
    }

    // Pending forces (consumed by hook each frame)
    public float3 PendingForce;
    public float3 PendingImpulse;
    public float3 PendingTorque;

    /// <summary>
    /// Clear pending forces (called by hook after applying them).
    /// </summary>
    public void ClearPendingForces()
    {
        PendingForce = float3.Zero;
        PendingImpulse = float3.Zero;
        PendingTorque = float3.Zero;
    }
}

