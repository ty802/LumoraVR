// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Interaction;

public enum ToolShape
{
    Box,
    Sphere,
    Cylinder,
    Cone,
    Capsule,
    Hull
}

// Places a primitive where you point, standing on the surface you pointed at.
//
// The shape's +Y goes up the surface normal and the body is pushed out by half its own height, so a
// cylinder placed on a wall sticks out of the wall rather than being buried halfway into it. Flat
// ground and a sloped roof therefore behave the same way, which is the whole reason the normal is
// worth a second raycast. -xlinka
//
// Hull is drawn rather than placed: hold primary and sweep, and a convex hull grows around everything the
// tip passed through. In a headset the tip is the tool's own; on a desktop it rides the pointer at the depth
// of whatever was under it when the stroke began. Only the points still on the hull are kept, so a long
// stroke stays a few hundred points at most, and they reach the shared list a few times a second instead
// of every frame. -xlinka
[ComponentCategory("Interaction/Tools")]
public sealed class ShapeTool : EquippableTool
{
    private static readonly float[] SizePresets = { 0.1f, 0.25f, 0.5f, 1f };
    private static readonly float[] HullPointPresets = { 0.01f, 0.025f, 0.05f, 0.1f };
    private static readonly float[] HullMergePresets = { 0.005f, 0.01f, 0.02f, 0.04f };

    // a hull the physics backend still takes as one shape
    private const int MaxHullPoints = 256;
    private const double HullRefreshSeconds = 0.06;

    public readonly Sync<ToolShape> Shape = new();
    public readonly Sync<float> ShapeSize = new();
    public readonly Sync<colorHDR> ShapeTint = new();
    // how far the six points of each sample sit from the tip
    public readonly Sync<float> HullPointSize = new(0.025f);
    // points closer than this are taken as one
    public readonly Sync<float> HullMergeDistance = new(0.01f);
    public readonly Sync<bool> HullFlatShading = new(true);

    private Slot? _hullSlot;
    private ConvexHullMesh? _hullMesh;
    private readonly List<float3> _hullPoints = new();
    private readonly List<float3> _pending = new();
    private readonly List<float3> _solveInput = new();
    private readonly List<float3> _solved = new();
    private readonly List<int> _solvedTriangles = new();
    private float3 _lastSample;
    private bool _hasSample;
    private double _lastRefresh;
    private float _pointerDepth;

    protected override colorHDR VisualTint => new(0.35f, 0.72f, 1f, 1f);

    public bool IsDrawingHull => _hullSlot != null;

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Shape Tool";
        Shape.Value = ToolShape.Box;
        ShapeSize.Value = 0.25f;
        ShapeTint.Value = new colorHDR(0.72f, 0.76f, 0.82f, 1f);
    }

    protected override void BuildVisualExtras(Slot visual)
    {
        EnsureBead(visual, "Bead", float3.Up * 0.05f, 0.011f, VisualTint);
    }

    public override bool OnPrimaryPress()
    {
        if (EditingBlocked || SpawnBlocked)
            return false;
        if (Shape.Value == ToolShape.Hull)
            return BeginHull();
        if (!TryGetAim(out var aim))
            return false;

        var parent = World?.RootSlot;
        if (parent == null)
            return false;

        var shape = Shape.Value;
        float size = MathF.Max(0.01f, ShapeSize.Value);
        var slot = TryAddSlot(parent, shape.ToString());
        if (slot == null)
            return false;

        bool built = Guarded(() =>
        {
            slot.GlobalRotation = AlignUpTo(aim.Normal);
            // Half the body's height along the normal: the shape stands ON the surface instead of
            // straddling it. Every primitive here is centred on its own origin, so it is the same
            // number for all of them.
            slot.GlobalPosition = aim.Point + aim.Normal * (size * 0.5f);

            var renderer = AttachSurface(slot);
            renderer.Mesh.Target = BuildMesh(slot, shape, size);
            BuildCollider(slot, shape, size);

            slot.AttachComponent<Grabbable>();
        });

        if (!built)
        {
            Guarded(() => slot.Destroy());
            return false;
        }

        RecordUndo(SlotExistenceUndoBatch.Created(World, new[] { slot }, UndoLocale.SpawnShape));
        return true;
    }

    public override bool OnPrimaryHold()
    {
        if (_hullSlot == null)
            return false;
        if (_hullSlot.IsDestroyed || _hullMesh == null || _hullMesh.IsDestroyed)
        {
            ForgetHull();
            return false;
        }

        if (SampleHullPoint(starting: false, out var point, out var frame))
        {
            float spacing = MathF.Max(HullMergeDistance.Value, HullPointSize.Value * 0.5f);
            if (!_hasSample || float3.Distance(point, _lastSample) >= spacing)
                AddSample(point, frame);
        }

        double now = World?.TotalTime ?? 0;
        if (now - _lastRefresh >= HullRefreshSeconds)
            RefreshHull();
        return true;
    }

    public override bool OnPrimaryRelease()
    {
        if (_hullSlot == null)
            return false;
        FinishHull();
        return true;
    }

    public override void OnDequipped()
    {
        base.OnDequipped();
        if (_hullSlot != null)
            FinishHull();
    }

    public override void OnDestroy()
    {
        if (_hullSlot != null)
            FinishHull();
        base.OnDestroy();
    }

    // Secondary steps the shape, same as the submenu entries - reaching for the radial menu to turn a
    // box into a sphere is more ceremony than the change deserves.
    public override bool OnSecondaryPress()
    {
        if (EditingBlocked || _hullSlot != null)
            return false;
        var values = Enum.GetValues<ToolShape>();
        Shape.Value = values[((int)Shape.Value + 1) % values.Length];
        return true;
    }

    public override void PopulateToolActions(ContextMenuPage page, ContextMenuContext context)
    {
        foreach (var shape in Enum.GetValues<ToolShape>())
        {
            var pick = shape;
            page.AddItem(new ContextMenuItem
            {
                Label = shape.ToString(),
                FillColor = Shape.Value == shape ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!IsDestroyed) Shape.Value = pick; },
            });
        }

        if (Shape.Value == ToolShape.Hull)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = $"Point {HullPointSize.Value * 100f:0.#} cm",
                FillColor = ItemFill,
                OnPressed = _ => { if (!IsDestroyed) HullPointSize.Value = NextPreset(HullPointPresets, HullPointSize.Value); },
            });
            page.AddItem(new ContextMenuItem
            {
                Label = $"Merge {HullMergeDistance.Value * 100f:0.#} cm",
                FillColor = ItemFill,
                OnPressed = _ => { if (!IsDestroyed) HullMergeDistance.Value = NextPreset(HullMergePresets, HullMergeDistance.Value); },
            });
            page.AddItem(new ContextMenuItem
            {
                Label = "Flat",
                FillColor = HullFlatShading.Value ? ActiveFill : ItemFill,
                IsToggle = true,
                IsToggled = HullFlatShading.Value,
                OnPressed = _ => { if (!IsDestroyed) HullFlatShading.Value = !HullFlatShading.Value; },
            });
            return;
        }

        foreach (float preset in SizePresets)
        {
            float pick = preset;
            page.AddItem(new ContextMenuItem
            {
                Label = $"Size {preset:0.##} m",
                FillColor = MathF.Abs(ShapeSize.Value - preset) < 1e-4f ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!IsDestroyed) ShapeSize.Value = pick; },
            });
        }
    }

    private static float NextPreset(float[] presets, float current)
    {
        int nearest = 0;
        for (int i = 1; i < presets.Length; i++)
        {
            if (MathF.Abs(presets[i] - current) < MathF.Abs(presets[nearest] - current))
                nearest = i;
        }
        return presets[(nearest + 1) % presets.Length];
    }

    private MeshRenderer AttachSurface(Slot slot)
    {
        var material = slot.AttachComponent<PBS_Metallic>();
        material.AlbedoColor.Value = ShapeTint.Value;
        material.Metallic.Value = 0.1f;
        material.Smoothness.Value = 0.35f;

        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Material.Target = material;
        return renderer;
    }

    // HULL DRAWING

    private bool BeginHull()
    {
        var parent = World?.RootSlot;
        if (parent == null || _hullSlot != null)
            return false;
        if (!SampleHullPoint(starting: true, out var start, out var frame))
            return false;

        var slot = TryAddSlot(parent, "Hull");
        if (slot == null)
            return false;

        ConvexHullMesh? mesh = null;
        bool built = Guarded(() =>
        {
            slot.GlobalPosition = start;
            var renderer = AttachSurface(slot);
            mesh = slot.AttachComponent<ConvexHullMesh>();
            mesh.FlatShading.Value = HullFlatShading.Value;
            mesh.MinPointDistance.Value = MergeDistance;
            renderer.Mesh.Target = mesh;
        });
        if (!built || mesh == null)
        {
            Guarded(() => slot.Destroy());
            return false;
        }

        _hullSlot = slot;
        _hullMesh = mesh;
        _hullPoints.Clear();
        _pending.Clear();
        _hasSample = false;
        AddSample(start, frame);
        RefreshHull();
        return true;
    }

    private float MergeDistance => MathF.Max(HullMergeDistance.Value, 0.001f);

    // A headset sweeps the tip itself. A desktop hand barely moves while the pointer crosses the screen, so
    // there the point rides the pointer at a fixed depth, taken from what it was on when the stroke began and
    // pulled back by a point's reach so the first sample is not half buried in it.
    private bool SampleHullPoint(bool starting, out float3 point, out floatQ frame)
    {
        frame = Slot.GlobalRotation;
        var laser = ActiveTool?.Laser;
        bool headset = Engine.Current?.InputInterface?.IsVRActive == true;
        if (headset || laser == null)
        {
            point = Tip;
            return true;
        }

        var origin = laser.RayOrigin;
        var direction = laser.RayDirection;
        if (direction.LengthSquared <= 1e-8f)
        {
            point = Tip;
            return true;
        }
        direction = direction.Normalized;

        if (starting)
        {
            float depth = 1.5f;
            if (TryGetAim(out var aim) && aim.HitSlot != null)
                depth = float3.Distance(origin, aim.Point) - MathF.Max(HullPointSize.Value, 0.005f);
            _pointerDepth = System.Math.Clamp(depth, 0.3f, 8f);
        }
        point = origin + direction * _pointerDepth;
        return true;
    }

    private void AddSample(float3 point, floatQ frame)
    {
        var slot = _hullSlot;
        if (slot == null)
            return;
        float reach = MathF.Max(HullPointSize.Value, 0.002f);
        _pending.Add(slot.GlobalPointToLocal(point + frame * (float3.Right * reach)));
        _pending.Add(slot.GlobalPointToLocal(point - frame * (float3.Right * reach)));
        _pending.Add(slot.GlobalPointToLocal(point + frame * (float3.Up * reach)));
        _pending.Add(slot.GlobalPointToLocal(point - frame * (float3.Up * reach)));
        _pending.Add(slot.GlobalPointToLocal(point + frame * (float3.Forward * reach)));
        _pending.Add(slot.GlobalPointToLocal(point - frame * (float3.Forward * reach)));
        _lastSample = point;
        _hasSample = true;
    }

    // A point inside the hull can never come back out of it, since the hull only grows, so only the current
    // hull and the new samples go into each solve.
    private void RefreshHull()
    {
        _lastRefresh = World?.TotalTime ?? 0;
        if (_pending.Count == 0 || _hullMesh == null || _hullMesh.IsDestroyed)
            return;

        _solveInput.Clear();
        _solveInput.AddRange(_hullPoints);
        _solveInput.AddRange(_pending);
        _pending.Clear();

        float merge = MergeDistance;
        var result = ConvexHullSolver.Solve(_solveInput, merge, _solved, _solvedTriangles);
        while (_solved.Count > MaxHullPoints)
        {
            merge *= 1.5f;
            result = ConvexHullSolver.Solve(_solveInput, merge, _solved, _solvedTriangles);
        }
        if (result == ConvexHullSolver.Result.Empty || result == ConvexHullSolver.Result.Collinear)
            return;

        _hullPoints.Clear();
        _hullPoints.AddRange(_solved);
        var mesh = _hullMesh;
        Guarded(() => WritePoints(mesh.Points, _hullPoints));
    }

    // The shared list moves to the new hull by changing only what changed: points that left the hull give
    // their places to the ones that joined it.
    internal static void WritePoints(SyncFieldList<float3> list, List<float3> points)
    {
        var wanted = new HashSet<float3>(points);
        var present = new HashSet<float3>();
        var free = new List<int>();
        for (int i = 0; i < list.Count; i++)
        {
            float3 value = list[i];
            if (wanted.Contains(value) && present.Add(value))
                continue;
            free.Add(i);
        }

        int next = 0;
        foreach (var point in points)
        {
            if (present.Contains(point))
                continue;
            present.Add(point);
            if (next < free.Count)
                list[free[next++]] = point;
            else
                list.Add(point);
        }

        for (int i = free.Count - 1; i >= next; i--)
            list.RemoveAt(free[i]);
    }

    // Finished hulls are centred on their own bounds, so they turn about their middle when grabbed, then get
    // the collider and grab every other shape gets.
    private void FinishHull()
    {
        var slot = _hullSlot;
        var mesh = _hullMesh;
        RefreshHull();
        ForgetHull();
        if (slot == null || slot.IsDestroyed || mesh == null || mesh.IsDestroyed)
            return;

        var points = new List<float3>(mesh.Points.Count);
        foreach (var point in mesh.Points)
            points.Add(point);
        if (points.Count < 4 || ConvexHullSolver.Solve(points, 0f, _solved, _solvedTriangles) != ConvexHullSolver.Result.Volume)
        {
            Guarded(() => slot.Destroy());
            return;
        }

        float3 min = points[0], max = points[0];
        foreach (var p in points)
        {
            min = new float3(MathF.Min(min.x, p.x), MathF.Min(min.y, p.y), MathF.Min(min.z, p.z));
            max = new float3(MathF.Max(max.x, p.x), MathF.Max(max.y, p.y), MathF.Max(max.z, p.z));
        }
        float3 centre = (min + max) * 0.5f;

        bool built = Guarded(() =>
        {
            float3 globalCentre = slot.LocalPointToGlobal(centre);
            for (int i = 0; i < mesh.Points.Count; i++)
                mesh.Points[i] = mesh.Points[i] - centre;
            slot.GlobalPosition = globalCentre;
            mesh.RegenerateMesh();
            ConvexHullCollider.Ensure(slot, mesh);
            slot.AttachComponent<Grabbable>();
        });
        if (!built)
        {
            Guarded(() => slot.Destroy());
            return;
        }

        RecordUndo(SlotExistenceUndoBatch.Created(World, new[] { slot }, UndoLocale.SpawnShape));
    }

    private void ForgetHull()
    {
        _hullSlot = null;
        _hullMesh = null;
        _hullPoints.Clear();
        _pending.Clear();
        _hasSample = false;
    }

    private static Component BuildMesh(Slot slot, ToolShape shape, float size)
    {
        switch (shape)
        {
            case ToolShape.Sphere:
            {
                var mesh = slot.AttachComponent<SphereMesh>();
                mesh.Radius.Value = size * 0.5f;
                mesh.Segments.Value = 24;
                mesh.Rings.Value = 14;
                return mesh;
            }
            case ToolShape.Cylinder:
            {
                var mesh = slot.AttachComponent<CylinderMesh>();
                mesh.Radius.Value = size * 0.5f;
                mesh.Height.Value = size;
                mesh.Segments.Value = 24;
                return mesh;
            }
            case ToolShape.Cone:
            {
                var mesh = slot.AttachComponent<ConeMesh>();
                mesh.RadiusBase.Value = size * 0.5f;
                mesh.RadiusTop.Value = 0f;
                mesh.Height.Value = size;
                mesh.Segments.Value = 24;
                return mesh;
            }
            case ToolShape.Capsule:
            {
                var mesh = slot.AttachComponent<CapsuleMesh>();
                mesh.Radius.Value = size * 0.25f;
                mesh.Height.Value = size;
                mesh.Segments.Value = 20;
                mesh.Rings.Value = 8;
                return mesh;
            }
            default:
            {
                var mesh = slot.AttachComponent<BoxMesh>();
                mesh.Size.Value = float3.One * size;
                mesh.ScaleUVWithSize.Value = true;
                return mesh;
            }
        }
    }

    private static void BuildCollider(Slot slot, ToolShape shape, float size)
    {
        switch (shape)
        {
            case ToolShape.Sphere:
                slot.AttachComponent<SphereCollider>().Radius.Value = size * 0.5f;
                break;
            case ToolShape.Cylinder:
            {
                var collider = slot.AttachComponent<CylinderCollider>();
                collider.Radius.Value = size * 0.5f;
                collider.Height.Value = size;
                break;
            }
            case ToolShape.Cone:
            {
                var collider = slot.AttachComponent<ConeCollider>();
                collider.Radius.Value = size * 0.5f;
                collider.Height.Value = size;
                break;
            }
            case ToolShape.Capsule:
            {
                var collider = slot.AttachComponent<CapsuleCollider>();
                collider.Radius.Value = size * 0.25f;
                collider.Height.Value = size;
                break;
            }
            default:
                slot.AttachComponent<BoxCollider>().Size.Value = float3.One * size;
                break;
        }
    }
}
