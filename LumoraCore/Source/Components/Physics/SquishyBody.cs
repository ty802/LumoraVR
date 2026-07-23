// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Simulation.SoftBody;

namespace Lumora.Core.Components;

// Our own CPU soft body: a Verlet particle mesh with distance constraints, pressure, pinning and
// collision against dynamic-bone colliders + a ground plane. Unlike Godot's Jolt soft body this
// actually collides, holds pins, and can be grabbed - we own every step. Deforms a source mesh and
// renders the result on a child DeformableMesh. Simulation is LOCAL per peer (visual), like dynamic
// bones: same rest-anchored, no-drift model, runs after IK. -xlinka
[ComponentCategory("Physics")]
[DefaultUpdateOrder(-4000)] // after AvatarIK (-5000), like dynamic bones
public class SquishyBody : Component, IInputUpdateReceiver, ISoftBodySpace, ISoftBodyCollisionHandler
{
    // read once for topology + rest verts
    [Group("General")]
    public readonly SyncRef<Component> SourceMesh;

    public readonly AssetRef<MaterialAsset> Material;

    // 0..1; exponent-corrected per iteration so the same setting means the same stiffness at 4 or 16 iterations
    [Group("Dynamics")]
    public readonly Sync<float> Stiffness;

    // resistance to folding, 0..1: 0 = folds like wet paper, ~0.1-0.3 = fabric that holds a drape, higher = stiff sheet
    public readonly Sync<float> BendStiffness;

    // 0 = bouncy, higher = sluggish
    public readonly Sync<float> Damping;

    // 0..1; 1 = full stick on contact, lower keeps tangential sliding so cloth slips down slopes instead of gluing
    public readonly Sync<float> Friction;

    // applied per triangle projected on its facing (edge-on catches nothing, face-on catches everything),
    // so flags/capes billow instead of translating
    [Group("Wind")]
    public readonly Sync<float3> Wind;

    // 0 = perfectly steady wind, ~0.5 = natural gusting
    public readonly Sync<float> WindGustiness;

    // more = stiffer/stabler
    [Group("Simulation")]
    public readonly Sync<int> Iterations;

    // 0 = floppy cloth, positive = holds a balloon
    public readonly Sync<float> Pressure;

    // Shape retention (0..1): each frame, pull particles toward their rest SHAPE. This is what makes
    // a jelly "squishy but hold its form and never explode" - stable by construction (bounded pull, no
    // runaway). 0 = floppy cloth; ~0.2 = a wobbly jelly; ~0.6 = a firm bouncy ball. -xlinka
    public readonly Sync<float> ShapeRetention;

    public readonly Sync<float3> Gravity;

    // pins every rest vertex whose local Y is above this; +Inf = pin none
    [Group("Pinning")]
    public readonly Sync<float> PinAboveLocalY;

    [Group("Collision")]
    public readonly Sync<float> ParticleRadius;

    public readonly SyncRefList<IDynamicBoneCollider> Colliders;

    // world Y; NaN disables
    public readonly Sync<float> GroundY;

    // drapes cloth over arbitrary world geometry, not just the assigned dynamic-bone colliders;
    // costs a raycast per moving particle per frame
    public readonly Sync<bool> CollideWithWorld;

    // The simulation itself lives in LumoraSimulation. This component owns one solver, feeds it the
    // topology and the tunables, and answers its two questions: where is the anchor space, and what did
    // this particle just hit. Nothing about the maths lives here any more. -xlinka
    private readonly SoftBodySolver _solver = new();
    private float3[]? _writeBuffer;   // local-space positions handed to the DeformableMesh
    private float3[]? _uploadedLocal; // last local positions actually pushed to the mesh (rest skip check)
    private readonly List<Slot> _selfExclude = new();
    private readonly List<Slot> _overlapScratch = new(); // broadphase: is anything near the body this frame?
    private DeformableMesh? _deformed;
    private bool _registered;
    private bool _built;
    private int _readyRetries;

    // Sleep/wake: a settled body stops simulating AND stops re-uploading its mesh (the expensive part).
    // It wakes only when something can perturb it - its own slot moving, or one of its colliders moving.
    // This is the big perf win: a room full of resting cloth/jelly costs almost nothing. -xlinka
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
        BendStiffness = new Sync<float>(this, 0.15f);
        Damping = new Sync<float>(this, 0.02f);
        Friction = new Sync<float>(this, 1f);
        Wind = new Sync<float3>(this, float3.Zero);
        WindGustiness = new Sync<float>(this, 0.5f);
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
            _solver.ResetVelocities();
            return;
        }
        if (!_built)
            return;

        _perturbedThisFrame = CheckPerturbed();
        // Fully at rest and nothing can have disturbed it -> skip the sim AND the mesh upload entirely.
        if (!_solver.IsAwake && !_perturbedThisFrame)
            return;

        PushSolverParameters();
        float dt = World?.Time.SmoothDelta ?? (1f / 60f);
        _solver.Step(dt, World?.Time.TotalTime ?? 0.0, this, this);
        WriteBack();
    }

    // Did anything that can move this body change since last frame? Its own slot transform (pinned
    // particles ride it) or any assigned collider's slot. A few cheap transform compares; returns true
    // the moment something moved so a sleeping body wakes on the same frame it's touched. -xlinka
    private bool CheckPerturbed()
    {
        bool moved = false;

        // Wind is a continuous external force - a windy body never truly rests, and a dormant one must
        // wake the moment wind is switched on (nothing else in the wake model would notice it).
        if (Wind.Value.LengthSquared > 1e-8f)
            moved = true;

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
            _solver.ComputeBounds(MathF.Max(ParticleRadius.Value, 0f), out var bc, out var bs);
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

        // The solver derives the stretch edges, the bend cross-edges, the rest volume, the shape-match
        // offsets and the long-range attachment map from the topology; all this component supplies is the
        // geometry and where the pin line sits.
        PushSolverParameters();
        _solver.Build(localPositions, indices, PinAboveLocalY.Value, this);

        _writeBuffer = new float3[n];
        _uploadedLocal = null; // force the first upload

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
        _solver.ResetVelocities();
    }

    // cheap enough to call every step
    private void PushSolverParameters()
    {
        _solver.Stiffness = Stiffness.Value;
        _solver.BendStiffness = BendStiffness.Value;
        _solver.Damping = Damping.Value;
        _solver.Friction = Friction.Value;
        _solver.Wind = Wind.Value;
        _solver.WindGustiness = WindGustiness.Value;
        _solver.Iterations = Iterations.Value;
        _solver.Pressure = Pressure.Value;
        _solver.ShapeRetention = ShapeRetention.Value;
        _solver.Gravity = Gravity.Value;
        _solver.ParticleRadius = ParticleRadius.Value;
        _solver.GroundY = GroundY.Value;
    }

    // The solver's anchor space is this component's slot: pinned particles are rebuilt through it every
    // step, which is what carries a grabbed body along with whatever is holding it.
    float3 ISoftBodySpace.LocalPointToGlobal(in float3 local) => Slot.LocalPointToGlobal(local);

    float3 ISoftBodySpace.GlobalPointToLocal(in float3 global) => Slot.GlobalPointToLocal(global);

    // Collision back end for the solver. World geometry first, then the assigned dynamic-bone colliders;
    // the ground plane is the solver's own last word so nothing can push a particle back under it.
    //
    // The world pass is a resting-contact push-out rather than a movement raycast, which is what lets
    // cloth drape over a static box and STAY there instead of sinking through it one frame at a time.
    // It is gated on the broadphase computed in CheckPerturbed, so a body falling through empty air does
    // zero world queries. -xlinka
    bool ISoftBodyCollisionHandler.ResolveParticle(ref float3 position, float radius)
    {
        bool hit = false;

        if (CollideWithWorld.Value && _worldNear && World?.Physics != null
            && World.Physics.ResolveSphere(position, radius, _selfExclude, out var corrected, out _))
        {
            position = corrected;
            hit = true;
        }

        foreach (var collider in Colliders)
        {
            if (collider != null && collider.ResolveParticle(ref position, radius))
                hit = true;
        }
        return hit;
    }

    private void WriteBack()
    {
        if (_deformed == null || _writeBuffer == null)
            return;

        // The solver converts to local and reports whether anything actually moved since the last upload.
        // The mesh re-upload (ArrayMesh rebuild) is the single most expensive step per frame, so when a
        // body is settled we skip it entirely - a resting cloth/jelly does no GPU work. -xlinka
        if (_solver.WriteLocalPositions(this, _writeBuffer, ref _uploadedLocal))
            _deformed.UpdatePositions(_writeBuffer);
    }
}
