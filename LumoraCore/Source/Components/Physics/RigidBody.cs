// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// requires a Collider component on the same slot to define the collision shape
[ComponentCategory("Physics")]
public class RigidBody : ImplementableComponent
{
    // SYNC FIELDS

    // kilograms
    public readonly Sync<float> Mass;

    public readonly Sync<float3> LinearVelocity;

    // radians per second
    public readonly Sync<float3> AngularVelocity;

    public readonly Sync<bool> UseGravity;

    public readonly Sync<bool> IsKinematic;

    public readonly Sync<float> LinearDamping;

    public readonly Sync<float> AngularDamping;

    public readonly Sync<bool> FreezePositionX;
    public readonly Sync<bool> FreezePositionY;
    public readonly Sync<bool> FreezePositionZ;

    public readonly Sync<bool> FreezeRotationX;
    public readonly Sync<bool> FreezeRotationY;
    public readonly Sync<bool> FreezeRotationZ;

    // RUNTIME STATE

    // These are LOCAL runtime state, not synced. Only the owner runs the simulation, so only the owner's
    // values are meaningful; a non-owner doesn't simulate and leaves them at their defaults. They are not
    // replicated - peers that need a body's sleep/collision state should drive off the synced velocity/pose
    // the owner writes, not these. If a future need requires them network-wide, promote to Sync fields the
    // owner writes. -xlinka

    // owner-local; only valid on the simulating peer
    public bool IsSleeping { get; set; }

    // owner-local; only valid on the simulating peer
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
        Slot.WorldTransformChanged += OnSlotMoved;
        LumoraLogger.Debug($"RigidBody: Initialized on '{Slot.SlotName.Value}' with Mass={Mass.Value}kg");
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.WorldTransformChanged -= OnSlotMoved;
        base.OnDestroy();
    }

    // A body this machine does not simulate still follows its object here, so a cast or a search meets it
    // where the object is. Only when the object moved: a still one costs nothing.
    private void OnSlotMoved(Slot _)
    {
        if (!IsSimulationOwner)
            RunApplyChanges();
    }

    private bool _wasOwner;

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

    // exactly one peer integrates forces and replicates pose/velocity; everyone else follows that
    // replicated transform. host-authoritative: a body under a user's root belongs to that user's peer,
    // everything else belongs to the world authority (host)
    public bool IsSimulationOwner
    {
        get
        {
            var world = World;
            if (world == null)
                return false;

            // A local body exists on this machine alone, so this machine is the one to move it.
            if (IsLocalElement)
                return true;

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

        bool owner = IsSimulationOwner;
        if (owner)
        {
            // Owner drives the sim: queue the hook so it integrates forces and writes the synced pose/velocity. -xlinka
            RunApplyChanges();
        }
        else
        {
            // Non-owner: never run a local sim that would fight the replicated transform. Drop any forces that
            // got queued locally so they can't accumulate and fire if ownership later transfers to us. The body
            // follows the Slot transform the owner replicates, moved whenever the slot is (OnSlotMoved). A body
            // just handed to another machine, by a grab, gets one more pass so it stops simulating here at
            // once instead of carrying on as a ghost that snaps the object back when it returns. -xlinka
            ClearPendingForces();
            if (_wasOwner)
                RunApplyChanges();
        }
        _wasOwner = owner;
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

    // no-op on a non-owner; forces are integrated only by the simulating peer
    public void AddForce(float3 force)
    {
        if (!IsSimulationOwner)
            return;
        PendingForce += force;
    }

    // instantaneous velocity change; no-op on a non-owner
    public void AddImpulse(float3 impulse)
    {
        if (!IsSimulationOwner)
            return;
        PendingImpulse += impulse;
    }

    // no-op on a non-owner
    public void AddTorque(float3 torque)
    {
        if (!IsSimulationOwner)
            return;
        PendingTorque += torque;
    }

    // Exact velocity hand-off, for a throw. An impulse is the wrong tool here: it would need the mass divided
    // back out, and it stacks on top of whatever the body already carries - and a body coming off a held
    // (kinematic) frame carries junk from being teleported around by the hand. A release wants the measured
    // velocity, verbatim. Owner only, same as the force methods. -xlinka
    // Hard ceiling on a velocity handed over from outside the simulation. Everything upstream of this is
    // negotiable: the throw tunable that clamps a release is itself a replicated field, and a peer that
    // forges the tunable and the velocity together gets both numbers it asked for. This is the one place
    // both have to pass through, on every peer including the host, so a spoofed magnitude cannot leave the
    // machine that made it up. Generous enough that no real throw ever reaches it. -xlinka
    // A respawn or a reset: the slot pose is the truth for one frame and the body is moved onto it
    // with its velocities cleared. Without this the body's own pose writes the slot straight back to
    // where it fell on the next physics step. -xlinka
    public bool HasPendingTeleport;
    public void Teleport(float3 position, floatQ rotation)
    {
        Slot.GlobalPosition = position;
        Slot.GlobalRotation = rotation;
        LinearVelocity.Value = float3.Zero;
        AngularVelocity.Value = float3.Zero;
        HasPendingTeleport = true;
        RunApplyChanges();
    }
    public const float MaxTransferSpeed = 64f;
    public const float MaxTransferSpin = 64f;

    // Bounded to MaxTransferSpeed and MaxTransferSpin, as a throw hands a body off; unbounded, whatever it
    // is given as long as it is finite, as a script sets it.
    public void SetVelocities(float3 linear, float3 angular, bool bounded = true)
    {
        if (!IsSimulationOwner)
            return;

        PendingLinearVelocity = Bound(linear, bounded ? MaxTransferSpeed : float.PositiveInfinity);
        PendingAngularVelocity = Bound(angular, bounded ? MaxTransferSpin : float.PositiveInfinity);
        HasPendingVelocity = true;
        RunApplyChanges();
    }

    private static float3 Bound(float3 value, float limit)
    {
        float lengthSquared = value.LengthSquared;
        if (!float.IsFinite(lengthSquared))
            return float3.Zero;
        return lengthSquared > limit * limit ? value.Normalized * limit : value;
    }

    // Pending forces (consumed by hook each frame)
    public float3 PendingForce;
    public float3 PendingImpulse;
    public float3 PendingTorque;

    public float3 PendingLinearVelocity;
    public float3 PendingAngularVelocity;
    public bool HasPendingVelocity;

    // called by the hook after applying pending forces
    public void ClearPendingForces()
    {
        PendingForce = float3.Zero;
        PendingImpulse = float3.Zero;
        PendingTorque = float3.Zero;
        PendingLinearVelocity = float3.Zero;
        PendingAngularVelocity = float3.Zero;
        HasPendingVelocity = false;
    }
}

