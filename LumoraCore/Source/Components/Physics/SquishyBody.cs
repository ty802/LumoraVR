// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Our own CPU soft body: a Verlet particle mesh with distance constraints, pressure, pinning and
/// collision against dynamic-bone colliders + a ground plane. Unlike Godot's Jolt soft body this
/// actually collides, holds pins, and can be grabbed - we own every step. Deforms a source mesh and
/// renders the result on a child DeformableMesh. Simulation is LOCAL per peer (visual), like dynamic
/// bones: same rest-anchored, no-drift model, runs after IK. -xlinka
/// </summary>
[ComponentCategory("Physics")]
[DefaultUpdateOrder(-4000)] // after AvatarIK (-5000), like dynamic bones
public class SquishyBody : Component, IInputUpdateReceiver
{
    /// <summary>Geometry source: a ProceduralMesh or MeshProvider. Read once for topology + rest verts.</summary>
    public readonly SyncRef<Component> SourceMesh;

    /// <summary>Material the deformed mesh renders with.</summary>
    public readonly AssetRef<MaterialAsset> Material;

    /// <summary>Spring stiffness toward rest edge lengths (constraint relaxation, 0..1).</summary>
    public readonly Sync<float> Stiffness;

    /// <summary>Velocity damping (0 = bouncy, higher = sluggish).</summary>
    public readonly Sync<float> Damping;

    /// <summary>Constraint solver iterations per frame (more = stiffer/stabler).</summary>
    public readonly Sync<int> Iterations;

    /// <summary>Internal pressure toward the rest volume (0 = floppy cloth, positive = holds a balloon).</summary>
    public readonly Sync<float> Pressure;

    /// <summary>
    /// Shape retention (0..1): each frame, pull particles toward their rest SHAPE. This is what makes
    /// a jelly "squishy but hold its form and never explode" - stable by construction (bounded pull, no
    /// runaway). 0 = floppy cloth; ~0.2 = a wobbly jelly; ~0.6 = a firm bouncy ball. -xlinka
    /// </summary>
    public readonly Sync<float> ShapeRetention;

    /// <summary>Gravity applied to free particles.</summary>
    public readonly Sync<float3> Gravity;

    /// <summary>Pin every rest vertex whose LOCAL Y is above this (cloth top edge). +Inf = pin none.</summary>
    public readonly Sync<float> PinAboveLocalY;

    /// <summary>Particle collision radius.</summary>
    public readonly Sync<float> ParticleRadius;

    /// <summary>Collide against these dynamic-bone colliders (spheres/capsules).</summary>
    public readonly SyncRefList<IDynamicBoneCollider> Colliders;

    /// <summary>Collide against a flat ground plane at this world Y (NaN disables).</summary>
    public readonly Sync<float> GroundY;

    /// <summary>Collide against ALL world geometry (raycast each moving particle). Drapes cloth over
    /// arbitrary meshes/colliders, not just the assigned dynamic-bone colliders. Costs a raycast per
    /// moving particle per frame.</summary>
    public readonly Sync<bool> CollideWithWorld;

    private struct Particle
    {
        public float3 Pos;
        public float3 Prev;
        public bool Pinned;
        public float3 RestLocal; // for pinned particles: rest offset in slot-local space (follows the slot)
    }

    private struct Edge
    {
        public int A;
        public int B;
        public float Rest;
    }

    private Particle[]? _particles;
    private Edge[]? _edges;
    private float[]? _invMass;         // 0 = pinned, 1 = free; lets the constraint solve run branchless
    private float3[]? _writeBuffer;   // local-space positions handed to the DeformableMesh
    private float3[]? _uploadedLocal; // last local positions actually pushed to the mesh (rest skip check)
    private float _restVolume;
    private float _avgEdge = 0.1f;    // velocity/collision scale, so tuning is mesh-size independent
    private float3[]? _restOffsets;   // each rest vertex relative to the rest centroid (shape-match goal)
    private readonly List<Slot> _selfExclude = new();
    private readonly List<Slot> _overlapScratch = new(); // broadphase: is anything near the body this frame?
    private DeformableMesh? _deformed;
    private bool _registered;
    private bool _built;
    private bool _wasSimulating;
    private int _readyRetries;

    // Sleep/wake: a settled body stops simulating AND stops re-uploading its mesh (the expensive part).
    // It wakes only when something can perturb it - its own slot moving, or one of its colliders moving.
    // This is the big perf win: a room full of resting cloth/jelly costs almost nothing. -xlinka
    private bool _awake = true;
    private bool _perturbedThisFrame;
    private float3 _lastSlotPos;
    private floatQ _lastSlotRot = floatQ.Identity;
    private float3[]? _lastColliderPos;
    private bool _worldNear;                              // broadphase: a world collider overlaps the body this frame
    private readonly List<Slot> _nearSlots = new();       // world colliders currently near - to detect one intruding
    private readonly List<float3> _nearPos = new();

    public SquishyBody()
    {
        SourceMesh = new SyncRef<Component>(this);
        Material = new AssetRef<MaterialAsset>(this);
        Stiffness = new Sync<float>(this, 0.6f);
        Damping = new Sync<float>(this, 0.02f);
        Iterations = new Sync<int>(this, 8);
        Pressure = new Sync<float>(this, 0f);
        ShapeRetention = new Sync<float>(this, 0f);
        Gravity = new Sync<float3>(this, new float3(0f, -9.81f, 0f));
        PinAboveLocalY = new Sync<float>(this, float.PositiveInfinity);
        ParticleRadius = new Sync<float>(this, 0.02f);
        Colliders = new SyncRefList<IDynamicBoneCollider>(this);
        GroundY = new Sync<float>(this, float.NaN);
        CollideWithWorld = new Sync<bool>(this, false);
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
        ArmReadyRetry();
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
        if (!_built)
            return;

        _perturbedThisFrame = CheckPerturbed();
        // Fully at rest and nothing can have disturbed it -> skip the sim AND the mesh upload entirely.
        if (!_awake && !_perturbedThisFrame)
            return;

        Simulate();
        WriteBack();
    }

    // Did anything that can move this body change since last frame? Its own slot transform (pinned
    // particles ride it) or any assigned collider's slot. A few cheap transform compares; returns true
    // the moment something moved so a sleeping body wakes on the same frame it's touched. -xlinka
    private bool CheckPerturbed()
    {
        bool moved = false;

        float3 pos = Slot.GlobalPosition;
        floatQ rot = Slot.GlobalRotation;
        if (float3.DistanceSquared(pos, _lastSlotPos) > 1e-8f)
            moved = true;
        else
        {
            float dot = _lastSlotRot.x * rot.x + _lastSlotRot.y * rot.y + _lastSlotRot.z * rot.z + _lastSlotRot.w * rot.w;
            if (MathF.Abs(dot) < 0.99999f)
                moved = true;
        }
        _lastSlotPos = pos;
        _lastSlotRot = rot;

        int count = Colliders.Count;
        if (_lastColliderPos == null || _lastColliderPos.Length != count)
        {
            _lastColliderPos = new float3[count];
            moved = true;
        }
        for (int i = 0; i < count; i++)
        {
            var slot = (Colliders[i] as Component)?.Slot;
            float3 cp = (slot != null && !slot.IsDestroyed) ? slot.GlobalPosition : float3.Zero;
            if (float3.DistanceSquared(cp, _lastColliderPos[i]) > 1e-8f)
                moved = true;
            _lastColliderPos[i] = cp;
        }

        // World collision wake: run the broadphase here too so a dormant body wakes the instant a world
        // collider (a grabbed/pushed object, or the player) enters its bounds or moves within them. Resting
        // on a STATIC box keeps the box in the near-set but unmoving -> no wake -> the body stays asleep.
        _worldNear = false;
        if (CollideWithWorld.Value && World?.Physics != null)
        {
            ComputeBodyAABB(MathF.Max(ParticleRadius.Value, 0f), out var bc, out var bs);
            _worldNear = World.Physics.OverlapBox(bc, bs, floatQ.Identity, _overlapScratch) > 0;
            if (NearSetChangedOrMoved())
                moved = true;
        }
        return moved;
    }

    // True if the set of nearby world colliders changed (one entered/left) or any of them moved since last
    // frame - i.e. something is intruding on or shifting against the body and it must wake. Rebuilds the
    // cached near-set as a side effect. -xlinka
    private bool NearSetChangedOrMoved()
    {
        bool changed = _overlapScratch.Count != _nearSlots.Count;
        if (!changed)
        {
            for (int i = 0; i < _overlapScratch.Count; i++)
            {
                var s = _overlapScratch[i];
                int idx = _nearSlots.IndexOf(s);
                if (idx < 0 || float3.DistanceSquared(s.GlobalPosition, _nearPos[idx]) > 1e-8f)
                {
                    changed = true;
                    break;
                }
            }
        }
        _nearSlots.Clear();
        _nearPos.Clear();
        for (int i = 0; i < _overlapScratch.Count; i++)
        {
            _nearSlots.Add(_overlapScratch[i]);
            _nearPos.Add(_overlapScratch[i].GlobalPosition);
        }
        return changed;
    }

    private void ArmReadyRetry()
    {
        if (IsDestroyed || World == null || _built)
            return;
        var source = ResolveSourceMesh();
        if (source != null)
        {
            Build(source.Value.positions, source.Value.indices, source.Value.uvs);
            return;
        }
        if (SourceMesh.Target == null || _readyRetries++ > 600)
            return;
        World.RunInUpdates(10, ArmReadyRetry);
    }

    private (float3[] positions, int[] indices, float2[]? uvs)? ResolveSourceMesh()
    {
        Phos.PhosMesh? mesh = SourceMesh.Target switch
        {
            Meshes.ProceduralMesh procedural => procedural.PhosMesh,
            MeshProvider provider => provider.Asset?.MeshData,
            _ => null
        };
        if (mesh == null || mesh.VertexCount == 0)
            return null;

        var positions = new float3[mesh.VertexCount];
        Array.Copy(mesh.RawPositions, positions, mesh.VertexCount);
        var uvs = mesh.HasUV0s ? (float2[]?)mesh.RawUV0s.Clone() : null;

        var indices = new List<int>();
        foreach (var submesh in mesh.Submeshes)
        {
            if (submesh.Topology != Phos.PhosTopology.Triangles)
                continue;
            var raw = submesh.RawIndices;
            for (int i = 0; i + 2 < submesh.IndexCount; i += 3)
            {
                indices.Add(raw[i]);
                indices.Add(raw[i + 1]);
                indices.Add(raw[i + 2]);
            }
        }
        if (indices.Count == 0)
            return null;
        return (positions, indices.ToArray(), uvs);
    }

    private void Build(float3[] localPositions, int[] indices, float2[]? uvs)
    {
        int n = localPositions.Length;
        float pinY = PinAboveLocalY.Value;

        _particles = new Particle[n];
        for (int i = 0; i < n; i++)
        {
            float3 world = Slot.LocalPointToGlobal(localPositions[i]);
            _particles[i] = new Particle
            {
                Pos = world,
                Prev = world,
                Pinned = localPositions[i].y > pinY,
                RestLocal = localPositions[i],
            };
        }

        // Unique undirected edges from the triangle list = the distance-constraint springs.
        var edgeSet = new Dictionary<long, Edge>();
        void AddEdge(int a, int b)
        {
            if (a == b) return;
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (edgeSet.ContainsKey(key)) return;
            edgeSet[key] = new Edge { A = a, B = b, Rest = float3.Distance(_particles[a].Pos, _particles[b].Pos) };
        }
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            AddEdge(indices[t], indices[t + 1]);
            AddEdge(indices[t + 1], indices[t + 2]);
            AddEdge(indices[t + 2], indices[t]);
        }
        _edges = new Edge[edgeSet.Count];
        edgeSet.Values.CopyTo(_edges, 0);

        float edgeSum = 0f;
        for (int e = 0; e < _edges.Length; e++)
            edgeSum += _edges[e].Rest;
        _avgEdge = _edges.Length > 0 ? MathF.Max(edgeSum / _edges.Length, 1e-3f) : 0.1f;

        _restIndices = indices;
        float3 restCentroid = Centroid();
        _restVolume = ComputeVolume(indices, restCentroid);
        _restOffsets = new float3[n];
        _invMass = new float[n];
        for (int i = 0; i < n; i++)
        {
            _restOffsets[i] = _particles[i].Pos - restCentroid;
            _invMass[i] = _particles[i].Pinned ? 0f : 1f;
        }
        _writeBuffer = new float3[n];
        _uploadedLocal = null; // force the first upload

        _awake = true;
        _lastColliderPos = null;
        _nearSlots.Clear();
        _nearPos.Clear();
        _lastSlotPos = Slot.GlobalPosition;
        _lastSlotRot = Slot.GlobalRotation;

        _selfExclude.Clear();
        _selfExclude.Add(Slot);

        // Render the deformed result on a child slot (the source mesh is data only, hidden by MeshHook).
        var deformedSlot = Slot.FindChildOrAdd("Deformed");
        _deformed = deformedSlot.GetComponent<DeformableMesh>() ?? deformedSlot.AttachComponent<DeformableMesh>();
        _deformed.SetGeometry((float3[])localPositions.Clone(), indices, uvs);
        var renderer = deformedSlot.GetComponent<MeshRenderer>() ?? deformedSlot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _deformed;
        if (Material.Target != null)
            renderer.Material.Target = Material.Target;

        _built = true;
        _wasSimulating = false;
    }

    private int[]? _restIndices;

    private float3 Centroid()
    {
        var particles = _particles!;
        float3 sum = float3.Zero;
        for (int i = 0; i < particles.Length; i++)
            sum += particles[i].Pos;
        return sum / MathF.Max(particles.Length, 1);
    }

    // Signed volume via the divergence theorem, tetrahedra from a LOCAL origin (the centroid) so the
    // sum stays numerically stable regardless of world position. -xlinka
    private float ComputeVolume(int[] indices, float3 origin)
    {
        var particles = _particles!;
        float vol = 0f;
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            var a = particles[indices[t]].Pos - origin;
            var b = particles[indices[t + 1]].Pos - origin;
            var c = particles[indices[t + 2]].Pos - origin;
            vol += float3.Dot(a, float3.Cross(b, c)) / 6f;
        }
        return MathF.Abs(vol);
    }

    private void Simulate()
    {
        var particles = _particles!;
        var edges = _edges!;
        float dt = World?.Time.SmoothDelta ?? (1f / 60f);
        dt = System.Math.Clamp(dt, 1e-4f, 0.05f);

        float damping = 1f - System.Math.Clamp(Damping.Value, 0f, 1f);
        float3 gravity = Gravity.Value;
        float radius = MathF.Max(ParticleRadius.Value, 0f);
        float groundY = GroundY.Value;
        bool hasGround = !float.IsNaN(groundY);

        // Max distance a particle may move in one frame = a couple rest-edge lengths. This velocity
        // clamp is the key stability fix: without it a stiff constraint or pressure spike accelerates a
        // particle unboundedly and the whole body stretches to infinity then NaNs out ("disappears").
        float maxMove = _avgEdge * 2f;
        bool wasSimulating = _wasSimulating;
        _wasSimulating = true;

        // Verlet integrate free particles; pinned ones ride the slot (so grabbing/moving carries them).
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].Pinned)
            {
                particles[i].Pos = Slot.LocalPointToGlobal(particles[i].RestLocal);
                particles[i].Prev = particles[i].Pos;
                continue;
            }
            var pos = particles[i].Pos;
            var vel = (pos - particles[i].Prev) * damping;
            // First frame after (re)build carries no velocity (avoid a launch); clamp velocity always.
            if (!wasSimulating)
                vel = float3.Zero;
            float vmag = vel.Length;
            if (vmag > maxMove)
                vel = vel * (maxMove / vmag);
            particles[i].Prev = pos;
            particles[i].Pos = pos + vel + gravity * (dt * dt);
            if (!IsFinite(particles[i].Pos))
            {
                particles[i].Pos = pos;
                particles[i].Prev = pos;
            }
        }

        // Pressure ONCE per frame (not per iteration - that was the explosion): a bounded outward push
        // to restore the rest volume. Ratio + push both clamped so a collapsed body can't blow up.
        float pressure = Pressure.Value;
        if (pressure > 0f && _restVolume > 1e-6f && _restIndices != null)
        {
            float3 centroid = Centroid();
            float vol = ComputeVolume(_restIndices, centroid);
            float ratio = vol > 1e-4f ? System.Math.Clamp(_restVolume / vol, 0.5f, 2f) : 1f;
            if (ratio > 1.001f)
            {
                float push = System.Math.Clamp((ratio - 1f) * pressure * 0.01f, 0f, _avgEdge * 0.5f);
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i].Pinned) continue;
                    var outward = particles[i].Pos - centroid;
                    float d = outward.Length;
                    if (d > 1e-5f)
                        particles[i].Pos += outward / d * push;
                }
            }
        }

        int iterations = System.Math.Clamp(Iterations.Value, 1, 32);
        float stiffness = System.Math.Clamp(Stiffness.Value, 0f, 1f);
        var invMass = _invMass!;
        for (int iter = 0; iter < iterations; iter++)
        {
            for (int e = 0; e < edges.Length; e++)
            {
                ref var edge = ref edges[e];
                float wa = invMass[edge.A];
                float wb = invMass[edge.B];
                float wsum = wa + wb;
                if (wsum < 1e-6f) // both ends pinned
                    continue;
                var pa = particles[edge.A].Pos;
                var pb = particles[edge.B].Pos;
                var delta = pb - pa;
                float dist = delta.Length;
                if (dist < 1e-6f)
                    continue;
                // Weighted PBD: split the correction by inverse mass (pinned = 0 -> immovable). Branchless.
                var corr = delta * ((dist - edge.Rest) / dist * stiffness / wsum);
                particles[edge.A].Pos = pa + corr * wa;
                particles[edge.B].Pos = pb - corr * wb;
            }
        }

        // SHAPE MATCHING: pull each free particle toward its rest shape (rest offset from the current
        // centroid). This is the stable "squishy but holds its form, never explodes" force - the goal is
        // bounded (the rest shape) so it can't run away like pressure did. Gravity/collision deform the
        // body; this springs it back. Translation-only for now (no rotation), which is perfect for a
        // blob that squishes and rebounds; add polar-decomposition rotation later for a rolling jelly.
        float shape = System.Math.Clamp(ShapeRetention.Value, 0f, 1f);
        if (shape > 0f && _restOffsets != null)
        {
            float3 c = Centroid();
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].Pinned) continue;
                var goal = c + _restOffsets[i];
                particles[i].Pos += (goal - particles[i].Pos) * shape;
            }
        }

        // ONE collision pass after the constraints settle: world colliders (resting-contact push-out),
        // dynamic-bone colliders, then the ground plane LAST (so it always wins - the body can never sink
        // through it). Any particle that was pushed has its velocity KILLED (Prev = Pos): that's what lets
        // it SETTLE and rest instead of endlessly re-penetrating and jittering. -xlinka
        // Broadphase gate (computed once in CheckPerturbed): only pay for hundreds of per-particle world
        // resolves if SOMETHING is actually near the body this frame. A cloth falling through empty air
        // does zero world queries.
        bool collideWorld = CollideWithWorld.Value && World?.Physics != null && _worldNear;

        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].Pinned)
                continue;
            var pos = particles[i].Pos;
            // NaN safety net: a runaway particle whose position went non-finite would slip past the
            // ground check (NaN < groundY is false) and vanish/fall through. Reset it onto the ground
            // (or its last good spot) so the body can never leak through the floor. -xlinka
            if (!IsFinite(pos))
            {
                pos = IsFinite(particles[i].Prev) ? particles[i].Prev : Slot.LocalPointToGlobal(particles[i].RestLocal);
                if (hasGround && pos.y < groundY + radius)
                    pos.y = groundY + radius;
                particles[i].Pos = pos;
                particles[i].Prev = pos;
                continue;
            }
            bool hitSurface = false;

            // Resting-contact push-out against EVERY world collider (box, orb, avatar capsule, mesh): if
            // the particle sphere overlaps anything, snap it to the surface. Unlike a movement raycast this
            // handles a stationary overlap, so the cloth drapes over and rests without sinking. -xlinka
            if (collideWorld && World!.Physics.ResolveSphere(pos, radius, _selfExclude, out var corrected, out _))
            {
                pos = corrected;
                hitSurface = true;
            }

            foreach (var collider in Colliders)
            {
                if (collider != null && collider.ResolveParticle(ref pos, radius))
                    hitSurface = true;
            }

            if (hasGround && pos.y - radius < groundY)
            {
                pos.y = groundY + radius;
                hitSurface = true;
            }

            particles[i].Pos = pos;
            if (hitSurface)
                particles[i].Prev = pos; // kill velocity -> settle with friction, no jitter/sink
        }

        // SLEEP: a particle barely moving this frame is at rest - zero its residual velocity so it
        // STOPS instead of buzzing forever. This is why real cloth settles; without it the constraint
        // + gravity equilibrium oscillates indefinitely (the endless jiggle). When EVERY free particle
        // is asleep the whole body goes dormant (see AfterInputUpdate) and stops costing anything. -xlinka
        float sleep = _avgEdge * 0.02f;
        float sleepSq = sleep * sleep;
        int awake = 0;
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].Pinned)
                continue;
            if (float3.DistanceSquared(particles[i].Pos, particles[i].Prev) < sleepSq)
                particles[i].Prev = particles[i].Pos;
            else
                awake++;
        }
        _awake = awake > 0;
    }

    private static bool IsFinite(float3 v)
        => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    // The body's world-space AABB grown by the particle radius - the broadphase box for world collision
    // and the sleep-wake intrusion check. -xlinka
    private void ComputeBodyAABB(float radius, out float3 center, out float3 size)
    {
        var particles = _particles!;
        float3 min = particles[0].Pos;
        float3 max = min;
        for (int i = 1; i < particles.Length; i++)
        {
            var p = particles[i].Pos;
            if (!IsFinite(p)) continue;
            min.x = MathF.Min(min.x, p.x); min.y = MathF.Min(min.y, p.y); min.z = MathF.Min(min.z, p.z);
            max.x = MathF.Max(max.x, p.x); max.y = MathF.Max(max.y, p.y); max.z = MathF.Max(max.z, p.z);
        }
        float3 pad = new float3(radius, radius, radius);
        min -= pad;
        max += pad;
        center = (min + max) * 0.5f;
        size = max - min;
    }

    private void WriteBack()
    {
        if (_deformed == null || _writeBuffer == null)
            return;
        var particles = _particles!;
        int n = particles.Length;

        // Convert to local and, in the same pass, detect whether anything actually moved since the last
        // upload. The mesh re-upload (ArrayMesh rebuild) is the single most expensive step per frame, so
        // when a body is settled we skip it entirely - a resting cloth/jelly does no GPU work. -xlinka
        float eps = _avgEdge * 0.01f;
        float epsSq = eps * eps;
        bool changed = _uploadedLocal == null || _uploadedLocal.Length != n;
        for (int i = 0; i < n; i++)
        {
            var local = Slot.GlobalPointToLocal(particles[i].Pos);
            _writeBuffer[i] = local;
            if (!changed && float3.DistanceSquared(local, _uploadedLocal![i]) > epsSq)
                changed = true;
        }
        if (!changed)
            return;

        _deformed.UpdatePositions(_writeBuffer);
        if (_uploadedLocal == null || _uploadedLocal.Length != n)
            _uploadedLocal = new float3[n];
        Array.Copy(_writeBuffer, _uploadedLocal, n);
    }
}
