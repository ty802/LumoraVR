// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;
using LumoraRigidBody = Lumora.Core.Components.RigidBody;

namespace Lumora.Godot.Hooks;

[ImplementableHook(typeof(LumoraRigidBody))]
public class RigidBodyHook : ComponentHook<LumoraRigidBody>
{
    private RigidBody3D _rigidBody = null!;
    private bool _hasCollisionShape;
    private bool _positionInitialized;
    private int _framesSinceInit;
    private bool _pendingWorldRootReparent;
    private readonly List<MeshInstance3D> _debugEdges = new();
    private const int DebugCircleSegments = 24;
    // Speed caps so a released grab / contact spike can't fling a prop across the room. ~a hard throw.
    private const float MaxLinearSpeed = 8f;    // m/s
    private const float MaxAngularSpeed = 16f;  // rad/s
    private static bool _showDebugEdges = true;
    private static StandardMaterial3D _debugLineMaterial = null!;

    public RigidBody3D GodotRigidBody => _rigidBody;

    public override void Initialize()
    {
        base.Initialize();

        _rigidBody = new RigidBody3D();
        _rigidBody.Name = "RigidBody_" + Owner.Slot.SlotName.Value;

        // This world's collision bit (BEFORE adding to tree): rigid bodies only interact with their
        // own world's geometry.
        uint worldBit = WorldHook.GetCollisionBitFor(Owner?.World);
        _rigidBody.CollisionLayer = worldBit;
        _rigidBody.CollisionMask = worldBit;
        (Owner?.World?.Hook as WorldHook)?.RegisterBody(Owner?.Slot, _rigidBody.GetRid());

        if (Owner?.Slot != null)
        {
            _rigidBody.SetMeta("LumoraSlotRef", Owner.Slot.ReferenceID.ToDecimalString());
        }

        // A world in the background keeps its bodies in the physics space, so its scripts still meet them.
        _rigidBody.DisableMode = CollisionObject3D.DisableModeEnum.KeepActive;
        _rigidBody.ContactMonitor = true;
        _rigidBody.MaxContactsReported = 16;
        _rigidBody.BodyEntered += OnBodyEntered;
        _rigidBody.BodyExited += OnBodyExited;
        _rigidBody.ContinuousCd = true;

        // Start frozen - we'll unfreeze after a few frames to let static colliders initialize first
        // This prevents rigid bodies from falling through the ground before it exists
        _rigidBody.Freeze = true;
        _framesSinceInit = 0;

        // Add collision shapes BEFORE adding to tree
        AddCollidersFromSlot();

        // Physics bodies must be siblings of attachedNode (not children) to avoid transform feedback loops.
        // When RigidBody syncs position to Slot, SlotHook updates attachedNode. If RigidBody were a child,
        // it would move WITH the parent, causing double-movement each frame.
        // Both RigidBody and attachedNode should be under worldRoot as siblings.
        Node3D worldRoot = (Owner?.World?.GodotSceneRoot as Node3D)!;
        if (worldRoot != null)
        {
            worldRoot.AddChild(_rigidBody);
            LumoraLogger.Debug($"RigidBodyHook: Added to worldRoot as sibling of slot node");
        }
        else
        {
            // Fallback: add to attachedNode's parent (making them siblings)
            var parent = attachedNode?.GetParent() as Node3D;
            if (parent != null)
            {
                parent.AddChild(_rigidBody);
                LumoraLogger.Debug($"RigidBodyHook: Added as sibling of attachedNode under '{parent.Name}'");
            }
            else
            {
                // Last resort - will cause issues but at least the body exists
                attachedNode!.AddChild(_rigidBody);
                LumoraLogger.Warn($"RigidBodyHook: Added as child of attachedNode (will have transform issues!)");
            }
            _pendingWorldRootReparent = true;
        }

        if (_rigidBody.IsInsideTree())
        {
            SetInitialTransform();
        }
        else
        {
            // Defer the transform setup until the node is in the tree
            _rigidBody.TreeEntered += OnTreeEntered;
        }

        LumoraLogger.Debug($"RigidBodyHook: Created RigidBody3D for '{Owner!.Slot.SlotName.Value}' with {_rigidBody.GetChildCount()} collision shapes (frozen until static colliders ready)");
    }

    private void OnTreeEntered()
    {
        _rigidBody.TreeEntered -= OnTreeEntered;

        // Just set the transform - don't try to reparent during tree entry
        // The RigidBody will be under worldRoot if it was available, or under attachedNode
        SetInitialTransform();
    }

    private void SetInitialTransform()
    {
        if (_positionInitialized) return;
        _positionInitialized = true;

        var slotPos = Owner.Slot.GlobalPosition;
        var slotRot = Owner.Slot.GlobalRotation;

        _rigidBody.GlobalPosition = new Vector3(slotPos.x, slotPos.y, slotPos.z);
        _rigidBody.Quaternion = new Quaternion(slotRot.x, slotRot.y, slotRot.z, slotRot.w);

        LumoraLogger.Debug($"RigidBodyHook: Position set for '{Owner.Slot.SlotName.Value}' at ({slotPos.x:F2}, {slotPos.y:F2}, {slotPos.z:F2})");
    }

    private readonly Dictionary<Collider, CollisionShape3D> _colliderShapes = new();
    private readonly List<Collider> _removedColliders = new();

    private void AddCollidersFromSlot()
    {
        if (_rigidBody == null) return;
        bool debugChanged = false;
        foreach (var component in Owner.Slot.Components)
        {
            if (component is not (BoxCollider or SphereCollider or CapsuleCollider or CylinderCollider)) continue;
            var collider = (Collider)component;
            bool created = !_colliderShapes.TryGetValue(collider, out var node);
            if (created)
            {
                node = new CollisionShape3D { Name = collider.GetType().Name + "Shape" };
                _rigidBody.AddChild(node);
                _colliderShapes.Add(collider, node);
            }
            var scale = Lumora.Godot.Helpers.ColliderShapeScaling.ScaleOf(collider);
            var shape = Lumora.Godot.Helpers.ColliderShapeScaling.Update(collider, scale, node!.Shape, out bool changed);
            if (!ReferenceEquals(node.Shape, shape)) node.Shape = shape;
            var offset = Lumora.Godot.Helpers.ColliderShapeScaling.OffsetOf(collider, scale);
            debugChanged |= created || changed || node.Position != offset;
            if (node.Position != offset) node.Position = offset;
            bool disabled = !collider.Enabled || !collider.Slot.IsActive
                || collider.Type.Value == Lumora.Core.Physics.ColliderType.NoCollision;
            if (node.Disabled != disabled) node.Disabled = disabled;
        }
        _removedColliders.Clear();
        foreach (var pair in _colliderShapes)
            if (pair.Key.IsDestroyed || pair.Key.Slot != Owner.Slot) _removedColliders.Add(pair.Key);
        foreach (var collider in _removedColliders)
        {
            var node = _colliderShapes[collider];
            _rigidBody.RemoveChild(node);
            node.QueueFree();
            _colliderShapes.Remove(collider);
            debugChanged = true;
        }
        _hasCollisionShape = _colliderShapes.Count > 0;
        if (!debugChanged) return;
        ClearDebugEdges();
        foreach (var node in _colliderShapes.Values)
        {
            switch (node.Shape)
            {
                case BoxShape3D box: AddDebugEdgesForBox(box.Size, node.Position); break;
                case SphereShape3D sphere: AddDebugEdgesForSphere(sphere.Radius, node.Position); break;
                case CapsuleShape3D capsule: AddDebugEdgesForCapsule(capsule.Radius, capsule.Height, node.Position); break;
                case CylinderShape3D cylinder: AddDebugEdgesForCylinder(cylinder.Radius, cylinder.Height, node.Position); break;
            }
        }
    }

    public override void ApplyChanges()
    {
        if (_rigidBody == null || !GodotObject.IsInstanceValid(_rigidBody))
            return;

        if (_pendingWorldRootReparent)
        {
            if (!TryReparentToWorldRoot())
            {
                return;
            }

            _pendingWorldRootReparent = false;
            _positionInitialized = false;
        }

        if (!_positionInitialized)
        {
            if (_rigidBody.IsInsideTree())
            {
                SetInitialTransform();
            }
            else
            {
                // Wait until the rigid body is in the scene tree before syncing transforms.
                return;
            }
        }

        // A body this machine does not simulate is kinematic here and follows its object, so casts and
        // searches meet it where it is. Nothing of it is written back to the object, and it reports no
        // touches: those belong to the machine that simulates it. -xlinka
        if (!Owner.IsSimulationOwner)
        {
            FollowSlot();
            ReportTouches();
            return;
        }

        // Wait a few frames before enabling physics to let static colliders (ground) initialize
        // This prevents rigid bodies from falling through the ground before it exists
        _framesSinceInit++;
        if (_framesSinceInit == 3)
        {
            // Reset position to slot's current position (in case it drifted during frozen frames)
            var slotPos = Owner.Slot.GlobalPosition;
            _rigidBody.GlobalPosition = new Vector3(slotPos.x, slotPos.y, slotPos.z);

            if (!Owner.IsKinematic.Value)
            {
                _rigidBody.Freeze = false;
                LumoraLogger.Debug($"RigidBodyHook: Unfroze '{Owner.Slot.SlotName.Value}' at ({slotPos.x:F1}, {slotPos.y:F1}, {slotPos.z:F1})");
            }
        }

        // Geometry, offsets and ancestor scale can change after the body was created.
        // The shape updater only sends changed dimensions to the physics server.
        AddCollidersFromSlot();

        // Only push a parameter the engine does not already hold. ApplyChanges runs every frame for the
        // simulation owner (RigidBody.OnUpdate queues it), and every one of these is a marshalled call
        // into the physics server that also dirties the body. A room of props was paying four of them
        // per body per frame to write the numbers back unchanged. -xlinka
        PushParameters();

        // Handle kinematic mode (only after initial delay). A HELD body counts as kinematic: the
        // grabber owns the transform - leaving physics live made it overwrite the slot every frame,
        // so grabs snapped back instantly and rigid bodies read as "not grabbable".
        if (_framesSinceInit >= 3)
        {
            if (Owner.IsKinematic.Value || IsHeld)
            {
                _rigidBody.Freeze = true;
                _rigidBody.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            }
            else
            {
                _rigidBody.Freeze = false;
            }
        }

        if (Owner.PendingForce != float3.Zero)
        {
            _rigidBody.ApplyCentralForce(new Vector3(
                Owner.PendingForce.x,
                Owner.PendingForce.y,
                Owner.PendingForce.z));
        }

        if (Owner.PendingImpulse != float3.Zero)
        {
            _rigidBody.ApplyCentralImpulse(new Vector3(
                Owner.PendingImpulse.x,
                Owner.PendingImpulse.y,
                Owner.PendingImpulse.z));
        }

        if (Owner.PendingTorque != float3.Zero)
        {
            _rigidBody.ApplyTorque(new Vector3(
                Owner.PendingTorque.x,
                Owner.PendingTorque.y,
                Owner.PendingTorque.z));
        }

        // Throw hand-off: SET the velocity, never add. The body was kinematic a frame ago and whatever the
        // freeze/unfreeze left on it has nothing to do with the throw. A sleeping body swallows velocity
        // writes, so wake it first. Clamped below like any other live body.
        if (Owner.HasPendingTeleport)
        {
            Owner.HasPendingTeleport = false;
            var teleportPos = Owner.Slot.GlobalPosition;
            var teleportRot = Owner.Slot.GlobalRotation;
            _rigidBody.GlobalPosition = new Vector3(teleportPos.x, teleportPos.y, teleportPos.z);
            _rigidBody.Quaternion = new Quaternion(teleportRot.x, teleportRot.y, teleportRot.z, teleportRot.w);
            _rigidBody.LinearVelocity = Vector3.Zero;
            _rigidBody.AngularVelocity = Vector3.Zero;
            _rigidBody.Sleeping = false;
        }
        if (Owner.HasPendingVelocity)
        {
            _rigidBody.Sleeping = false;
            var throwLinear = Owner.PendingLinearVelocity;
            var throwAngular = Owner.PendingAngularVelocity;
            _rigidBody.LinearVelocity = new Vector3(throwLinear.x, throwLinear.y, throwLinear.z);
            _rigidBody.AngularVelocity = new Vector3(throwAngular.x, throwAngular.y, throwAngular.z);
        }

        Owner.ClearPendingForces();

        ReportTouches();

        // SLEEPING BODIES COST ONE READ. Godot has parked this body: its transform cannot change, its
        // velocity is zero, and there is nothing to clamp. Everything below - two velocity reads, two
        // sync writes and a transform round trip through the slot - would be answering questions whose
        // answers cannot have changed. The one read that stays is Sleeping itself, because that is how
        // this peer learns the body was woken by a collision it did not initiate. -xlinka
        if (!Owner.IsKinematic.Value && !IsHeld && !_rigidBody.Freeze && _rigidBody.Sleeping)
        {
            Owner.IsSleeping = true;
            if (!_wasSleeping)
            {
                _wasSleeping = true;
                Owner.LinearVelocity.Value = float3.Zero;
                Owner.AngularVelocity.Value = float3.Zero;
                SyncTransformToSlot();
            }
            else
            {
                // A parked body still has to notice its slot being moved from outside it - a gizmo, an
                // inspector edit, a script. The check is two cached engine-side reads and a compare; only
                // a pose that actually changed costs a wake and a push. Without it the body would sit
                // where it fell and snap the slot back the moment something else woke it. -xlinka
                var slotPos = Owner.Slot.GlobalPosition;
                var slotRot = Owner.Slot.GlobalRotation;
                float dot = slotRot.x * _sleepSlotRot.x + slotRot.y * _sleepSlotRot.y
                          + slotRot.z * _sleepSlotRot.z + slotRot.w * _sleepSlotRot.w;
                if (float3.DistanceSquared(slotPos, _sleepSlotPos) > 1e-8f || System.MathF.Abs(dot) < 0.99999f)
                {
                    _rigidBody.Sleeping = false;
                    _rigidBody.GlobalPosition = new Vector3(slotPos.x, slotPos.y, slotPos.z);
                    _rigidBody.Quaternion = new Quaternion(slotRot.x, slotRot.y, slotRot.z, slotRot.w);
                }
            }
            _sleepSlotPos = Owner.Slot.GlobalPosition;
            _sleepSlotRot = Owner.Slot.GlobalRotation;
            return;
        }
        _wasSleeping = false;

        // Velocity clamp: a released grab (kinematic freeze-mode hands the body the velocity derived from
        // the last teleport-to-hand) or a spawn/contact depenetration spike can fling a prop across the
        // room. Cap the speed so nothing leaves faster than a hard throw - props settle instead of flying.
        // Only in live dynamic mode (a held/kinematic body is driven by transform, not velocity). -xlinka
        if (!Owner.IsKinematic.Value && !IsHeld && !_rigidBody.Freeze)
        {
            var lv = _rigidBody.LinearVelocity;
            if (lv.LengthSquared() > MaxLinearSpeed * MaxLinearSpeed)
                _rigidBody.LinearVelocity = lv.Normalized() * MaxLinearSpeed;
            var av = _rigidBody.AngularVelocity;
            if (av.LengthSquared() > MaxAngularSpeed * MaxAngularSpeed)
                _rigidBody.AngularVelocity = av.Normalized() * MaxAngularSpeed;
        }

        Owner.IsSleeping = _rigidBody.Sleeping;

        var linVel = _rigidBody.LinearVelocity;
        var angVel = _rigidBody.AngularVelocity;
        Owner.LinearVelocity.Value = new float3(linVel.X, linVel.Y, linVel.Z);
        Owner.AngularVelocity.Value = new float3(angVel.X, angVel.Y, angVel.Z);

        SyncTransformToSlot();
    }

    private Vector3 _followPosition = new(float.NaN, float.NaN, float.NaN);
    private Quaternion _followRotation = new(float.NaN, float.NaN, float.NaN, float.NaN);

    private void FollowSlot()
    {
        if (!_rigidBody.Freeze || _rigidBody.FreezeMode != RigidBody3D.FreezeModeEnum.Kinematic)
        {
            // The mode before the freeze, so the body is never static for a step.
            _rigidBody.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            _rigidBody.Freeze = true;
            _rigidBody.LinearVelocity = Vector3.Zero;
            _rigidBody.AngularVelocity = Vector3.Zero;
        }
        AddCollidersFromSlot();
        var slotPos = Owner.Slot.GlobalPosition;
        var slotRot = Owner.Slot.GlobalRotation;
        var position = new Vector3(slotPos.x, slotPos.y, slotPos.z);
        var rotation = new Quaternion(slotRot.x, slotRot.y, slotRot.z, slotRot.w);
        if (position == _followPosition && rotation == _followRotation)
            return;
        _followPosition = position;
        _followRotation = rotation;
        _rigidBody.GlobalPosition = position;
        _rigidBody.Quaternion = rotation;
    }

    private bool TryReparentToWorldRoot()
    {
        var worldRoot = Owner?.World?.GodotSceneRoot as Node3D;
        if (worldRoot == null || _rigidBody == null || !GodotObject.IsInstanceValid(_rigidBody))
        {
            return false;
        }

        if (_rigidBody.GetParent() != worldRoot)
        {
            var slotPos = Owner!.Slot.GlobalPosition;
            var slotRot = Owner.Slot.GlobalRotation;

            if (_rigidBody.GetParent() != null)
            {
                _rigidBody.Reparent(worldRoot, true);
            }
            else
            {
                worldRoot.AddChild(_rigidBody);
            }

            _rigidBody.GlobalPosition = new Vector3(slotPos.x, slotPos.y, slotPos.z);
            _rigidBody.Quaternion = new Quaternion(slotRot.x, slotRot.y, slotRot.z, slotRot.w);
        }

        return true;
    }

    private bool _wasSleeping;
    private float3 _sleepSlotPos;
    private floatQ _sleepSlotRot = floatQ.Identity;
    private float _lastMass = float.NaN;
    private float _lastGravityScale = float.NaN;
    private float _lastLinearDamp = float.NaN;
    private float _lastAngularDamp = float.NaN;

    // Six checkboxes that did nothing.
    //
    // FreezePositionX/Y/Z and FreezeRotationX/Y/Z were declared, replicated, saved and shown in the
    // inspector, and the hook read none of them - it only ever touched the body's whole-body Freeze,
    // which is a different feature entirely. Ticking "Freeze Rotation Y" left the object spinning. The
    // renderer has had the real per-axis constraints all along. -xlinka
    private bool _lastFreezeWritten;
    private bool _lastFpx, _lastFpy, _lastFpz, _lastFrx, _lastFry, _lastFrz;

    private void PushAxisLocks()
    {
        bool fpx = Owner.FreezePositionX.Value, fpy = Owner.FreezePositionY.Value, fpz = Owner.FreezePositionZ.Value;
        bool frx = Owner.FreezeRotationX.Value, fry = Owner.FreezeRotationY.Value, frz = Owner.FreezeRotationZ.Value;

        if (_lastFreezeWritten
            && fpx == _lastFpx && fpy == _lastFpy && fpz == _lastFpz
            && frx == _lastFrx && fry == _lastFry && frz == _lastFrz)
            return;

        _rigidBody.AxisLockLinearX = fpx;
        _rigidBody.AxisLockLinearY = fpy;
        _rigidBody.AxisLockLinearZ = fpz;
        _rigidBody.AxisLockAngularX = frx;
        _rigidBody.AxisLockAngularY = fry;
        _rigidBody.AxisLockAngularZ = frz;

        _lastFpx = fpx; _lastFpy = fpy; _lastFpz = fpz;
        _lastFrx = frx; _lastFry = fry; _lastFrz = frz;
        _lastFreezeWritten = true;
    }

    private void PushParameters()
    {
        PushAxisLocks();

        float mass = Owner.Mass.Value;
        if (mass != _lastMass)
        {
            _rigidBody.Mass = mass;
            _lastMass = mass;
        }

        float gravity = Owner.UseGravity.Value ? 1f : 0f;
        if (gravity != _lastGravityScale)
        {
            _rigidBody.GravityScale = gravity;
            _lastGravityScale = gravity;
        }

        float linear = Owner.LinearDamping.Value;
        if (linear != _lastLinearDamp)
        {
            _rigidBody.LinearDamp = linear;
            _lastLinearDamp = linear;
        }

        float angular = Owner.AngularDamping.Value;
        if (angular != _lastAngularDamp)
        {
            _rigidBody.AngularDamp = angular;
            _lastAngularDamp = angular;
        }
    }

    // Cached lazily; a body without a Grabbable never allocates the lookup again.
    private Grabbable? _grabbable;
    private bool _grabbableChecked;

    private bool IsHeld
    {
        get
        {
            if (!_grabbableChecked || (_grabbable != null && _grabbable.IsDestroyed))
            {
                _grabbable = Owner?.Slot?.GetComponent<Grabbable>();
                _grabbableChecked = true;
            }
            return _grabbable != null && !_grabbable.IsDestroyed && _grabbable.IsGrabbed;
        }
    }

    private void SyncTransformToSlot()
    {
        if (_rigidBody == null || !_rigidBody.IsInsideTree())
            return;

        if (Owner.IsKinematic.Value || IsHeld)
        {
            // Kinematic or held: sync Slot -> RigidBody
            // The slot is being moved by the grabber, RigidBody needs to follow
            var slotPos = Owner.Slot.GlobalPosition;
            var slotRot = Owner.Slot.GlobalRotation;
            _rigidBody.GlobalPosition = new Vector3(slotPos.x, slotPos.y, slotPos.z);
            _rigidBody.Quaternion = new Quaternion(slotRot.x, slotRot.y, slotRot.z, slotRot.w);
        }
        else
        {
            // Dynamic mode: sync RigidBody -> Slot (physics simulation)
            var globalPos = _rigidBody.GlobalPosition;
            var globalRot = _rigidBody.GlobalBasis.GetRotationQuaternion();

            // Sync physics position to Lumora Slot (this is what gets networked)
            Owner.Slot.GlobalPosition = new float3(globalPos.X, globalPos.Y, globalPos.Z);
            Owner.Slot.GlobalRotation = new floatQ(globalRot.X, globalRot.Y, globalRot.Z, globalRot.W);
        }

        // SlotHook will sync attachedNode from Slot.GlobalPosition - don't do it here
        // to avoid transform conflicts (RigidBody is a sibling of attachedNode, not a child)
    }

    // TOUCHES. Godot says a body came into or out of contact from inside its physics step, where nothing of
    // the world may be written, so the handlers only note the other node. ApplyChanges, on the world
    // thread, hands the notes to World.Physics, which tells whatever watches either side. Only the peer
    // simulating the body reports; a body frozen for a hold, made kinematic or handed to another peer ends
    // every touch it had. -xlinka
    private const int MaxTouchNotes = 64;
    private readonly List<(bool Began, ulong Id, Node Other)> _touchNotes = new();
    private readonly Dictionary<ulong, Lumora.Core.Slot?> _touching = new();

    private void OnBodyEntered(Node body) => NoteTouch(true, body);

    private void OnBodyExited(Node body) => NoteTouch(false, body);

    private void NoteTouch(bool began, Node body)
    {
        if (body == null || !GodotObject.IsInstanceValid(body))
            return;
        lock (_touchNotes)
        {
            if (_touchNotes.Count < MaxTouchNotes)
                _touchNotes.Add((began, body.GetInstanceId(), body));
        }
    }

    private void ReportTouches()
    {
        (bool Began, ulong Id, Node Other)[] notes;
        lock (_touchNotes)
        {
            notes = _touchNotes.ToArray();
            _touchNotes.Clear();
        }
        var physics = Owner?.World?.Physics;
        if (physics == null || !Owner!.IsSimulationOwner || Owner.IsKinematic.Value || IsHeld || _rigidBody.Freeze)
        {
            EndTouches();
            return;
        }

        var hook = Owner.World.Hook as WorldHook;
        foreach (var (began, id, other) in notes)
        {
            if (began)
            {
                if (_touching.ContainsKey(id))
                    continue;
                var slot = GodotObject.IsInstanceValid(other) ? hook?.SlotOfBody(other) : null;
                _touching[id] = slot;
                float3 point = Owner.Slot.GlobalPosition, normal = float3.Zero;
                float impulse = 0f;
                if (physics.HasContactWatchers(Owner.Slot) || physics.HasContactWatchers(slot))
                    ReadContact(other, ref point, ref normal, ref impulse);
                physics.ReportContactBegan(Owner.Slot, slot, point, normal, impulse);
            }
            else if (_touching.Remove(id, out var slot))
            {
                physics.ReportContactEnded(Owner.Slot, slot);
            }
        }
        Owner.IsColliding = _touching.Count > 0;
    }

    // Where and how hard this body met the other, from the last physics step. Falls back to the middle
    // between the two, with no impulse, if the step kept no contact for them.
    private void ReadContact(Node other, ref float3 point, ref float3 normal, ref float impulse)
    {
        if (other is Node3D node3D && GodotObject.IsInstanceValid(node3D))
        {
            var mine = _rigidBody.GlobalPosition;
            var theirs = node3D.GlobalPosition;
            point = new float3((mine.X + theirs.X) * 0.5f, (mine.Y + theirs.Y) * 0.5f, (mine.Z + theirs.Z) * 0.5f);
            var away = mine - theirs;
            if (away.LengthSquared() > 1e-10f)
            {
                away = away.Normalized();
                normal = new float3(away.X, away.Y, away.Z);
            }
        }

        var state = PhysicsServer3D.BodyGetDirectState(_rigidBody.GetRid());
        if (state == null || !GodotObject.IsInstanceValid(other))
            return;
        ulong otherId = other.GetInstanceId();
        int found = 0;
        Vector3 sum = Vector3.Zero, normals = Vector3.Zero;
        float total = 0f;
        int count = state.GetContactCount();
        for (int i = 0; i < count; i++)
        {
            if (state.GetContactColliderId(i) != otherId)
                continue;
            sum += state.GetContactColliderPosition(i);
            normals += state.GetContactLocalNormal(i);
            total += state.GetContactImpulse(i).Length();
            found++;
        }
        if (found == 0)
            return;
        sum /= found;
        point = new float3(sum.X, sum.Y, sum.Z);
        if (normals.LengthSquared() > 1e-10f)
        {
            normals = normals.Normalized();
            normal = new float3(normals.X, normals.Y, normals.Z);
        }
        impulse = total;
    }

    private void EndTouches()
    {
        if (_touching.Count == 0)
            return;
        var physics = Owner?.World?.Physics;
        var self = Owner?.Slot;
        if (physics != null && self != null)
        {
            foreach (var slot in _touching.Values)
                physics.ReportContactEnded(self, slot);
        }
        _touching.Clear();
        if (Owner != null)
            Owner.IsColliding = false;
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld)
            EndTouches();
        lock (_touchNotes)
            _touchNotes.Clear();
        if (_rigidBody != null && GodotObject.IsInstanceValid(_rigidBody))
        {
            _rigidBody.BodyEntered -= OnBodyEntered;
            _rigidBody.BodyExited -= OnBodyExited;
            (Owner?.World?.Hook as WorldHook)?.UnregisterBody(Owner?.Slot, _rigidBody.GetRid());
        }
        if (!destroyingWorld && _rigidBody != null && GodotObject.IsInstanceValid(_rigidBody))
        {
            _rigidBody.QueueFree();
        }

        ClearDebugEdges();
        _rigidBody = null!;
        base.Destroy(destroyingWorld);
    }

    private void AddDebugEdgesForBox(Vector3 size, Vector3 offset)
    {
        if (!_showDebugEdges || _rigidBody == null) return;
        var mesh = BuildBoxWireMesh(size);
        AddDebugEdgeMesh(mesh, offset, "DebugBoxEdges");
    }

    private void AddDebugEdgesForSphere(float radius, Vector3 offset)
    {
        if (!_showDebugEdges || _rigidBody == null) return;
        var mesh = BuildSphereWireMesh(radius);
        AddDebugEdgeMesh(mesh, offset, "DebugSphereEdges");
    }

    private void AddDebugEdgesForCapsule(float radius, float height, Vector3 offset)
    {
        if (!_showDebugEdges || _rigidBody == null) return;
        var mesh = BuildCapsuleWireMesh(radius, height);
        AddDebugEdgeMesh(mesh, offset, "DebugCapsuleEdges");
    }

    private void AddDebugEdgesForCylinder(float radius, float height, Vector3 offset)
    {
        if (!_showDebugEdges || _rigidBody == null) return;
        var mesh = BuildCylinderWireMesh(radius, height);
        AddDebugEdgeMesh(mesh, offset, "DebugCylinderEdges");
    }

    private void AddDebugEdgeMesh(ArrayMesh? mesh, Vector3 offset, string name)
    {
        if (mesh == null) return;
        var meshInstance = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = GetDebugLineMaterial(),
            Position = offset,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _rigidBody.AddChild(meshInstance);
        _debugEdges.Add(meshInstance);
    }

    private void ClearDebugEdges()
    {
        for (int i = 0; i < _debugEdges.Count; i++)
        {
            var node = _debugEdges[i];
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }
        _debugEdges.Clear();
    }

    private static StandardMaterial3D GetDebugLineMaterial()
    {
        if (_debugLineMaterial != null) return _debugLineMaterial;
        _debugLineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.15f, 0.15f, 1f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        return _debugLineMaterial;
    }

    private static ArrayMesh? BuildBoxWireMesh(Vector3 size)
    {
        float hx = size.X * 0.5f;
        float hy = size.Y * 0.5f;
        float hz = size.Z * 0.5f;

        var corners = new[]
        {
            new Vector3(-hx, -hy, -hz),
            new Vector3(hx, -hy, -hz),
            new Vector3(hx, hy, -hz),
            new Vector3(-hx, hy, -hz),
            new Vector3(-hx, -hy, hz),
            new Vector3(hx, -hy, hz),
            new Vector3(hx, hy, hz),
            new Vector3(-hx, hy, hz)
        };

        var vertices = new List<Vector3>();
        var indices = new List<int>();

        AddLine(vertices, indices, corners[0], corners[1]);
        AddLine(vertices, indices, corners[1], corners[2]);
        AddLine(vertices, indices, corners[2], corners[3]);
        AddLine(vertices, indices, corners[3], corners[0]);

        AddLine(vertices, indices, corners[4], corners[5]);
        AddLine(vertices, indices, corners[5], corners[6]);
        AddLine(vertices, indices, corners[6], corners[7]);
        AddLine(vertices, indices, corners[7], corners[4]);

        AddLine(vertices, indices, corners[0], corners[4]);
        AddLine(vertices, indices, corners[1], corners[5]);
        AddLine(vertices, indices, corners[2], corners[6]);
        AddLine(vertices, indices, corners[3], corners[7]);

        return BuildLineMesh(vertices, indices);
    }

    private static ArrayMesh? BuildSphereWireMesh(float radius)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();

        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Up, radius, DebugCircleSegments, Vector3.Zero);
        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Forward, radius, DebugCircleSegments, Vector3.Zero);
        AddCircleLines(vertices, indices, Vector3.Up, Vector3.Forward, radius, DebugCircleSegments, Vector3.Zero);

        return BuildLineMesh(vertices, indices);
    }

    private static ArrayMesh? BuildCylinderWireMesh(float radius, float height)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();

        float half = height * 0.5f;
        var top = new Vector3(0f, half, 0f);
        var bottom = new Vector3(0f, -half, 0f);

        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Forward, radius, DebugCircleSegments, top);
        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Forward, radius, DebugCircleSegments, bottom);

        AddLine(vertices, indices, new Vector3(radius, -half, 0f), new Vector3(radius, half, 0f));
        AddLine(vertices, indices, new Vector3(-radius, -half, 0f), new Vector3(-radius, half, 0f));
        AddLine(vertices, indices, new Vector3(0f, -half, radius), new Vector3(0f, half, radius));
        AddLine(vertices, indices, new Vector3(0f, -half, -radius), new Vector3(0f, half, -radius));

        return BuildLineMesh(vertices, indices);
    }

    private static ArrayMesh? BuildCapsuleWireMesh(float radius, float height)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();

        float half = height * 0.5f;
        var top = new Vector3(0f, half, 0f);
        var bottom = new Vector3(0f, -half, 0f);

        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Forward, radius, DebugCircleSegments, top);
        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Forward, radius, DebugCircleSegments, bottom);

        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Up, radius, DebugCircleSegments, top);
        AddCircleLines(vertices, indices, Vector3.Right, Vector3.Up, radius, DebugCircleSegments, bottom);
        AddCircleLines(vertices, indices, Vector3.Up, Vector3.Forward, radius, DebugCircleSegments, top);
        AddCircleLines(vertices, indices, Vector3.Up, Vector3.Forward, radius, DebugCircleSegments, bottom);

        return BuildLineMesh(vertices, indices);
    }

    private static ArrayMesh? BuildLineMesh(List<Vector3> vertices, List<int> indices)
    {
        if (vertices.Count == 0) return null;
        var mesh = new ArrayMesh();
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
    }

    private static void AddCircleLines(List<Vector3> vertices, List<int> indices, Vector3 axisA, Vector3 axisB, float radius, int segments, Vector3 center)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Tau * i / segments;
            float a1 = Mathf.Tau * (i + 1) / segments;
            var p0 = center + axisA * (Mathf.Cos(a0) * radius) + axisB * (Mathf.Sin(a0) * radius);
            var p1 = center + axisA * (Mathf.Cos(a1) * radius) + axisB * (Mathf.Sin(a1) * radius);
            AddLine(vertices, indices, p0, p1);
        }
    }

    private static void AddLine(List<Vector3> vertices, List<int> indices, Vector3 from, Vector3 to)
    {
        int start = vertices.Count;
        vertices.Add(from);
        vertices.Add(to);
        indices.Add(start);
        indices.Add(start + 1);
    }
}
