// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;

namespace Lumora.Godot.Hooks;

// MOVING OBJECTS THAT LOOK ALIKE, DRAWN AS ONE.
//
// Every instance moved on the render server is a culling update, a light re-pairing and its own entry in each
// pass the frame draws, and a pile of ten thousand identical cubes is one mesh and one material ten thousand
// times over. So moving objects that would draw exactly alike (same mesh, same material, same layers, same
// shadow casting, same ordering and distances) are drawn together: one multimesh per kind per stretch of space,
// its instance transforms filled from the poses the body pass already has. Each object's own instance is hidden while it is in a batch and
// shown again, at the pose last drawn, when it leaves.
//
// The space is cut into cells, and a kind gets one batch per cell its members are in, bounded by that cell
// grown by the largest member's reach: one huge batch would be drawn in every shadow cascade and never culled.
// A member that crosses into another cell changes batch.
//
// What joins and what leaves is the body's business (RigidBodyHook); this keeps the batches. World thread, one
// per world, freed with it. -xlinka
internal sealed class MoverBatches : IDisposable
{
    private const float CellSize = 8f;
    private const int Stride = 12;
    private const int MaxSurfaces = 4;
    private const int FreeAfterEmptyFrames = 120;

    // What makes two objects draw alike.
    internal sealed class Kind : IEquatable<Kind>
    {
        public readonly Rid Mesh;
        public readonly Rid[] Materials;
        public readonly uint Layers;
        public readonly GeometryInstance3D.ShadowCastingSetting Cast;
        public readonly float SortingOffset;
        public readonly float ExtraCull;
        public readonly float RangeBegin, RangeEnd, RangeBeginMargin, RangeEndMargin;
        public readonly GeometryInstance3D.VisibilityRangeFadeModeEnum Fade;
        public readonly float Radius;
        private readonly int _hash;

        private Kind(MeshInstance3D instance, Mesh mesh, Rid[] materials)
        {
            Mesh = mesh.GetRid();
            Materials = materials;
            Layers = instance.Layers;
            Cast = instance.CastShadow;
            SortingOffset = instance.SortingOffset;
            ExtraCull = instance.ExtraCullMargin;
            RangeBegin = instance.VisibilityRangeBegin;
            RangeEnd = instance.VisibilityRangeEnd;
            RangeBeginMargin = instance.VisibilityRangeBeginMargin;
            RangeEndMargin = instance.VisibilityRangeEndMargin;
            Fade = instance.VisibilityRangeFadeMode;
            var bounds = mesh.GetAabb();
            Radius = bounds.Position.Length() + bounds.Size.Length();
            var hash = new HashCode();
            hash.Add(Mesh);
            foreach (var material in materials)
                hash.Add(material);
            hash.Add(Layers);
            hash.Add(Cast);
            hash.Add(SortingOffset);
            hash.Add(ExtraCull);
            hash.Add(RangeBegin);
            hash.Add(RangeEnd);
            _hash = hash.ToHashCode();
        }

        // What the instance draws like now, or nothing when it is not something a batch can draw for it.
        public static Kind? Of(MeshInstance3D instance)
        {
            var mesh = instance.Mesh;
            if (mesh == null || instance.MaterialOverride != null || instance.MaterialOverlay != null || instance.Transparency > 0f)
                return null;
            int surfaces = mesh.GetSurfaceCount();
            if (surfaces == 0 || surfaces > MaxSurfaces)
                return null;
            // A batch wears one material over every surface: the render server takes per-surface materials on a
            // single mesh's instance only. So every surface has to wear the same one.
            var materials = new Rid[surfaces];
            for (int i = 0; i < surfaces; i++)
            {
                materials[i] = instance.GetSurfaceOverrideMaterial(i)?.GetRid() ?? default;
                if (!materials[i].IsValid || materials[i] != materials[0])
                    return null;
            }
            return new Kind(instance, mesh, materials);
        }

        public bool Equals(Kind? other)
        {
            if (other is null || other._hash != _hash || other.Mesh != Mesh || other.Materials.Length != Materials.Length
                || other.Layers != Layers || other.Cast != Cast || other.SortingOffset != SortingOffset
                || other.ExtraCull != ExtraCull || other.RangeBegin != RangeBegin || other.RangeEnd != RangeEnd
                || other.RangeBeginMargin != RangeBeginMargin || other.RangeEndMargin != RangeEndMargin || other.Fade != Fade)
                return false;
            for (int i = 0; i < Materials.Length; i++)
            {
                if (other.Materials[i] != Materials[i])
                    return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is Kind other && Equals(other);

        public override int GetHashCode() => _hash;
    }

    // A member's place: its batch, where it sits in it, and its own instance.
    internal sealed class Seat
    {
        public Rid Own;
        public Kind? Kind;
        public Chunk? Chunk;
        public int Index = -1;
        public float Reach;
    }

    internal sealed class Chunk
    {
        public Rid Multimesh;
        public Rid Instance;
        public Kind Kind = null!;
        public Vector3I Cell;
        public float[] Buffer = Array.Empty<float>();
        public Seat[] Seats = Array.Empty<Seat>();
        public int Count;
        public int Allocated;
        public float Reach;
        public float BoundsReach = -1f;
        public bool Queued;
        public int EmptyFrames;
    }

    private readonly WorldHook _world;
    private readonly Dictionary<Kind, Kind> _kinds = new();
    private readonly Dictionary<(Kind, Vector3I), Chunk> _chunks = new();
    private readonly List<Chunk> _queued = new();
    private readonly List<(Kind, Vector3I)> _gone = new();
    private Rid _scenario;
    private bool _visible = true;
    private int _frame;

    public MoverBatches(WorldHook world) => _world = world;

    public int ChunkCount => _chunks.Count;

    // One kind object per look, so members compare kinds by reference.
    public Kind Intern(Kind kind)
    {
        if (_kinds.TryGetValue(kind, out var known))
            return known;
        _kinds.Add(kind, kind);
        return kind;
    }

    // Draws seat's member at pose, moving it to the batch of the cell it is in now. False when there is no
    // batch to draw it in yet (the world is not in a scene).
    public bool Place(Seat seat, in Transform3D pose)
    {
        var origin = pose.Origin;
        var cell = new Vector3I((int)MathF.Floor(origin.X / CellSize), (int)MathF.Floor(origin.Y / CellSize), (int)MathF.Floor(origin.Z / CellSize));
        var chunk = seat.Chunk;
        if (chunk == null || chunk.Cell != cell)
        {
            Remove(seat);
            chunk = ChunkFor(seat.Kind!, cell);
            if (chunk == null)
                return false;
            Add(chunk, seat);
        }
        int at = seat.Index * Stride;
        var buffer = chunk.Buffer;
        var b = pose.Basis;
        buffer[at] = b.Row0.X;
        buffer[at + 1] = b.Row0.Y;
        buffer[at + 2] = b.Row0.Z;
        buffer[at + 3] = origin.X;
        buffer[at + 4] = b.Row1.X;
        buffer[at + 5] = b.Row1.Y;
        buffer[at + 6] = b.Row1.Z;
        buffer[at + 7] = origin.Y;
        buffer[at + 8] = b.Row2.X;
        buffer[at + 9] = b.Row2.Y;
        buffer[at + 10] = b.Row2.Z;
        buffer[at + 11] = origin.Z;
        Queue(chunk);
        return true;
    }

    // Out of its batch; the last member takes its place.
    public void Remove(Seat seat)
    {
        var chunk = seat.Chunk;
        if (chunk == null)
            return;
        int index = seat.Index;
        int last = chunk.Count - 1;
        if (index != last)
        {
            var moved = chunk.Seats[last];
            chunk.Seats[index] = moved;
            moved.Index = index;
            Array.Copy(chunk.Buffer, last * Stride, chunk.Buffer, index * Stride, Stride);
        }
        chunk.Seats[last] = null!;
        chunk.Count = last;
        seat.Chunk = null;
        seat.Index = -1;
        Queue(chunk);
    }

    private void Add(Chunk chunk, Seat seat)
    {
        if (chunk.Count == chunk.Seats.Length)
        {
            int size = Math.Max(16, chunk.Seats.Length * 2);
            Array.Resize(ref chunk.Seats, size);
            Array.Resize(ref chunk.Buffer, size * Stride);
        }
        seat.Chunk = chunk;
        seat.Index = chunk.Count;
        chunk.Seats[chunk.Count++] = seat;
        if (seat.Reach > chunk.Reach)
            chunk.Reach = seat.Reach;
        chunk.EmptyFrames = 0;
    }

    private Chunk? ChunkFor(Kind kind, Vector3I cell)
    {
        if (_chunks.TryGetValue((kind, cell), out var chunk))
            return chunk;
        if (!_scenario.IsValid)
        {
            var root = _world.WorldRoot;
            if (root == null || !GodotObject.IsInstanceValid(root) || !root.IsInsideTree())
                return null;
            _scenario = root.GetWorld3D().Scenario;
        }
        chunk = new Chunk { Kind = kind, Cell = cell };
        chunk.Multimesh = RenderingServer.MultimeshCreate();
        RenderingServer.MultimeshSetMesh(chunk.Multimesh, kind.Mesh);
        chunk.Instance = RenderingServer.InstanceCreate2(chunk.Multimesh, _scenario);
        RenderingServer.InstanceSetLayerMask(chunk.Instance, kind.Layers);
        RenderingServer.InstanceGeometrySetCastShadowsSetting(chunk.Instance, (RenderingServer.ShadowCastingSetting)kind.Cast);
        RenderingServer.InstanceGeometrySetMaterialOverride(chunk.Instance, kind.Materials[0]);
        RenderingServer.InstanceSetPivotData(chunk.Instance, kind.SortingOffset, true);
        if (kind.ExtraCull > 0f)
            RenderingServer.InstanceSetExtraVisibilityMargin(chunk.Instance, kind.ExtraCull);
        if (kind.RangeBegin > 0f || kind.RangeEnd > 0f)
            RenderingServer.InstanceGeometrySetVisibilityRange(chunk.Instance, kind.RangeBegin, kind.RangeEnd, kind.RangeBeginMargin,
                kind.RangeEndMargin, (RenderingServer.VisibilityRangeFadeMode)kind.Fade);
        RenderingServer.InstanceSetVisible(chunk.Instance, _visible);
        _chunks.Add((kind, cell), chunk);
        return chunk;
    }

    private void Queue(Chunk chunk)
    {
        if (chunk.Queued)
            return;
        chunk.Queued = true;
        _queued.Add(chunk);
    }

    // At the start of the body pass. A world out of sight hides its batches, and coming back into sight shows
    // every object's own instance again, which has to be hidden again while it is in a batch.
    public void BeginFrame()
    {
        _frame++;
        var root = _world.WorldRoot;
        bool visible = root != null && GodotObject.IsInstanceValid(root) && root.IsVisibleInTree();
        if (visible == _visible)
            return;
        _visible = visible;
        foreach (var chunk in _chunks.Values)
            RenderingServer.InstanceSetVisible(chunk.Instance, visible);
        if (!visible)
            return;
        foreach (var chunk in _chunks.Values)
        {
            for (int i = 0; i < chunk.Count; i++)
                RenderingServer.InstanceSetVisible(chunk.Seats[i].Own, false);
        }
    }

    // At the end of the body pass: every batch a member moved in, joined or left gets its transforms in one call.
    public void Flush()
    {
        for (int i = 0; i < _queued.Count; i++)
        {
            var chunk = _queued[i];
            chunk.Queued = false;
            int capacity = chunk.Seats.Length;
            if (chunk.Allocated != capacity)
            {
                RenderingServer.MultimeshAllocateData(chunk.Multimesh, capacity, RenderingServer.MultimeshTransformFormat.Transform3D);
                chunk.Allocated = capacity;
            }
            if (chunk.BoundsReach < chunk.Reach)
            {
                chunk.BoundsReach = chunk.Reach;
                var low = new Vector3(chunk.Cell.X, chunk.Cell.Y, chunk.Cell.Z) * CellSize - Vector3.One * chunk.Reach;
                RenderingServer.MultimeshSetCustomAabb(chunk.Multimesh, new Aabb(low, Vector3.One * (CellSize + chunk.Reach * 2f)));
            }
            if (capacity > 0)
                RenderingServer.MultimeshSetBuffer(chunk.Multimesh, chunk.Buffer);
            RenderingServer.MultimeshSetVisibleInstances(chunk.Multimesh, chunk.Count);
        }
        _queued.Clear();

        // Batches left empty a while are let go.
        if (_frame % 60 != 0)
            return;
        _gone.Clear();
        foreach (var (key, chunk) in _chunks)
        {
            if (chunk.Count > 0)
                continue;
            chunk.EmptyFrames += 60;
            if (chunk.EmptyFrames >= FreeAfterEmptyFrames)
            {
                Free(chunk);
                _gone.Add(key);
            }
        }
        foreach (var key in _gone)
            _chunks.Remove(key);
        if (_chunks.Count == 0)
            _kinds.Clear();
    }

    private static void Free(Chunk chunk)
    {
        if (chunk.Instance.IsValid)
            RenderingServer.FreeRid(chunk.Instance);
        if (chunk.Multimesh.IsValid)
            RenderingServer.FreeRid(chunk.Multimesh);
        chunk.Instance = default;
        chunk.Multimesh = default;
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks.Values)
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                chunk.Seats[i].Chunk = null;
                chunk.Seats[i].Index = -1;
            }
            Free(chunk);
        }
        _chunks.Clear();
        _queued.Clear();
        _kinds.Clear();
    }
}
