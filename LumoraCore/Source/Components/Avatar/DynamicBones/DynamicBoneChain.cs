// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Warden;

namespace Lumora.Core.Components;

// Secondary bone motion for hair, tails, ears, clothes: a particle chain rides the animated skeleton
// and lags, springs and collides believably. Set Root (chain auto-builds from its descendants) or
// fill Bones explicitly; tune Inertia/Damping/Elasticity/Stiffness like the classic spring-bone
// parameters avatar creators already know.
//
// WHAT IS AND ISN'T REPLICATED. The simulation is LOCAL on every peer (bones are already posed
// identically from replicated proxies, so broadcasting the wiggle would only add churn) and runs
// AFTER the IK solve each frame. Writes are silent rotation-only swings, so skinning stays scale
// safe. The ONLY replicated state on this component is the grab: who is holding it, which bone, and
// where in that hand the bone was caught. Everything else - collisions, velocities, the solved pose -
// is worked out again on each machine from state it already has. -xlinka
[ComponentCategory("Physics/Dynamic Bones")]
[DefaultUpdateOrder(-4000)] // after AvatarIK (-5000) so chains ride the solved pose
public class DynamicBoneChain : Component, IInputUpdateReceiver, IGrabbable, IPermissionGrabSurface, IGrabHolderSurface
{
    // Chain root bone; descendants become the chain when Bones is empty.
    public readonly SyncRef<Slot> Root;

    // Explicit chain bones (depth-first, root first). Leave empty to auto-build from Root.
    public readonly SyncRefList<Slot> Bones;

    // 0..1: how rigidly base motion carries the chain (1 = no world-space lag).
    public readonly Sync<float> Inertia;

    // Force applied by base motion (the whip when the base moves fast).
    public readonly Sync<float> InertiaForce;

    public readonly Sync<float> Damping;

    // Spring pull toward the rest pose.
    public readonly Sync<float> Elasticity;

    // 0..1 hard limit on deviation from the rest pose (1 = rigid).
    public readonly Sync<float> Stiffness;

    // Give leaf bones a virtual end particle so the last real bone swings too.
    public readonly Sync<bool> SimulateTerminalBones;

    // Collision radius of each particle (scaled by the avatar).
    public readonly Sync<float> BaseBoneRadius;

    public readonly Sync<float3> Gravity;

    // Constant force in the chain root's local frame (wind, tail curl).
    public readonly Sync<float3> LocalForce;

    // Uniform length multiplier on the whole chain.
    public readonly Sync<float> GlobalStretch;

    // Colliders the chain pushes out of.
    public readonly SyncRefList<IDynamicBoneCollider> StaticColliders;

    // Metres from the local user's head past which this chain stops simulating. Zero never pauses.
    // Measured from the head rather than the camera node because that is what the renderer's own
    // visibility range is measured from, same as particle systems.
    public readonly Sync<float> MaxViewDistance;

    // Collide with OTHER users' heads and hands (see DynamicBonePlayerColliders).
    public readonly Sync<bool> CollideWithPlayers;

    // Collide with the body this chain is worn by. Off by default: an avatar author who cares about
    // their own hair meeting their own skull puts real colliders in StaticColliders, and those are
    // shaped like the actual head instead of the generic sphere the player set has to assume.
    public readonly Sync<bool> CollideWithOwnBody;

    // GRAB CONFIGURATION (tunables, not the protocol - see the three replicated members below)

    public readonly Sync<bool> AllowGrab;
    public readonly Sync<bool> AllowSteal;

    // Reach around a bone, in metres at 1:1 scale. Added to the particle radius.
    public readonly Sync<float> GrabRadius;

    // How far the hand may drag the caught bone past the chain's reach before it slips out.
    public readonly Sync<float> GrabReleaseDistance;

    // Let the hold slide to a neighbouring bone as the hand moves along the chain.
    public readonly Sync<bool> GrabSlipping;

    // Whether the virtual tip past the last real bone can be caught.
    public readonly Sync<bool> GrabTerminalBones;

    // The first bone is pinned to the animated skeleton, so catching it does nothing. Off by default.
    public readonly Sync<bool> GrabFirstBone;

    // Bones that refuse to be caught (the rest of the chain still can).
    public readonly SyncRefList<Slot> UngrabbableBones;

    public readonly Sync<int> GrabPriority;
    public readonly Sync<int> InteractionPriority;

    // Multiplier on the hand velocity handed to the chain when it lets go.
    public readonly Sync<float> ReleaseVelocityScale;

    // THE REPLICATED GRAB PROTOCOL - all three of it.
    //
    // Holder, which bone, and where in the holder's hand that bone was caught. Nothing else about a
    // grab crosses the wire: the solve that makes the chain follow the hand runs on every peer from
    // these three values plus the hand pose it already replicates. -xlinka

    public readonly SyncRef<Grabber> GrabberRef;

    // Index into the particle array, -1 when free. Not a bone RefID: the array carries virtual tip
    // particles that are not slots at all, and every peer builds it the same way from Bones.
    public readonly Sync<int> GrabbedBone;

    [NonPersistent]
    public readonly Sync<float3> GrabAnchor;

    public DynamicBoneChain()
    {
        Root = new SyncRef<Slot>(this);
        Bones = new SyncRefList<Slot>(this);
        Inertia = new Sync<float>(this, 0.2f);
        InertiaForce = new Sync<float>(this, 2f);
        Damping = new Sync<float>(this, 5f);
        Elasticity = new Sync<float>(this, 100f);
        Stiffness = new Sync<float>(this, 0.2f);
        SimulateTerminalBones = new Sync<bool>(this, true);
        BaseBoneRadius = new Sync<float>(this, 0.025f);
        Gravity = new Sync<float3>(this, float3.Zero);
        LocalForce = new Sync<float3>(this, float3.Zero);
        GlobalStretch = new Sync<float>(this, 1f);
        StaticColliders = new SyncRefList<IDynamicBoneCollider>(this);
        MaxViewDistance = new Sync<float>(this, 0f);
        CollideWithPlayers = new Sync<bool>(this, true);
        CollideWithOwnBody = new Sync<bool>(this, false);
        AllowGrab = new Sync<bool>(this, false);
        AllowSteal = new Sync<bool>(this, true);
        GrabRadius = new Sync<float>(this, 0.08f);
        GrabReleaseDistance = new Sync<float>(this, 0.75f);
        GrabSlipping = new Sync<bool>(this, true);
        GrabTerminalBones = new Sync<bool>(this, true);
        GrabFirstBone = new Sync<bool>(this, false);
        UngrabbableBones = new SyncRefList<Slot>(this);
        GrabPriority = new Sync<int>(this, 0);
        InteractionPriority = new Sync<int>(this, 0);
        ReleaseVelocityScale = new Sync<float>(this, 1f);
        GrabberRef = new SyncRef<Grabber>(this);
        GrabbedBone = new Sync<int>(this, -1);
        GrabAnchor = new Sync<float3>(this, float3.Zero);
    }

    private struct Particle
    {
        public Slot? Bone;            // null = virtual terminal extension
        public int ParentIndex;
        public float3 RestOffsetRootSpace; // offset from parent particle, in the ROOT's capture rotation frame
        public float3 RestDirParentSpace;  // normalized offset in the PARENT bone's capture rotation frame
        public floatQ RestRotParentSpace;  // bone rotation relative to parent bone at capture
        public float Length;               // capture-time world segment length
        public float3 Pos;
        public float3 PrevPos;
        public float3 Vel;
        public floatQ Rot;                 // solved world rotation this frame
        public bool Held;                  // on the path from the root to the caught bone
        public int RotationRoot;           // nearest held ancestor, 0 when none
        public floatQ RotOffset;           // how far that held ancestor swung off its rest direction
    }

    private Particle[]? _particles;
    private int[]? _childCounts;
    private floatQ _captureRootRotInverse;
    private floatQ _rootRestLocalRot;
    private float _captureRootScale = 1f;
    private bool _registered;
    private bool _needsRebuild = true;
    private bool _wasSimulating;
    private bool _paused;
    private bool _hasStepped;
    private ulong _lastSteppedFrame;

    private DynamicBoneManager? _manager;
    private int _effector = -1;
    private int _pendingGrabBone = -1;
    private Grabber? _lastKnownHolder;
    private readonly HandMotion _handMotion = new();

    // Frames this chain actually stepped. Diagnostics only - the batching probe reads it to prove a
    // chain steps exactly once per update.
    public int SimulationCount { get; private set; }

    public bool IsPausedByDistance => _paused;

    public int ParticleCount => _particles?.Length ?? 0;

    public float3 GetParticlePosition(int index)
        => _particles != null && index >= 0 && index < _particles.Length ? _particles[index].Pos : float3.Zero;

    public int HeldBoneIndex => _effector;

    public override void OnAwake()
    {
        base.OnAwake();
        Root.OnChanged += _ => _needsRebuild = true;
        Bones.OnChanged += _ => _needsRebuild = true;
        // The manager keeps its chains sorted by update order so the batched pass fires in the same
        // sequence the input dispatch would have. Moving a chain's order has to re-sort that list or
        // the pass runs in the order the chains happened to be created in.
        updateOrder.OnValueChange += _ => _manager?.NoteUpdateOrderChanged();
        // Runs on every instance including ones decoded from the network, unlike OnInit: this is where
        // a peer learns the chain was grabbed, stolen or let go.
        GrabberRef.OnTargetChange += OnHolderChanged;
    }

    public override void OnStart()
    {
        base.OnStart();
        _manager = DynamicBoneManager.For(World);
        _manager?.Register(this);

        var input = Engine.Current?.InputInterface;
        if (input != null)
        {
            input.RegisterInputEventReceiver(this);
            _registered = true;
        }
    }

    public override void OnDestroy()
    {
        if (_registered)
            Engine.Current?.InputInterface?.UnregisterInputEventReceiver(this);
        _registered = false;
        _manager?.Unregister(this);
        _manager = null;
        base.OnDestroy();
    }

    public void BeforeInputUpdate() { }

    // The chain keeps its own dispatch slot so its UpdateOrder still decides where in the frame the
    // pass happens; the work itself is batched by the world's manager. See DynamicBoneManager.
    public void AfterInputUpdate()
    {
        if (IsDestroyed)
            return;
        _manager ??= DynamicBoneManager.For(World);
        _manager?.Tick(this);
    }

    internal bool RunManagedSimulation(DynamicBoneManager manager, float delta)
    {
        if (!Enabled.Value || IsDestroyed)
        {
            _wasSimulating = false;
            return false;
        }

        if (_needsRebuild)
            BuildParticles();
        if (_particles == null || _particles.Length < 2)
            return false;

        // A bone died (avatar re-equip, slot deletion): rebuild next frame rather than throwing.
        for (int i = 0; i < _particles.Length; i++)
        {
            if (_particles[i].Bone != null && _particles[i].Bone!.IsDestroyed)
            {
                _needsRebuild = true;
                return false;
            }
        }

        if (IsBeyondViewDistance(manager))
        {
            // Paused chains write nothing, so the bones simply ride the skeleton rigidly. Nobody at
            // that range can tell, and there is no frozen pose left lying around to pop back in.
            _paused = true;
            _wasSimulating = false;
            return false;
        }

        // Re-entry after ANY gap, not just a distance pause: a backgrounded world, a disabled chain, an
        // avatar that was hidden for a while. Reseed straight from the live skeleton instead of picking
        // up wherever the chain was standing when it stopped, because the body may be fifty metres and
        // a minute away by now and those stored positions are not a pose, they are a teleport waiting
        // to happen. Starting at rest with no velocity keeps the first frame's step bounded by the
        // springs, and the chain settles into its hang over the usual fraction of a second. -xlinka
        ulong frame = World?.Time.UpdateIndex ?? 0;
        if (_paused || !_hasStepped || frame - _lastSteppedFrame > ResumeGapFrames)
        {
            _paused = false;
            SeedParticlePositions();
            _wasSimulating = false;
        }
        _hasStepped = true;
        _lastSteppedFrame = frame;

        UpdateEffector();
        SampleHandMotion();
        Simulate(manager, delta);
        WriteBones();
        HandleGrabbing();
        SimulationCount++;
        return true;
    }

    // Frames a chain may miss and still carry on from where it was. Anything longer is a gap, and a
    // gap is reseeded.
    private const ulong ResumeGapFrames = 3;

    private bool IsBeyondViewDistance(DynamicBoneManager manager)
    {
        float distance = MaxViewDistance.Value;
        if (distance <= 0f || !manager.HasLocalHead)
            return false;

        // Never pause a chain someone is holding: the hand is right there.
        if (GrabberRef.Target != null)
            return false;

        var root = _particles![0].Bone;
        if (root == null || root.IsDestroyed)
            return false;

        // Tighter to come back than to leave, so a chain sitting exactly on the line does not pause and
        // reseed itself every other frame.
        float cutoff = (distance + ViewDistanceFadeMargin) * (_paused ? 0.95f : 1f);
        return (root.GlobalPosition - manager.LocalHeadPosition).LengthSquared > cutoff * cutoff;
    }

    // Keep simulating a little past the stated distance: the renderer's own visibility range has a
    // fade band of its own, and a chain that stopped moving inside it would be visibly frozen.
    private const float ViewDistanceFadeMargin = 3f;

    // Populate Bones from Root's descendant hierarchy (depth-first).
    public void SetupFromChildren()
    {
        var root = Root.Target;
        if (root == null)
            return;
        Bones.Clear();
        AddRecursive(root);
        _needsRebuild = true;
    }

    private void AddRecursive(Slot bone)
    {
        Bones.Add(bone);
        foreach (var child in bone.Children)
            AddRecursive(child);
    }

    private void BuildParticles()
    {
        _needsRebuild = false;
        _particles = null;
        _effector = -1;

        if (Bones.Count == 0 && Root.Target != null)
            SetupFromChildren();
        if (Bones.Count < 2 && !(Bones.Count == 1 && SimulateTerminalBones.Value))
            return;

        var bones = new List<Slot>();
        foreach (var bone in Bones)
        {
            if (bone != null && !bone.IsDestroyed)
                bones.Add(bone);
        }
        if (bones.Count == 0)
            return;

        var rootBone = bones[0];
        floatQ rootRot = rootBone.GlobalRotation;
        _captureRootRotInverse = rootRot.Inverse;
        // The sim writes the root bone's rotation every frame, so the live base orientation must be
        // derived from the CAPTURED rest local rotation under the animated parent - reading the live
        // rotation back would compound the swing frame over frame.
        _rootRestLocalRot = rootBone.LocalRotation.Value;
        var gs = rootBone.GlobalScale;
        _captureRootScale = MathF.Max((MathF.Abs(gs.x) + MathF.Abs(gs.y) + MathF.Abs(gs.z)) / 3f, 1e-4f);

        var list = new List<Particle>(bones.Count + 4);
        var indexOf = new Dictionary<Slot, int>(bones.Count);

        for (int i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            int parentIndex = -1;
            if (i > 0)
            {
                // Parent particle = nearest ancestor that is part of the chain.
                for (var p = bone.Parent; p != null; p = p.Parent)
                {
                    if (indexOf.TryGetValue(p, out int pi)) { parentIndex = pi; break; }
                }
                if (parentIndex < 0)
                    continue; // stray bone outside the chain tree - skip it
            }

            var particle = new Particle
            {
                Bone = bone,
                ParentIndex = parentIndex,
                Pos = bone.GlobalPosition,
                PrevPos = bone.GlobalPosition,
                Rot = bone.GlobalRotation,
                RotOffset = floatQ.Identity,
            };

            if (parentIndex >= 0)
            {
                var parentBone = list[parentIndex].Bone!;
                float3 offset = bone.GlobalPosition - parentBone.GlobalPosition;
                particle.RestOffsetRootSpace = _captureRootRotInverse * offset;
                particle.Length = offset.Length;
                float3 dir = offset.LengthSquared > 1e-10f ? offset.Normalized : float3.Down;
                particle.RestDirParentSpace = parentBone.GlobalRotation.Inverse * dir;
                particle.RestRotParentSpace = parentBone.GlobalRotation.Inverse * bone.GlobalRotation;
            }

            indexOf[bone] = list.Count;
            list.Add(particle);
        }

        if (list.Count == 0)
            return;

        // Count children; leaves get a virtual end particle so the final real bone swings.
        var childCount = new int[list.Count];
        for (int i = 1; i < list.Count; i++)
            childCount[list[i].ParentIndex]++;

        if (SimulateTerminalBones.Value)
        {
            int realCount = list.Count;
            for (int i = 0; i < realCount; i++)
            {
                if (childCount[i] > 0)
                    continue;
                var leaf = list[i];
                var leafBone = leaf.Bone!;
                // Extend along the leaf's own rest direction by its segment length (or a nub).
                float length = leaf.ParentIndex >= 0 ? MathF.Max(leaf.Length, 0.01f) : 0.05f;
                float3 dirWorld = leaf.ParentIndex >= 0
                    ? leafBone.GlobalRotation * leaf.RestDirParentSpace
                    : leafBone.GlobalRotation * float3.Down;
                float3 endPos = leafBone.GlobalPosition + dirWorld * length;
                list.Add(new Particle
                {
                    Bone = null,
                    ParentIndex = i,
                    RestOffsetRootSpace = _captureRootRotInverse * (dirWorld * length),
                    RestDirParentSpace = leafBone.GlobalRotation.Inverse * dirWorld,
                    RestRotParentSpace = floatQ.Identity,
                    Length = length,
                    Pos = endPos,
                    PrevPos = endPos,
                    Rot = leafBone.GlobalRotation,
                    RotOffset = floatQ.Identity,
                });
            }
        }

        _particles = list.ToArray();
        _childCounts = new int[_particles.Length];
        for (int i = 1; i < _particles.Length; i++)
            _childCounts[_particles[i].ParentIndex]++;
        _wasSimulating = false;
        _effector = -1;
    }

    // Put every particle back on the skeleton it hangs off, at rest, with no velocity. Used whenever
    // the chain resumes after a gap, where carrying on from the stored pose would be a teleport.
    private void SeedParticlePositions()
    {
        var particles = _particles;
        if (particles == null)
            return;

        for (int i = 0; i < particles.Length; i++)
        {
            ref var p = ref particles[i];
            float3 position;
            if (p.Bone != null && !p.Bone.IsDestroyed)
            {
                position = p.Bone.GlobalPosition;
            }
            else if (p.ParentIndex >= 0)
            {
                var parentBone = particles[p.ParentIndex].Bone;
                float3 dir = parentBone != null && !parentBone.IsDestroyed
                    ? parentBone.GlobalRotation * p.RestDirParentSpace
                    : float3.Down;
                position = particles[p.ParentIndex].Pos + dir * p.Length;
            }
            else
            {
                position = p.Pos;
            }

            p.Pos = position;
            p.PrevPos = position;
            p.Vel = float3.Zero;
            p.RotOffset = floatQ.Identity;
        }
    }

    private void Simulate(DynamicBoneManager manager, float frameDelta)
    {
        var particles = _particles!;
        var rootBone = particles[0].Bone!;
        var rootParent = rootBone.Parent;
        if (rootParent == null)
            return;

        float dt = System.Math.Clamp(frameDelta, 1e-4f, 0.1f);
        float invDt = 1f / dt;

        _frameCollidePlayers = CollideWithPlayers.Value;
        _frameCollideOwn = CollideWithOwnBody.Value;
        _frameWearer = (_frameCollidePlayers || _frameCollideOwn) ? Slot?.ActiveUser : null;

        float inertia = System.Math.Clamp(Inertia.Value, 0f, 1f);
        float damping = MathF.Max(Damping.Value, 0f);
        float elasticity = MathF.Max(Elasticity.Value, 0f);
        float stiffnessRange = 1f - System.Math.Clamp(Stiffness.Value, 0f, 1f);
        float inertiaForce = InertiaForce.Value;

        // Base: the root particle is pinned to the chain root's ANIMATED pose (IK/trackers already
        // wrote it this frame). Orientation comes from the captured rest local rotation under the
        // live parent - the sim writes the root's actual rotation, so it can't be read back.
        particles[0].PrevPos = particles[0].Pos;
        particles[0].Pos = rootBone.GlobalPosition;
        floatQ liveRootRot = rootParent.GlobalRotation * _rootRestLocalRot;
        particles[0].Rot = liveRootRot;

        var gsNow = rootBone.GlobalScale;
        float scaleNow = MathF.Max((MathF.Abs(gsNow.x) + MathF.Abs(gsNow.y) + MathF.Abs(gsNow.z)) / 3f, 1e-4f);
        float stretchScale = scaleNow / _captureRootScale * MathF.Max(GlobalStretch.Value, 0.01f);

        float3 baseDelta = particles[0].Pos - particles[0].PrevPos;
        // First simulated frame after (re)build: don't slam the chain with a giant base delta.
        if (!_wasSimulating)
        {
            baseDelta = float3.Zero;
            _wasSimulating = true;
        }
        float3 carried = baseDelta * inertia;

        float3 externalForce = rootParent.GlobalRotation * LocalForce.Value + Gravity.Value;
        float particleRadius = BaseBoneRadius.Value * scaleNow;

        // Carry base motion through the chain (inertia = how rigidly the world motion is followed).
        float maxSegment = 0f;
        for (int i = 1; i < particles.Length; i++)
        {
            particles[i].PrevPos = particles[i].Pos + carried;
            particles[i].Pos += carried;
            maxSegment = MathF.Max(maxSegment, particles[i].Length);
        }

        PrepareCollisionShapes(manager, particleRadius, maxSegment * stretchScale);

        for (int i = 1; i < particles.Length; i++)
        {
            ref var p = ref particles[i];
            int parent = p.ParentIndex;

            float segLength = p.Length * stretchScale;
            // Rest offsets were captured in the root's rotation frame; the live root rotation carries
            // them, so the whole rest pose turns with the body. Anything hanging BELOW a caught bone
            // takes that bone's swing with it, or the far half of a held tail would spring back toward
            // the body while the near half followed the hand.
            floatQ restFrame = p.RotationRoot > 0
                ? particles[p.RotationRoot].RotOffset * liveRootRot
                : liveRootRot;
            float3 restOffset = restFrame * p.RestOffsetRootSpace * stretchScale;
            float3 restTarget = particles[parent].Pos + restOffset;

            if (p.Held)
                p.RotOffset = Lumora.Core.Components.Avatar.IK.FabrikSolver.FromToRotation(restOffset, p.Pos - particles[parent].Pos);

            // Spring toward rest + damping + base-motion force + gravity/wind.
            float3 force = (restTarget - p.Pos) * elasticity;
            float3 dampingForce = -p.Vel * damping;
            float maxDamp = p.Vel.Length * invDt;
            if (dampingForce.Length > maxDamp)
                dampingForce = dampingForce.LengthSquared > 1e-12f ? dampingForce.Normalized * maxDamp : float3.Zero;
            force += dampingForce + baseDelta * invDt * inertiaForce + externalForce;

            p.Vel += force * dt;
            p.Pos += p.Vel * dt;

            // A held bone answers to the hand, so the clamps that pull a particle back toward its rest
            // pose are skipped for the whole held path. They come back the moment it is let go.
            if (!p.Held)
            {
                // Stiffness: hard clamp on how far the particle may deviate from its rest target.
                if (stiffnessRange < 1f)
                {
                    float3 toRest = restTarget - p.Pos;
                    float dist = toRest.Length;
                    float allowed = segLength * stiffnessRange * 2f;
                    if (dist > allowed && dist > 1e-6f)
                        p.Pos += toRest / dist * (dist - allowed);
                }

                ResolveCollisions(ref p.Pos, particleRadius);

                // Rigid segment length to the parent particle.
                FixLength(particles, i, parent, segLength);
            }

            // Velocity from actual travel, clamped so corrections can't inject energy.
            float travel = (p.Pos - p.PrevPos).Length * invDt;
            if (p.Vel.Length > travel * 2f)
                p.Vel = p.Vel.LengthSquared > 1e-12f ? p.Vel.Normalized * (travel * 2f) : float3.Zero;
        }

        if (_effector > 0)
            SolveHeld(particles, stretchScale, particleRadius);
    }

    // Two passes and the chain is continuous on both sides of the hand: pull the held path UP from
    // the caught bone toward the root so it reaches the hand (never moving the root itself, which is
    // pinned to the skeleton), then relax every segment DOWN from the root so nothing is left
    // stretched. Whatever the hand cannot reach simply comes up short, which is what a tail that is
    // too short to reach does. -xlinka
    private void SolveHeld(Particle[] particles, float stretchScale, float particleRadius)
    {
        var anchorSlot = GrabberRef.Target?.Slot;
        if (anchorSlot == null || anchorSlot.IsRemoved)
            return;

        particles[_effector].Pos = anchorSlot.LocalPointToGlobal(GrabAnchor.Value);

        for (int i = _effector; i > 0; i--)
        {
            if (!particles[i].Held)
                continue;
            int parent = particles[i].ParentIndex;
            if (parent <= 0)
                continue; // the root particle stays where the skeleton put it
            FixLength(particles, parent, i, particles[i].Length * stretchScale);
        }

        for (int i = 1; i < particles.Length; i++)
        {
            ResolveCollisions(ref particles[i].Pos, particleRadius);
            FixLength(particles, i, particles[i].ParentIndex, particles[i].Length * stretchScale);
        }
    }

    // Move particle `index` so it sits exactly `length` from `anchor`, leaving `anchor` alone.
    private static void FixLength(Particle[] particles, int index, int anchor, float length)
    {
        float3 delta = particles[index].Pos - particles[anchor].Pos;
        float distance = delta.Length;
        if (distance > 1e-6f)
            particles[index].Pos += delta / distance * (length - distance);
    }

    // COLLISION SNAPSHOT
    //
    // Everything a particle needs to know about a collider is worked out ONCE a frame and copied into
    // a flat array: the collider's world matrix, its scaled radius, its world AABB. The per particle
    // pass is then arithmetic over that array and never touches a slot, a sync field or an interface
    // call. Before this, every particle went back through the collider component for its offset, its
    // global scale and its world matrix, to get an answer that cannot change mid-solve: measured at
    // 100ns a test against 8ns for the snapshot.
    //
    // The same pass throws out anything the chain cannot reach. A room of twenty people is sixty
    // hand and head colliders, and a tail on one side of it was testing every particle against every
    // one of them - all-pairs across the whole room. The chain's own bounds plus a motion margin cull
    // that down to the wearer's own body in a single AABB test per collider per frame. -xlinka
    private User? _frameWearer;
    private bool _frameCollidePlayers;
    private bool _frameCollideOwn;

    private DynamicBoneColliderShape[] _frameShapes = Array.Empty<DynamicBoneColliderShape>();
    private int _frameShapeCount;

    // Slack on the chain's bounds when culling: a particle cannot travel much more than a segment in
    // one step (the length constraint yanks it back) and a collider is a hand at arm speed. A quarter
    // of a metre on top of both is 15 m/s of closing speed at 60Hz, which nothing on a body does.
    private const float BroadphaseMotionMargin = 0.25f;

    private void PrepareCollisionShapes(DynamicBoneManager manager, float particleRadius, float maxSegment)
    {
        _frameShapeCount = 0;

        int staticCount = StaticColliders.Count;
        int playerCount = (_frameCollidePlayers || _frameCollideOwn) ? manager.PlayerColliderCount : 0;
        int capacity = staticCount + playerCount;
        if (capacity == 0)
            return;
        if (_frameShapes.Length < capacity)
            Array.Resize(ref _frameShapes, System.Math.Max(capacity, 8));

        var particles = _particles!;
        var min = particles[0].Pos;
        var max = min;
        for (int i = 1; i < particles.Length; i++)
        {
            var p = particles[i].Pos;
            min.x = MathF.Min(min.x, p.x); min.y = MathF.Min(min.y, p.y); min.z = MathF.Min(min.z, p.z);
            max.x = MathF.Max(max.x, p.x); max.y = MathF.Max(max.y, p.y); max.z = MathF.Max(max.z, p.z);
        }
        float margin = particleRadius + maxSegment + BroadphaseMotionMargin;

        if (staticCount > 0)
        {
            foreach (var collider in StaticColliders)
            {
                if (collider == null || !collider.TryGetShape(out var shape))
                    continue;
                if (!shape.BoundsOverlap(in min, in max, margin))
                    continue;
                _frameShapes[_frameShapeCount++] = shape;
            }
        }

        if (playerCount == 0)
            return;

        var players = manager.PlayerColliders;
        for (int i = 0; i < playerCount; i++)
        {
            ref var shape = ref players[i];
            bool mine = _frameWearer != null && ReferenceEquals(shape.Owner, _frameWearer);
            if (mine ? !_frameCollideOwn : !_frameCollidePlayers)
                continue;
            if (!shape.BoundsOverlap(in min, in max, margin))
                continue;
            _frameShapes[_frameShapeCount++] = shape;
        }
    }

    private void ResolveCollisions(ref float3 position, float particleRadius)
    {
        var shapes = _frameShapes;
        for (int i = 0; i < _frameShapeCount; i++)
            shapes[i].Resolve(ref position, particleRadius);
    }

    private void WriteBones()
    {
        var particles = _particles!;
        var childCount = _childCounts!;

        // Swing each parent so its rest child direction points at the simulated child, then carry
        // rest rotations down. Single-child parents only (a branch fork keeps its rest rotation).
        for (int i = 1; i < particles.Length; i++)
        {
            int parent = particles[i].ParentIndex;
            if (childCount[parent] == 1)
            {
                float3 restDirWorld = particles[parent].Rot * particles[i].RestDirParentSpace;
                float3 simDir = particles[i].Pos - particles[parent].Pos;
                if (simDir.LengthSquared > 1e-10f && restDirWorld.LengthSquared > 1e-10f)
                {
                    floatQ swing = Lumora.Core.Components.Avatar.IK.FabrikSolver.FromToRotation(restDirWorld, simDir.Normalized);
                    particles[parent].Rot = swing * particles[parent].Rot;
                }
            }
            particles[i].Rot = particles[parent].Rot * particles[i].RestRotParentSpace;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            var bone = particles[i].Bone;
            if (bone != null && !bone.IsDestroyed)
                bone.SetGlobalRotationSilently(particles[i].Rot);
        }
    }

    // GRAB
    //
    // The hold is a constraint, not a reparent: nothing moves in the hierarchy, the caught particle
    // is simply pinned to a point in the hand each frame and the chain solves around it. That is why
    // the protocol is three values and no transform - there is no transform to steal.

    public bool IsGrabbed => GrabberRef.Target != null;

    public Grabber? Grabber => GrabberRef.Target;

    bool IGrabbable.Scalable => false;
    bool IGrabbable.Receivable => false;

    // Bones are reachable by hand only - there is nothing for a laser to hold onto out at range.
    bool IGrabbable.AllowOnlyPhysicalGrab => true;

    int IGrabbable.GrabPriority => GrabPriority.Value;
    bool IGrabbable.CanBeStolen => AllowSteal.Value;

    public int InteractionTargetPriority => InteractionPriority.Value;

    public event Action<IGrabbable>? OnLocalGrabbed;
    public event Action<IGrabbable>? OnLocalReleased;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        bool grabbable = AllowGrab.Value && (!IsGrabbed || AllowSteal.Value);
        return new InteractionDescription
        {
            Name = Slot?.SlotName.Value,
            Cursor = grabbable ? LaserCursor.Grab : LaserCursor.Disabled,
            ForceActivate = false,
        };
    }

    // PERMISSION GATE VIEW
    // Same shape as any other grabbable so the gate treats a bone grab as grab protocol rather than
    // as an edit of somebody else's avatar - which is exactly what it is not. There is no Transform
    // kind here because a bone grab never writes a transform: the chain is not reparented and the
    // solved rotations are local, silent writes that never reach the wire. -xlinka

    bool IPermissionGrabSurface.AllowsGrab => AllowGrab.Value;

    bool IPermissionGrabSurface.AllowsSteal => AllowSteal.Value;

    IPermissionActor? IPermissionGrabSurface.CurrentHolder => GrabberRef.Target?.OwningUser;

    RefID IGrabHolderSurface.GrabHolderRefId => GrabberRef.ReferenceID;

    GrabWriteKind IPermissionGrabSurface.ClassifyGrabWrite(IPermissionTarget? member)
    {
        if (ReferenceEquals(member, GrabberRef))
            return GrabWriteKind.HolderRef;
        if (ReferenceEquals(member, GrabbedBone) || ReferenceEquals(member, GrabAnchor))
            return GrabWriteKind.GrabState;
        return GrabWriteKind.None;
    }

    public bool CanGrab(Grabber grabber)
    {
        if (IsDestroyed || !Enabled.Value || !AllowGrab.Value)
            return false;
        if (_particles == null || _particles.Length < 2)
            return false;

        var current = GrabberRef.Target;
        if (current != null)
        {
            // Already held. Only takeable if the chain allows it, you are not already the holder, and
            // you are not stealing from your own other hand.
            if (!AllowSteal.Value) return false;
            if (ReferenceEquals(current, grabber)) return false;
            var holdingUser = current.OwningUser;
            if (holdingUser != null && ReferenceEquals(holdingUser, World?.LocalUser)) return false;
        }
        return true;
    }

    // Nearest catchable particle to a world point, or -1 when nothing is in reach. The reach is the
    // particle's own collision radius plus GrabRadius, both scaled by the avatar.
    public int FindGrabbableBone(float3 point, out float distance)
    {
        distance = float.MaxValue;
        var particles = _particles;
        if (particles == null || particles.Length < 2)
            return -1;

        float scale = 1f;
        var rootBone = particles[0].Bone;
        if (rootBone != null && !rootBone.IsDestroyed)
        {
            var gs = rootBone.GlobalScale;
            scale = MathF.Max((MathF.Abs(gs.x) + MathF.Abs(gs.y) + MathF.Abs(gs.z)) / 3f, 1e-4f);
        }
        float reach = (GrabRadius.Value + BaseBoneRadius.Value) * scale;

        int best = -1;
        float bestDistance = float.MaxValue;
        int first = GrabFirstBone.Value ? 0 : 1;
        for (int i = first; i < particles.Length; i++)
        {
            if (particles[i].Bone == null && !GrabTerminalBones.Value)
                continue;
            if (IsBoneUngrabbable(particles[i].Bone))
                continue;

            float d = (point - particles[i].Pos).Length;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        if (best < 0 || bestDistance > reach)
            return -1;
        distance = bestDistance;
        return best;
    }

    private bool IsBoneUngrabbable(Slot? bone)
    {
        if (bone == null || UngrabbableBones.Count == 0)
            return false;
        return UngrabbableBones.IndexOf(bone) >= 0;
    }

    // Entry point for the manager's hand poll. Routes through the hand's own Grabber so the hold is
    // recorded there like any other, which is what makes grip release let go of it.
    internal bool BeginHandGrab(Grabber grabber, int bone)
    {
        _pendingGrabBone = bone;
        bool grabbed = grabber.TryGrab(this);
        _pendingGrabBone = -1;
        return grabbed;
    }

    public IGrabbable Grab(Grabber grabber, Slot holdSlot, bool suppressEvents = false)
    {
        if (!CanGrab(grabber))
            return this;

        var hand = grabber.Slot;
        if (hand == null || hand.IsRemoved)
            return this;

        int bone = _pendingGrabBone >= 0 ? _pendingGrabBone : FindGrabbableBone(hand.GlobalPosition, out _);
        if (bone <= 0 && !(bone == 0 && GrabFirstBone.Value))
            return this;
        if (_particles == null || bone >= _particles.Length)
            return this;

        var prior = GrabberRef.Target;
        if (prior != null && !ReferenceEquals(prior, grabber))
            prior.NotifyStolen(this);

        // Optimistic on a client, authoritative on the host - same deal as a prop. The bypass is so a
        // guest can write the holder of a chain it does not own; the host still arbitrates.
        using (World?.DataModelPermissions?.EnterSystemBypass())
        {
            GrabberRef.Target = grabber;
            GrabbedBone.Value = bone;
            GrabAnchor.Value = hand.GlobalPointToLocal(_particles[bone].Pos);
        }
        _lastKnownHolder = grabber;
        _handMotion.Reset();

        UpdateEffector();
        if (!suppressEvents)
            OnLocalGrabbed?.Invoke(this);
        return this;
    }

    public void Release(Grabber grabber, bool suppressEvents = false)
    {
        // Only the recorded holder can let go. A client that was already stolen from must not be able
        // to yank the chain out of the new holder's hand with a stale release.
        if (!ReferenceEquals(GrabberRef.Target, grabber))
            return;

        using (World?.DataModelPermissions?.EnterSystemBypass())
        {
            GrabberRef.Target = null!;
            GrabbedBone.Value = -1;
        }
        _lastKnownHolder = null;

        if (!suppressEvents)
            OnLocalReleased?.Invoke(this);
    }

    private void OnHolderChanged(SyncRef<Grabber> reference)
    {
        if (reference.IsInInitPhase || reference.IsLoading)
            return;

        var newHolder = reference.Target;
        var oldHolder = _lastKnownHolder;
        _lastKnownHolder = newHolder;
        if (ReferenceEquals(oldHolder, newHolder))
            return;

        if (newHolder == null)
        {
            // Let go. Every peer runs this off its own sampled hand motion rather than off a
            // replicated velocity: each one has been watching that hand move for as long as the hold
            // lasted, and the wiggle was never synchronised in the first place. -xlinka
            ApplyReleaseVelocity();
            _effector = -1;
            MarkHeldPath(-1);
            _handMotion.Reset();
            return;
        }

        _handMotion.Reset();

        // The holder flipped to somebody else while WE were holding it: the host handed it off. Drop
        // the local hold so the hand stops claiming it. A normal release nulls the ref and is handled
        // above, so this only ever fires on a real steal.
        if (oldHolder != null && IsLocalGrabber(oldHolder))
        {
            oldHolder.NotifyStolen(this);
            OnLocalReleased?.Invoke(this);
        }
    }

    private bool IsLocalGrabber(Grabber grabber)
    {
        var owner = grabber.OwningUser;
        return owner != null && ReferenceEquals(owner, World?.LocalUser);
    }

    // Keep the local effector in step with the replicated bone index, and recompute which particles
    // sit on the path between the root and the caught one.
    private void UpdateEffector()
    {
        int wanted = GrabberRef.Target == null ? -1 : GrabbedBone.Value;
        if (_particles == null || wanted >= _particles.Length)
            wanted = -1;
        if (wanted == _effector)
            return;
        _effector = wanted;
        MarkHeldPath(wanted);
    }

    private void MarkHeldPath(int effector)
    {
        var particles = _particles;
        if (particles == null)
            return;

        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Held = false;
            particles[i].RotationRoot = 0;
            particles[i].RotOffset = floatQ.Identity;
        }
        if (effector < 0)
            return;

        for (int i = effector; i >= 0; i = particles[i].ParentIndex)
        {
            particles[i].Held = true;
            if (particles[i].ParentIndex < 0)
                break;
        }

        // Anything not on the held path inherits the nearest held ancestor, so a side branch or the
        // tail past the hand swings with what is actually holding it.
        for (int i = 1; i < particles.Length; i++)
        {
            if (particles[i].Held)
                continue;
            int parent = particles[i].ParentIndex;
            if (parent < 0)
                continue;
            particles[i].RotationRoot = particles[parent].Held ? parent : particles[parent].RotationRoot;
        }
    }

    private void SampleHandMotion()
    {
        if (_effector < 0)
            return;
        var hand = GrabberRef.Target?.Slot;
        if (hand == null || hand.IsRemoved)
            return;
        _handMotion.Sample(World?.Time.TotalTime ?? 0d, hand.GlobalPosition);
    }

    private void ApplyReleaseVelocity()
    {
        if (_particles == null || _effector < 0 || _effector >= _particles.Length)
            return;
        if (!_handMotion.Evaluate(out float3 velocity))
            return;

        velocity *= ReleaseVelocityScale.Value;
        if (velocity.LengthSquared < 1e-8f)
            return;

        // Seeded onto the path the hand was actually dragging. The rest of the chain picks the motion
        // up through the segment constraints on the next few frames, which is what a rope does.
        for (int i = 0; i <= _effector && i < _particles.Length; i++)
        {
            if (_particles[i].Held && i > 0)
                _particles[i].Vel = velocity;
        }
    }

    // Slipping and the give-up distance. Runs after the solve so it judges where things ended up.
    private void HandleGrabbing()
    {
        var grabber = GrabberRef.Target;
        if (grabber == null || _effector <= 0 || _particles == null)
            return;
        if (!IsLocalGrabber(grabber))
            return; // the holding peer owns these decisions

        var hand = grabber.Slot;
        if (hand == null || hand.IsRemoved)
            return;

        float3 anchor = hand.LocalPointToGlobal(GrabAnchor.Value);
        float distance = (_particles[_effector].Pos - anchor).Length;

        if (GrabSlipping.Value)
        {
            int next = System.Math.Min(_effector + 1, _particles.Length - 1);
            int previous = System.Math.Max(_effector - 1, 1);
            int slipTo = _effector;
            float nextDistance = (_particles[next].Pos - anchor).Length;
            float previousDistance = (_particles[previous].Pos - anchor).Length;
            if (nextDistance < distance && !IsBoneUngrabbable(_particles[next].Bone))
            {
                slipTo = next;
                distance = nextDistance;
            }
            if (previousDistance < distance && !IsBoneUngrabbable(_particles[previous].Bone))
            {
                slipTo = previous;
                distance = previousDistance;
            }
            if (slipTo != _effector)
            {
                using (World?.DataModelPermissions?.EnterSystemBypass())
                    GrabbedBone.Value = slipTo;
                UpdateEffector();
            }
        }

        float scale = 1f;
        var rootBone = _particles[0].Bone;
        if (rootBone != null && !rootBone.IsDestroyed)
        {
            var gs = rootBone.GlobalScale;
            scale = MathF.Max((MathF.Abs(gs.x) + MathF.Abs(gs.y) + MathF.Abs(gs.z)) / 3f, 1e-4f);
        }

        if (distance > GrabReleaseDistance.Value * scale)
            grabber.Release(this);
    }

    // Short rolling window of the holding hand's world position. Release velocity is not the last
    // frame's delta: the frame you let go on is the worst one to trust, because the button press
    // comes with a hand jerk. Average the pairwise velocities across the window instead, weighted by
    // how long each pair covered. Fixed ring, allocated once, no per-frame garbage. -xlinka
    private sealed class HandMotion
    {
        private const int Capacity = 8;
        private const float WindowSeconds = 0.12f;
        private const float MinStep = 1e-5f;

        private readonly double[] _time = new double[Capacity];
        private readonly float3[] _position = new float3[Capacity];
        private int _count;
        private int _next;

        public void Reset()
        {
            _count = 0;
            _next = 0;
        }

        public void Sample(double time, in float3 position)
        {
            if (_count > 0 && time - _time[(_next - 1 + Capacity) % Capacity] < MinStep)
                return;
            _time[_next] = time;
            _position[_next] = position;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }

        public bool Evaluate(out float3 velocity)
        {
            velocity = float3.Zero;
            if (_count < 2)
                return false;

            int oldest = (_next - _count + Capacity) % Capacity;
            double newest = _time[(_next - 1 + Capacity) % Capacity];

            float3 sum = float3.Zero;
            float total = 0f;
            for (int i = 0; i < _count - 1; i++)
            {
                int a = (oldest + i) % Capacity;
                int b = (oldest + i + 1) % Capacity;
                if (newest - _time[a] > WindowSeconds)
                    continue;
                float dt = (float)(_time[b] - _time[a]);
                if (dt < MinStep)
                    continue;
                sum += _position[b] - _position[a];
                total += dt;
            }

            if (total <= 0f)
                return false;
            velocity = sum / total;
            return true;
        }
    }
}
