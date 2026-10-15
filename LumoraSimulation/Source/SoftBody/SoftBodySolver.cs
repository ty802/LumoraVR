// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Simulation.SoftBody;

// CPU soft body: a Verlet particle mesh with distance constraints, bending constraints, long-range
// attachment, pressure, shape matching, wind and contact friction. Knows nothing about worlds, slots
// or rendering - it is handed a topology at build time, a delta time and an anchor space per step, and
// it produces particle positions.
//
// One step, in order:
//   1. accumulate per-triangle wind on the air velocity relative to the cloth, projected on each
//      triangle's facing and spread over each particle's share of the rest area
//   2. Verlet integrate the free particles; pinned ones are rebuilt from the anchor space
//   3. one bounded pressure push toward the rest volume
//   4. N relaxation passes over the stretch edges, then the bend edges
//   5. long-range attachment clamp against the nearest pin
//   6. shape matching toward the rest shape, rotated to best fit for a free body
//   7. one collision pass, ground last, with a friction-aware contact response
//   8. sleep check: particles slow for long enough get the residual velocity zeroed
//
// Every constant and ordering choice in here was tuned against real cloth and jelly and is load
// bearing. The reasons are in the comments at each step; they are not decoration. -xlinka
public sealed class SoftBodySolver
{
    private struct Particle
    {
        public float3 Pos;
        public float3 Prev;
        public bool Pinned;

        // For pinned particles: rest offset in anchor-space, so they follow the anchor.
        public float3 RestLocal;
    }

    private struct Edge
    {
        public int A;
        public int B;
        public float Rest;

        // Share of the correction each end takes, fixed at build from the pin state: a free-free edge
        // splits it in half, a pinned end takes none. Baked here so the inner loop is one sqrt, one
        // divide and two scaled adds with nothing to look up. Edges with both ends pinned never enter
        // the list at all. -xlinka
        public float Ka;
        public float Kb;
    }

    private Particle[]? _particles;
    private Edge[]? _edges;
    private Edge[]? _bendEdges;       // cross-edge constraints over each pair of adjacent triangles
    private int[]? _restIndices;
    private float3[]? _restOffsets;   // each rest vertex relative to the rest centroid (shape-match goal)
    private int[]? _lraPin;
    private float[]? _lraRest;
    private float3[]? _windAccum;     // per-particle wind force accumulator, allocated only while wind blows
    private float[]? _restArea;       // each particle's third of every rest triangle it touches
    private byte[]? _slowSteps;       // consecutive steps each particle has spent under the sleep speed
    private float _restVolume;
    private float[]? _pull;           // per-particle share of the way to its goal taken each step; null = none
    private float3[]? _goal;          // per-particle goal this frame, world space; null = the rest shape through the anchor
    private bool _goalMoved;          // the rest lengths still describe the previous goal
    private float _avgEdge = 0.1f;    // velocity/collision scale, so tuning is mesh-size independent
    private floatQ _shapeRotation = floatQ.Identity;
    private float _lastDt = 1f / 60f;
    private float _stepDt = 1f / 60f;
    private bool _wasSimulating;
    private int _pinnedCount;

    // TUNABLES

    // Spring stiffness toward rest edge lengths (0..1), corrected to be iteration-independent.
    public float Stiffness = 0.6f;

    // Resistance to folding (0..1) via cross-edge constraints over adjacent triangle pairs.
    public float BendStiffness = 0.15f;

    // Share of velocity lost every sixtieth of a second (0 = bouncy, higher = sluggish), so the same
    // value bleeds the same energy per second at 60, 90 or 144 Hz.
    public float Damping = 0.02f;

    // Contact friction (0..1). 1 is full stick on contact; lower keeps tangential sliding.
    public float Friction = 1f;

    // World-space air velocity. A still sheet face-on to it accelerates at its length in m/s^2; a sheet
    // already moving with the air feels nothing, so the same term is the drag that makes it flutter.
    public float3 Wind = float3.Zero;

    // Wind gust strength (0 = perfectly steady, ~0.5 = natural gusting).
    public float WindGustiness = 0.5f;

    // Constraint solver iterations per step.
    public int Iterations = 8;

    // Internal pressure toward the rest volume (0 = floppy cloth, positive = holds a balloon).
    public float Pressure;

    // Shape retention (0..1): how hard particles are pulled back toward the rest shape.
    public float ShapeRetention;

    public float3 Gravity = new(0f, -9.81f, 0f);

    public float ParticleRadius = 0.02f;

    // Flat ground plane height. NaN disables it.
    public float GroundY = float.NaN;

    // Something that can push the body is within reach. Nothing sleeps meanwhile, so a particle resting
    // against a hand still answers the moment the hand moves.
    public bool CollidersNear;

    // Air coupling, per second: a sheet face-on to the wind accelerates at this times the relative air
    // speed along its facing.
    private const float AirCoupling = 1f;

    // A particle sleeps after this many steps in a row under the sleep speed. A count of steps rather
    // than one slow step, so the turning point of a swing, where the speed passes through zero, does not
    // stop the swing dead. -xlinka
    private const int SleepSteps = 30;
    private const float SleepSpeed = 0.02f;

    // STATE

    public bool IsBuilt => _particles != null;

    public int ParticleCount => _particles?.Length ?? 0;

    public int EdgeCount => _edges?.Length ?? 0;

    public int BendEdgeCount => _bendEdges?.Length ?? 0;

    public int PinnedCount => _pinnedCount;

    // Average rest edge length. Every internal threshold is scaled by it.
    public float AverageEdgeLength => _avgEdge;

    // False once every free particle has settled. The host can then skip stepping entirely.
    public bool IsAwake { get; private set; } = true;

    // World-space position of one particle.
    public float3 GetPosition(int index) => _particles![index].Pos;

    // Wake a sleeping body, e.g. because something that can disturb it has moved. The slow counts start
    // over so it does not drop straight back to sleep on its first quiet step. Called by the host once per
    // frame it steps a dormant body, never per substep: a body stepped twice a frame that fell asleep on
    // the first substep would have its count cleared on the second, every time, and never go dormant.
    public void Wake()
    {
        if (!IsAwake && _slowSteps != null)
            Array.Clear(_slowSteps, 0, _slowSteps.Length);
        IsAwake = true;
    }

    // Forget the accumulated velocity, so the next step starts from rest without a launch.
    public void ResetVelocities() => _wasSimulating = false;

    // Put every particle back on its rest position through the anchor, at rest. Used when a body
    // resumes after a gap it sat out - a distance pause, a disabled frame, a world that was in the
    // background. Carrying on from the stored pose would be a teleport: the anchor may be fifty metres
    // and a minute away by now, and the pins would snap to it while the free particles tried to catch
    // up from wherever they were parked, which reads as the garment being fired at its owner. -xlinka
    public void ResetToRest(ISoftBodySpace space)
    {
        var particles = _particles;
        if (particles == null)
            return;

        var goal = _goal;
        for (int i = 0; i < particles.Length; i++)
        {
            var world = goal != null ? goal[i] : space.LocalPointToGlobal(particles[i].RestLocal);
            particles[i].Pos = world;
            particles[i].Prev = world;
        }
        if (_slowSteps != null)
            Array.Clear(_slowSteps, 0, _slowSteps.Length);
        _shapeRotation = floatQ.Identity;
        _wasSimulating = false;
        IsAwake = true;
    }

    // BUILD

    // Build the constraint sets from a triangle mesh. Vertices above pinAboveLocalY in anchor-local
    // space are pinned. The particle order matches the input vertex order, so the host can write
    // results straight back onto the same mesh.
    public void Build(float3[] localPositions, int[] indices, float pinAboveLocalY, ISoftBodySpace space)
        => Build(localPositions, indices, pinAboveLocalY, null, space);

    // The same, with a hold per particle on top of the pin line: 1 pins it, anything between 0 and 1 is the share of
    // the way back to its goal it is pulled every step. The goal is its rest spot through the anchor, or whatever
    // SetGoals last handed in.
    public void Build(float3[] localPositions, int[] indices, float pinAboveLocalY, float[]? hold, ISoftBodySpace space)
    {
        int n = localPositions.Length;

        _particles = new Particle[n];
        _pinnedCount = 0;
        _pull = null;
        _goal = null;
        _goalMoved = false;
        for (int i = 0; i < n; i++)
        {
            var world = space.LocalPointToGlobal(localPositions[i]);
            bool pinned = localPositions[i].y > pinAboveLocalY;
            if (!pinned && hold != null && i < hold.Length)
            {
                if (hold[i] >= 1f)
                {
                    pinned = true;
                }
                else if (hold[i] > 0f)
                {
                    _pull ??= new float[n];
                    _pull[i] = hold[i];
                }
            }
            if (pinned)
                _pinnedCount++;
            _particles[i] = new Particle
            {
                Pos = world,
                Prev = world,
                Pinned = pinned,
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
            edgeSet[key] = MakeEdge(a, b);
        }
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            AddEdge(indices[t], indices[t + 1]);
            AddEdge(indices[t + 1], indices[t + 2]);
            AddEdge(indices[t + 2], indices[t]);
        }

        // The average is taken over EVERY unique edge, pinned rows included, because every threshold in
        // the solver hangs off it and it has to mean the same thing whatever gets pinned. The solve list
        // then drops the edges that cannot move anything.
        float edgeSum = 0f;
        foreach (var edge in edgeSet.Values)
            edgeSum += edge.Rest;
        _avgEdge = edgeSet.Count > 0 ? MathF.Max(edgeSum / edgeSet.Count, 1e-3f) : 0.1f;
        _edges = CollectSolvable(edgeSet.Values);

        // BENDING constraints: for every interior edge shared by two triangles, constrain the two OPPOSITE
        // vertices to their rest separation. Stretch edges alone fold like wet paper (zero resistance to
        // creasing); these cross-edges are what give fabric a drape and a jelly its resistance to shearing
        // flat. Same solver as stretch, just a second (softer) edge list. -xlinka
        var opposite = new Dictionary<long, int>();
        var bendList = new List<Edge>();
        void AddBend(int a, int b, int opp)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (opposite.TryGetValue(key, out int other))
            {
                if (other != opp)
                    bendList.Add(MakeEdge(other, opp));
            }
            else
            {
                opposite[key] = opp;
            }
        }
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            AddBend(indices[t], indices[t + 1], indices[t + 2]);
            AddBend(indices[t + 1], indices[t + 2], indices[t]);
            AddBend(indices[t + 2], indices[t], indices[t + 1]);
        }
        _bendEdges = CollectSolvable(bendList);

        _restIndices = indices;
        _restArea = new float[n];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            var a = _particles[indices[t]].Pos;
            float third = float3.Cross(_particles[indices[t + 1]].Pos - a, _particles[indices[t + 2]].Pos - a).Length * (0.5f / 3f);
            _restArea[indices[t]] += third;
            _restArea[indices[t + 1]] += third;
            _restArea[indices[t + 2]] += third;
        }
        _slowSteps = new byte[n];
        var restCentroid = Centroid();
        _restVolume = ComputeVolume(indices, restCentroid);
        _restOffsets = new float3[n];
        for (int i = 0; i < n; i++)
            _restOffsets[i] = _particles[i].Pos - restCentroid;

        // Long-range attachment: nearest pin (rest-space distance) per free particle. Rest-space euclidean
        // is an approximation of the true geodesic, but for sheets/blobs it's within a few percent and it
        // never OVER-constrains a straight drop, which is the case that matters visually. -xlinka
        _lraPin = null;
        _lraRest = null;
        var pinIdx = new List<int>();
        for (int i = 0; i < n; i++)
            if (_particles[i].Pinned) pinIdx.Add(i);
        if (pinIdx.Count > 0)
        {
            _lraPin = new int[n];
            _lraRest = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (_particles[i].Pinned) { _lraPin[i] = -1; continue; }
                int best = pinIdx[0];
                float bestSq = float.MaxValue;
                for (int p = 0; p < pinIdx.Count; p++)
                {
                    float dSq = float3.DistanceSquared(localPositions[i], localPositions[pinIdx[p]]);
                    if (dSq < bestSq) { bestSq = dSq; best = pinIdx[p]; }
                }
                _lraPin[i] = best;
                _lraRest[i] = MathF.Sqrt(bestSq);
            }
        }

        _shapeRotation = floatQ.Identity;
        _windAccum = null;
        _lastDt = 1f / 60f;
        _wasSimulating = false;
        IsAwake = true;
    }

    private Edge MakeEdge(int a, int b)
    {
        var particles = _particles!;
        float wa = particles[a].Pinned ? 0f : 1f;
        float wb = particles[b].Pinned ? 0f : 1f;
        float wsum = wa + wb;
        return new Edge
        {
            A = a,
            B = b,
            Rest = float3.Distance(particles[a].Pos, particles[b].Pos),
            Ka = wsum > 0f ? wa / wsum : 0f,
            Kb = wsum > 0f ? wb / wsum : 0f,
        };
    }

    // A pose to follow, one world point per particle, taken until the next call. Pinned particles sit on it, every
    // free one is pulled toward it by its hold, and lengths, the pin distances and the volume are measured off it,
    // so a skin that stretches as it bends is not fought back to the shape it had when the body was built. The
    // rigid shape match stands aside while a pose is followed; fold ShapeRetention into the holds instead.
    public void SetGoals(float3[] worldGoals)
    {
        var particles = _particles;
        if (particles == null || worldGoals.Length < particles.Length)
            return;
        if (_goal == null || _goal.Length != particles.Length)
            _goal = new float3[particles.Length];
        Array.Copy(worldGoals, _goal, particles.Length);
        _goalMoved = true;
    }

    // Back to the rest shape through the anchor.
    public void ClearGoals()
    {
        _goal = null;
        _goalMoved = false;
    }

    public bool FollowsGoals => _goal != null;

    // Holds can change after a build without touching the topology: 0 frees a particle's pull, anything up to 1
    // pulls it. A particle pinned at build stays pinned.
    public void SetPull(float[]? pull)
    {
        var particles = _particles;
        if (particles == null)
            return;
        if (pull == null)
        {
            _pull = null;
            return;
        }
        if (_pull == null || _pull.Length != particles.Length)
            _pull = new float[particles.Length];
        int n = System.Math.Min(pull.Length, particles.Length);
        for (int i = 0; i < n; i++)
            _pull[i] = System.Math.Clamp(pull[i], 0f, 1f);
    }

    private void RefreshRestFromGoals()
    {
        var goal = _goal!;
        RefreshEdgeRests(_edges, goal);
        RefreshEdgeRests(_bendEdges, goal);

        var lraPin = _lraPin;
        var lraRest = _lraRest;
        if (lraPin != null && lraRest != null)
        {
            for (int i = 0; i < lraPin.Length; i++)
            {
                int p = lraPin[i];
                if (p >= 0)
                    lraRest[i] = float3.Distance(goal[i], goal[p]);
            }
        }

        if (Pressure > 0f && _restIndices != null)
        {
            var sum = float3.Zero;
            for (int i = 0; i < goal.Length; i++)
                sum += goal[i];
            var origin = sum / MathF.Max(goal.Length, 1);
            var idx = _restIndices;
            float vol = 0f;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                var a = goal[idx[t]] - origin;
                var b = goal[idx[t + 1]] - origin;
                var c = goal[idx[t + 2]] - origin;
                vol += float3.Dot(a, float3.Cross(b, c)) / 6f;
            }
            _restVolume = MathF.Abs(vol);
        }
    }

    private static void RefreshEdgeRests(Edge[]? edges, float3[] goal)
    {
        if (edges == null)
            return;
        for (int e = 0; e < edges.Length; e++)
            edges[e].Rest = float3.Distance(goal[edges[e].A], goal[edges[e].B]);
    }

    // Only edges with at least one free end can move anything; the rest are skipped once here instead
    // of once per iteration per step forever.
    private static Edge[] CollectSolvable(IEnumerable<Edge> edges)
    {
        var list = new List<Edge>();
        foreach (var edge in edges)
        {
            if (edge.Ka + edge.Kb > 0f)
                list.Add(edge);
        }
        return list.ToArray();
    }

    // STEP

    // Advance one step. time is a monotonic clock used only for wind gusting, so it only has to be
    // consistent, not absolute.
    public void Step(float dt, double time, ISoftBodySpace space, ISoftBodyCollisionHandler? collision)
    {
        if (SolveConstraints(dt, time, space))
            ResolveContacts(space, collision);
    }

    // The pure half of a step: integration, wind, pressure and every constraint. The anchor is only read
    // through the space it is handed, so a host that hands in a frozen copy of its transform can run this
    // half on any thread. False when there is nothing to step. -xlinka
    public bool SolveConstraints(float dt, double time, ISoftBodySpace space)
    {
        if (_particles == null || _edges == null)
            return false;

        var particles = _particles;
        var edges = _edges;
        dt = System.Math.Clamp(dt, 1e-4f, 0.05f);
        _stepDt = dt;
        // Time-corrected Verlet: (pos - prev) is last frame's displacement, i.e. velocity * LAST dt. Scale
        // by dt/lastDt or a frame-time fluctuation reads as a velocity change and the body visibly pulses
        // with the frame rate. Ratio clamped so one hitch frame can't triple the velocity. -xlinka
        float prevDt = MathF.Max(_lastDt, 1e-4f);
        float dtRatio = System.Math.Clamp(dt / prevDt, 0.5f, 2f);
        _lastDt = dt;

        // Damping is stated per sixtieth of a second and raised to the steps actually taken, or a 120 Hz
        // headset bleeds twice the energy a 60 Hz one does from the same setting.
        float damping = MathF.Pow(1f - System.Math.Clamp(Damping, 0f, 1f), dt * 60f);
        var gravity = Gravity;

        // Speed cap in PER-SECOND terms (equivalent to the old 2-edges-per-frame cap at 60fps). A per-frame
        // cap silently changes terminal velocity with the frame rate. Still the key stability fix: without
        // it a stiff constraint or pressure spike accelerates a particle unboundedly and the whole body
        // stretches to infinity then NaNs out ("disappears").
        float maxMove = _avgEdge * 120f * dt;
        bool wasSimulating = _wasSimulating;
        _wasSimulating = true;

        var goal = _goal;
        if (goal != null && _goalMoved)
        {
            _goalMoved = false;
            RefreshRestFromGoals();
        }

        // WIND: per-triangle force on the air velocity RELATIVE to the triangle, projected on its facing,
        // so a sheet edge-on to the wind catches nothing, a face-on one billows, and a part already moving
        // with the air stops being pushed, which is the drag that turns a lean into a flutter. Each
        // particle's sum is divided by its share of the rest area, so Wind is an acceleration and a finer
        // mesh of the same sheet catches the same breeze. Gusting = two incommensurate sines, never
        // periodic-looking. -xlinka
        var wind = Wind;
        var restArea = _restArea;
        bool hasWind = wind.LengthSquared > 1e-8f && _restIndices != null && restArea != null;
        if (hasWind)
        {
            if (_windAccum == null || _windAccum.Length != particles.Length)
                _windAccum = new float3[particles.Length];
            else
                Array.Clear(_windAccum, 0, _windAccum.Length);

            float t = (float)time;
            float gust = 1f + System.Math.Clamp(WindGustiness, 0f, 2f)
                * (0.6f * MathF.Sin(t * 2.3f) + 0.4f * MathF.Sin(t * 5.9f + 1.7f));
            var w = wind * gust;
            // Verlet velocity is last step's displacement over last step's dt; none on a first step.
            float velScale = wasSimulating ? 1f / (3f * prevDt) : 0f;
            var idx = _restIndices!;
            for (int tri = 0; tri + 2 < idx.Length; tri += 3)
            {
                int i0 = idx[tri], i1 = idx[tri + 1], i2 = idx[tri + 2];
                var a = particles[i0].Pos;
                var n = float3.Cross(particles[i1].Pos - a, particles[i2].Pos - a) * 0.5f;
                float nLen = n.Length;
                if (nLen < 1e-8f) continue;
                var facing = n / nLen;
                var vel = (particles[i0].Pos - particles[i0].Prev
                    + particles[i1].Pos - particles[i1].Prev
                    + particles[i2].Pos - particles[i2].Prev) * velScale;
                // Signed projection kept: wind pushes a face from either side toward its lee.
                var f = facing * (float3.Dot(facing, w - vel) * AirCoupling * nLen * (1f / 3f));
                _windAccum[i0] += f;
                _windAccum[i1] += f;
                _windAccum[i2] += f;
            }
            for (int i = 0; i < particles.Length; i++)
            {
                float area = restArea![i];
                _windAccum[i] = area > 1e-8f ? _windAccum[i] / area : float3.Zero;
            }
        }

        // Verlet integrate free particles; pinned ones ride the anchor (so grabbing/moving carries them).
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].Pinned)
            {
                particles[i].Pos = goal != null ? goal[i] : space.LocalPointToGlobal(particles[i].RestLocal);
                particles[i].Prev = particles[i].Pos;
                continue;
            }
            var pos = particles[i].Pos;
            var vel = (pos - particles[i].Prev) * (damping * dtRatio);
            // First frame after (re)build carries no velocity (avoid a launch); clamp velocity always.
            if (!wasSimulating)
                vel = float3.Zero;
            float vmag = vel.Length;
            if (vmag > maxMove)
                vel = vel * (maxMove / vmag);
            var accel = gravity;
            if (hasWind)
                accel += _windAccum![i];
            particles[i].Prev = pos;
            particles[i].Pos = pos + vel + accel * (dt * dt);
            if (!IsFinite(particles[i].Pos))
            {
                particles[i].Pos = pos;
                particles[i].Prev = pos;
            }
        }

        // Pressure ONCE per frame (not per iteration - that was the explosion): a bounded outward push
        // to restore the rest volume. Ratio + push both clamped so a collapsed body can't blow up.
        float pressure = Pressure;
        if (pressure > 0f && _restVolume > 1e-6f && _restIndices != null)
        {
            var centroid = Centroid();
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

        int iterations = System.Math.Clamp(Iterations, 1, 32);
        // Iteration-independent stiffness: applying k per iteration compounds to 1-(1-k)^N, so the same
        // setting got stiffer every time someone raised Iterations. Solve for the per-iteration value that
        // compounds to the REQUESTED one and tuning finally means what it says. -xlinka
        float stiffness = CorrectedStiffness(Stiffness, iterations);
        float bend = CorrectedStiffness(BendStiffness, iterations);
        var bendEdges = _bendEdges;
        bool solveBend = bend > 0f && bendEdges != null && bendEdges.Length > 0;
        for (int iter = 0; iter < iterations; iter++)
        {
            SolveEdges(particles, edges, stiffness);
            if (solveBend)
                SolveEdges(particles, bendEdges!, bend);
        }

        // LONG-RANGE ATTACHMENT: clamp every free particle to at most its rest distance (small slack) from
        // its nearest pin. Edge relaxation alone ALWAYS sags under gravity - each iteration only fixes one
        // ring of edges - so pinned cloth stretched like taffy. This is a direct, unconditionally stable
        // clamp that makes it read inextensible at any iteration count. One-sided: it never pushes IN, so
        // slack cloth hangs naturally. -xlinka
        if (_lraPin != null && _lraRest != null)
        {
            for (int i = 0; i < particles.Length; i++)
            {
                int p = _lraPin[i];
                if (p < 0) continue;
                float allowed = _lraRest[i] * 1.02f;
                var d = particles[i].Pos - particles[p].Pos;
                float dist = d.Length;
                if (dist > allowed && dist > 1e-6f)
                    particles[i].Pos = particles[p].Pos + d * (allowed / dist);
            }
        }

        // SHAPE MATCHING: pull each free particle toward its rest shape. Stable by construction (the goal
        // is bounded) so it can't run away like pressure did. For a fully-free body the goal shape is now
        // ROTATED to the best-fit orientation (polar extraction below), so a jelly tumbles and rolls
        // instead of being dragged back to its spawn orientation. Pinned bodies keep the translation-only
        // goal: the pins already define the orientation and fighting them just fights the solver. -xlinka
        float shape = System.Math.Clamp(ShapeRetention, 0f, 1f);
        if (shape > 0f && _restOffsets != null && goal == null)
        {
            var c = Centroid();
            bool rotate = _lraPin == null; // any pins -> translation-only
            if (rotate)
                ExtractShapeRotation(particles, _restOffsets, c);
            var rot = _shapeRotation;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].Pinned) continue;
                var target = c + (rotate ? rot * _restOffsets[i] : _restOffsets[i]);
                particles[i].Pos += (target - particles[i].Pos) * shape;
            }
        }

        // HOLDS: each held particle takes its own share of the way to its goal, after everything else, so a vertex
        // half weighted to a pinned bone sits half as loose as one that is not.
        var pull = _pull;
        if (pull != null)
        {
            for (int i = 0; i < particles.Length; i++)
            {
                float k = pull[i];
                if (k <= 0f || particles[i].Pinned)
                    continue;
                var target = goal != null ? goal[i] : space.LocalPointToGlobal(particles[i].RestLocal);
                particles[i].Pos += (target - particles[i].Pos) * k;
            }
        }
        return true;
    }

    // The half that touches the world: the host's collision callback, the ground plane, contact friction
    // and the sleep test. Runs on the thread that owns the host, after SolveConstraints for the same step.
    public void ResolveContacts(ISoftBodySpace space, ISoftBodyCollisionHandler? collision)
    {
        var particles = _particles;
        if (particles == null || _edges == null)
            return;
        float dt = _stepDt;
        float radius = MathF.Max(ParticleRadius, 0f);
        float groundY = GroundY;
        bool hasGround = !float.IsNaN(groundY);

        // ONE collision pass after the constraints settle: the host's colliders first, then the ground
        // plane LAST (so it always wins - the body can never sink through it). Any particle that was
        // pushed has its velocity KILLED (Prev = Pos): that's what lets it SETTLE and rest instead of
        // endlessly re-penetrating and jittering. -xlinka
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
                pos = IsFinite(particles[i].Prev) ? particles[i].Prev
                    : _goal != null ? _goal[i] : space.LocalPointToGlobal(particles[i].RestLocal);
                if (hasGround && pos.y < groundY + radius)
                    pos.y = groundY + radius;
                particles[i].Pos = pos;
                particles[i].Prev = pos;
                continue;
            }
            bool hitSurface = false;
            var posBefore = pos;

            if (collision != null && collision.ResolveParticle(i, ref pos, radius))
                hitSurface = true;

            if (hasGround && pos.y - radius < groundY)
            {
                pos.y = groundY + radius;
                hitSurface = true;
            }

            particles[i].Pos = pos;
            if (hitSurface)
            {
                // Contact response: kill the velocity INTO the surface, keep the tangential part scaled by
                // (1 - friction). Friction 1 = full stick (Prev = Pos, settles instantly); lower lets cloth
                // slide down slopes instead of gluing to the first thing it touches. The push-out direction
                // doubles as the surface normal. -xlinka
                float friction = System.Math.Clamp(Friction, 0f, 1f);
                if (friction >= 1f)
                {
                    particles[i].Prev = pos;
                }
                else
                {
                    var vel = posBefore - particles[i].Prev;
                    var push = pos - posBefore;
                    float pushLen = push.Length;
                    if (pushLen > 1e-6f)
                    {
                        var n = push / pushLen;
                        var tangential = vel - n * float3.Dot(vel, n);
                        particles[i].Prev = pos - tangential * (1f - friction);
                    }
                    else
                    {
                        particles[i].Prev = pos;
                    }
                }
            }
        }

        // SLEEP: a particle that has stayed slow for SleepSteps steps in a row is at rest - zero its
        // residual velocity so it STOPS instead of buzzing forever. This is why real cloth settles;
        // without it the constraint + gravity equilibrium oscillates indefinitely (the endless jiggle).
        // When EVERY free particle is asleep the whole body goes dormant and stops costing anything.
        //
        // The speed is per second, so the same cloth settles alike at 60 and 144 Hz, and small: zeroing
        // anything under 1.2 edges a second on the spot was a cut a breeze or the pull back after a push
        // never got past, so a shoved curtain crept home over twenty seconds instead of swinging. Wind and
        // anything within reach keep every particle awake. -xlinka
        var slow = _slowSteps;
        bool canSleep = slow != null && !CollidersNear && Wind.LengthSquared <= 1e-8f;
        float sleep = MathF.Min(SleepSpeed, _avgEdge * 0.25f) * dt;
        float sleepSq = sleep * sleep;
        int awake = 0;
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].Pinned)
                continue;
            if (canSleep && float3.DistanceSquared(particles[i].Pos, particles[i].Prev) < sleepSq)
            {
                if (slow![i] < SleepSteps)
                    slow[i]++;
                if (slow[i] >= SleepSteps)
                {
                    particles[i].Prev = particles[i].Pos;
                    continue;
                }
            }
            else if (slow != null)
            {
                slow[i] = 0;
            }
            awake++;
        }
        IsAwake = awake > 0;
    }

    // OUTPUT

    // Convert every particle back into anchor-local space. Returns true if anything moved by more than
    // a hundredth of an average edge since the last call, so a settled body can skip its mesh upload -
    // the single most expensive thing a host does with this data. -xlinka
    public bool WriteLocalPositions(ISoftBodySpace space, float3[] target, ref float3[]? lastUploaded)
    {
        var particles = _particles;
        if (particles == null || target.Length < particles.Length)
            return false;

        int n = particles.Length;
        float eps = _avgEdge * 0.01f;
        float epsSq = eps * eps;
        bool changed = lastUploaded == null || lastUploaded.Length != n;
        for (int i = 0; i < n; i++)
        {
            var local = space.GlobalPointToLocal(particles[i].Pos);
            target[i] = local;
            if (!changed && float3.DistanceSquared(local, lastUploaded![i]) > epsSq)
                changed = true;
        }
        if (!changed)
            return false;

        if (lastUploaded == null || lastUploaded.Length != n)
            lastUploaded = new float3[n];
        Array.Copy(target, lastUploaded, n);
        return true;
    }

    // The body's world-space bounds grown by padding.
    public void ComputeBounds(float padding, out float3 center, out float3 size)
    {
        var particles = _particles;
        if (particles == null || particles.Length == 0)
        {
            center = float3.Zero;
            size = float3.Zero;
            return;
        }
        var min = particles[0].Pos;
        var max = min;
        for (int i = 1; i < particles.Length; i++)
        {
            var p = particles[i].Pos;
            if (!IsFinite(p)) continue;
            min.x = MathF.Min(min.x, p.x); min.y = MathF.Min(min.y, p.y); min.z = MathF.Min(min.z, p.z);
            max.x = MathF.Max(max.x, p.x); max.y = MathF.Max(max.y, p.y); max.z = MathF.Max(max.z, p.z);
        }
        var pad = new float3(padding, padding, padding);
        min -= pad;
        max += pad;
        center = (min + max) * 0.5f;
        size = max - min;
    }

    // INTERNALS

    private float3 Centroid()
    {
        var particles = _particles!;
        var sum = float3.Zero;
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

    private static bool IsFinite(float3 v)
        => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    // Per-iteration stiffness that COMPOUNDS to the requested value over N iterations: k' = 1-(1-k)^(1/N).
    private static float CorrectedStiffness(float k, int iterations)
    {
        k = System.Math.Clamp(k, 0f, 1f);
        if (k <= 0f || k >= 1f || iterations <= 1)
            return k;
        return 1f - MathF.Pow(1f - k, 1f / iterations);
    }

    // One relaxation pass over an edge list. Weighted position-based dynamics: correction split by
    // the per-end shares baked at build, pinned = 0 -> immovable. Shared by the stretch and bend lists.
    // The shares are exact halves or whole, so the arithmetic lands on the same bits as splitting by
    // the inverse-mass sum did. -xlinka
    private static void SolveEdges(Particle[] particles, Edge[] edges, float stiffness)
    {
        for (int e = 0; e < edges.Length; e++)
        {
            ref var edge = ref edges[e];
            ref var a = ref particles[edge.A];
            ref var b = ref particles[edge.B];
            var delta = b.Pos - a.Pos;
            float dist = delta.Length;
            if (dist < 1e-6f)
                continue;
            var corr = delta * ((dist - edge.Rest) / dist * stiffness);
            a.Pos += corr * edge.Ka;
            b.Pos -= corr * edge.Kb;
        }
    }

    // Best-fit rotation of the rest shape onto the current pose (shape matching), extracted from the
    // covariance between current offsets and rest offsets. Quaternion iteration instead of a matrix polar
    // decomposition (we have no 3x3 type): rotate the basis by the residual torque between the rotated
    // rest axes and the covariance columns until they align. Warm-started from last frame's rotation, so
    // it converges in 1-2 iterations during smooth motion. Guaranteed-orthonormal output by construction
    // (it's always a quaternion), which is the property that makes shape matching unconditionally stable. -xlinka
    private void ExtractShapeRotation(Particle[] particles, float3[] restOffsets, float3 centroid)
    {
        // Covariance columns: A_col_j = sum over particles of (p - c) * rest_j.
        float3 a0 = float3.Zero, a1 = float3.Zero, a2 = float3.Zero;
        for (int i = 0; i < particles.Length; i++)
        {
            var p = particles[i].Pos - centroid;
            var q = restOffsets[i];
            a0 += p * q.x;
            a1 += p * q.y;
            a2 += p * q.z;
        }
        if (a0.LengthSquared + a1.LengthSquared + a2.LengthSquared < 1e-12f)
            return; // degenerate (all at centroid) - keep last rotation

        var rot = _shapeRotation;
        for (int iter = 0; iter < 12; iter++)
        {
            var r0 = rot * float3.Right;
            var r1 = rot * float3.Up;
            var r2 = rot * float3.Forward;
            var omega = (float3.Cross(r0, a0) + float3.Cross(r1, a1) + float3.Cross(r2, a2))
                * (1f / (MathF.Abs(float3.Dot(r0, a0) + float3.Dot(r1, a1) + float3.Dot(r2, a2)) + 1e-9f));
            float angle = omega.Length;
            if (angle < 1e-5f)
                break;
            rot = floatQ.AxisAngleRad(omega / angle, angle) * rot;
        }
        // Renormalize: repeated quaternion products drift off unit length and a non-unit rotation SCALES
        // everything it touches.
        float len = MathF.Sqrt(rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w);
        if (len > 1e-6f)
            _shapeRotation = new floatQ(rot.x / len, rot.y / len, rot.z / len, rot.w / len);
    }
}
