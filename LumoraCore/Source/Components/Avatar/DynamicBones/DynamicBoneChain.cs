// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Secondary bone motion for hair, tails, ears, clothes: a particle chain rides the animated
/// skeleton and lags, springs and collides believably. Set Root (chain auto-builds from its
/// descendants) or fill Bones explicitly; tune Inertia/Damping/Elasticity/Stiffness like the
/// classic dynamic/spring-bone parameters avatar creators already know.
///
/// Simulation is LOCAL on every peer (bones are already posed identically from replicated proxies,
/// so broadcasting the wiggle would only add churn) and runs AFTER the IK solve each frame. Writes
/// are silent rotation-only swings, so skinning stays scale-safe. -xlinka
/// </summary>
[ComponentCategory("Physics/Dynamic Bones")]
[DefaultUpdateOrder(-4000)] // after AvatarIK (-5000) so chains ride the solved pose
public class DynamicBoneChain : Component, IInputUpdateReceiver
{
    /// <summary>Chain root bone; descendants become the chain when Bones is empty.</summary>
    public readonly SyncRef<Slot> Root;

    /// <summary>Explicit chain bones (depth-first, root first). Leave empty to auto-build from Root.</summary>
    public readonly SyncRefList<Slot> Bones;

    /// <summary>0..1: how rigidly base motion carries the chain (1 = no world-space lag).</summary>
    public readonly Sync<float> Inertia;

    /// <summary>Force applied by base motion (the whip when the base moves fast).</summary>
    public readonly Sync<float> InertiaForce;

    /// <summary>Velocity damping.</summary>
    public readonly Sync<float> Damping;

    /// <summary>Spring pull toward the rest pose.</summary>
    public readonly Sync<float> Elasticity;

    /// <summary>0..1 hard limit on deviation from the rest pose (1 = rigid).</summary>
    public readonly Sync<float> Stiffness;

    /// <summary>Give leaf bones a virtual end particle so the last real bone swings too.</summary>
    public readonly Sync<bool> SimulateTerminalBones;

    /// <summary>Collision radius of each particle (scaled by the avatar).</summary>
    public readonly Sync<float> BaseBoneRadius;

    /// <summary>World-space gravity on the chain.</summary>
    public readonly Sync<float3> Gravity;

    /// <summary>Constant force in the chain root's local frame (wind, tail curl).</summary>
    public readonly Sync<float3> LocalForce;

    /// <summary>Uniform length multiplier on the whole chain.</summary>
    public readonly Sync<float> GlobalStretch;

    /// <summary>Colliders the chain pushes out of.</summary>
    public readonly SyncRefList<IDynamicBoneCollider> StaticColliders;

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
    }

    private struct Particle
    {
        public Slot? Bone;            // null = virtual terminal extension
        public int ParentIndex;
        public int ChildCount;
        public float3 RestOffsetRootSpace; // offset from parent particle, in the ROOT's capture rotation frame
        public float3 RestDirParentSpace;  // normalized offset in the PARENT bone's capture rotation frame
        public floatQ RestRotParentSpace;  // bone rotation relative to parent bone at capture
        public float Length;               // capture-time world segment length
        public float3 Pos;
        public float3 PrevPos;
        public float3 Vel;
        public floatQ Rot;                 // solved world rotation this frame
    }

    private Particle[]? _particles;
    private int[]? _childCounts;
    private floatQ _captureRootRotInverse;
    private floatQ _rootRestLocalRot;
    private float _captureRootScale = 1f;
    private bool _registered;
    private bool _needsRebuild = true;
    private bool _wasSimulating;

    public override void OnAwake()
    {
        base.OnAwake();
        Root.OnChanged += _ => _needsRebuild = true;
        Bones.OnChanged += _ => _needsRebuild = true;
    }

    public override void OnStart()
    {
        base.OnStart();
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
        base.OnDestroy();
    }

    public void BeforeInputUpdate() { }

    public void AfterInputUpdate()
    {
        if (!Enabled || IsDestroyed)
        {
            _wasSimulating = false;
            return;
        }

        if (_needsRebuild)
            BuildParticles();
        if (_particles == null || _particles.Length < 2)
            return;

        // A bone died (avatar re-equip, slot deletion): rebuild next frame rather than throwing.
        for (int i = 0; i < _particles.Length; i++)
        {
            if (_particles[i].Bone != null && _particles[i].Bone!.IsDestroyed)
            {
                _needsRebuild = true;
                return;
            }
        }

        Simulate();
        WriteBones();
    }

    /// <summary>Populate Bones from Root's descendant hierarchy (depth-first).</summary>
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
                });
            }
        }

        _particles = list.ToArray();
        _childCounts = new int[_particles.Length];
        for (int i = 1; i < _particles.Length; i++)
            _childCounts[_particles[i].ParentIndex]++;
        _wasSimulating = false;
    }

    private void Simulate()
    {
        var particles = _particles!;
        var rootBone = particles[0].Bone!;
        var rootParent = rootBone.Parent;
        if (rootParent == null)
            return;

        float dt = World?.Time.SmoothDelta ?? (1f / 60f);
        dt = System.Math.Clamp(dt, 1e-4f, 0.1f);
        float invDt = 1f / dt;

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
        for (int i = 1; i < particles.Length; i++)
        {
            particles[i].PrevPos = particles[i].Pos + carried;
            particles[i].Pos += carried;
        }

        for (int i = 1; i < particles.Length; i++)
        {
            ref var p = ref particles[i];
            int parent = p.ParentIndex;

            float segLength = p.Length * stretchScale;
            // Rest offsets were captured in the root's rotation frame; the live root rotation
            // carries them, so the whole rest pose turns with the body.
            float3 restTarget = particles[parent].Pos + liveRootRot * p.RestOffsetRootSpace * stretchScale;

            // Spring toward rest + damping + base-motion force + gravity/wind.
            float3 force = (restTarget - p.Pos) * elasticity;
            float3 dampingForce = -p.Vel * damping;
            float maxDamp = p.Vel.Length * invDt;
            if (dampingForce.Length > maxDamp)
                dampingForce = dampingForce.LengthSquared > 1e-12f ? dampingForce.Normalized * maxDamp : float3.Zero;
            force += dampingForce + baseDelta * invDt * inertiaForce + externalForce;

            p.Vel += force * dt;
            p.Pos += p.Vel * dt;

            // Stiffness: hard clamp on how far the particle may deviate from its rest target.
            if (stiffnessRange < 1f)
            {
                float3 toRest = restTarget - p.Pos;
                float dist = toRest.Length;
                float allowed = segLength * stiffnessRange * 2f;
                if (dist > allowed && dist > 1e-6f)
                    p.Pos += toRest / dist * (dist - allowed);
            }

            // Push out of colliders.
            if (StaticColliders.Count > 0)
            {
                float3 pos = p.Pos;
                foreach (var collider in StaticColliders)
                {
                    collider?.ResolveParticle(ref pos, particleRadius);
                }
                p.Pos = pos;
            }

            // Rigid segment length to the parent particle.
            float3 seg = p.Pos - particles[parent].Pos;
            float segDist = seg.Length;
            if (segDist > 1e-6f)
                p.Pos += seg / segDist * (segLength - segDist);

            // Velocity from actual travel, clamped so corrections can't inject energy.
            float travel = (p.Pos - p.PrevPos).Length * invDt;
            if (p.Vel.Length > travel * 2f)
                p.Vel = p.Vel.LengthSquared > 1e-12f ? p.Vel.Normalized * (travel * 2f) : float3.Zero;
        }
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
}
