// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Godot.Extensions;
using Lumora.Godot.Hooks.Particles;
using Lumora.Simulation.Particles;

namespace Lumora.Godot.Hooks;

// Draws a TrailRenderer's recorded path, through exactly the same ribbon builder the particle system's
// trails and ribbons go through. One strand instead of hundreds is the only difference that reaches
// any of the geometry code.
//
// The mesh does not hang in the slot's own frame. Points are recorded in a chosen space - world, or
// some other slot - so this node carries the transform that takes the slot's frame back to that space,
// and then a swung sword's streak stays in the air instead of swinging with the blade. -xlinka
[ImplementableHook(typeof(TrailRenderer))]
public sealed partial class TrailRendererHook : ComponentHook<TrailRenderer>, ILodRangeTarget
{
    private TrailProcessNode _processNode = null!;
    private StrandSurface _surface = null!;
    private readonly StrandRange[] _strand = new StrandRange[1];
    private LodVisibilityRange _lodRange = LodVisibilityRange.Unbounded;

    public static IHook<TrailRenderer> Constructor() => new TrailRendererHook();

    public override void Initialize()
    {
        base.Initialize();

        _processNode = new TrailProcessNode(this) { Name = "TrailRenderer" };
        attachedNode.AddChild(_processNode);
        _surface = new StrandSurface(_processNode, "TrailRibbon");

        ApplyChanges();
    }

    public override void ApplyChanges()
    {
        if (_surface == null)
            return;
        _surface.Visible = Owner.Enabled.Value && Owner.Slot.IsActive;
        // The recording space or the point budget may have just moved under it, and either invalidates
        // whatever ribbon is currently on screen.
        _surface.Invalidate();
        ApplyLodRange();
    }

    public void SetLodVisibilityRange(in LodVisibilityRange range)
    {
        if (_lodRange.Equals(range))
            return;
        _lodRange = range;
        ApplyLodRange();
    }

    private void ApplyLodRange()
    {
        if (Owner == null || _surface == null)
            return;

        float distance = System.Math.Max(0f, Owner.MaxViewDistance.Value);
        var own = LodVisibilityRange.To(distance, distance > 0f ? FadeMargin : 0f);
        var range = LodVisibilityRange.Tightest(in _lodRange, in own);
        _surface.ApplyLodRange(in range);
    }

    private const float FadeMargin = 3f;

    internal void PullRender()
    {
        if (Owner == null || Owner.IsDestroyed || _surface == null)
            return;

        bool visible = Owner.Enabled.Value && Owner.Slot.IsActive;
        _surface.Visible = visible;
        _surface.ApplyMaterial(Owner.EmissionStrength.Value, Owner.RenderQueue.Value, Owner.Texture.Target);
        if (!visible)
            return;

        ApplySpaceTransform();

        int points = Owner.PointCount;
        if (points < 2)
        {
            _surface.ClearGeometry();
            return;
        }

        _strand[0] = new StrandRange(0, points, 0f);

        var parameters = new StrandGeometryParams(
            Owner.UVMode.Value,
            Owner.Alignment.Value,
            Owner.AlignmentUp.Value,
            Owner.TileLength.Value,
            Owner.Smoothing.Value);

        _surface.Rebuild(
            Owner.Positions.AsSpan(0, points),
            Owner.Colors.AsSpan(0, points),
            Owner.Widths.AsSpan(0, points),
            _strand,
            1,
            in parameters,
            ViewPosition(),
            Owner.Version);
    }

    // Slot frame -> recording space. Empty reference slot means world, whose matrix is identity, so the
    // one expression covers both.
    private void ApplySpaceTransform()
    {
        if (_processNode == null || !GodotObject.IsInstanceValid(_processNode))
            return;

        var space = Owner.RecordingSpace;
        var spaceToWorld = space != null && !space.IsDestroyed ? space.LocalToWorld : float4x4.Identity;
        _processNode.Transform = (Owner.Slot.WorldToLocal * spaceToWorld).ToGodot();
    }

    private float3 ViewPosition()
    {
        if (_processNode == null || !GodotObject.IsInstanceValid(_processNode) || !_processNode.IsInsideTree())
            return float3.Zero;

        var camera = _processNode.GetViewport()?.GetCamera3D();
        if (camera == null || !GodotObject.IsInstanceValid(camera))
            return float3.Zero;

        var local = _processNode.GlobalTransform.AffineInverse() * camera.GlobalPosition;
        return new float3(local.X, local.Y, local.Z);
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld)
        {
            _surface?.Dispose();
            if (_processNode != null && GodotObject.IsInstanceValid(_processNode))
                _processNode.QueueFree();
        }
        _surface = null!;
        _processNode = null!;
        base.Destroy(destroyingWorld);
    }

    // Node3D for the same reason the particle system's is: visibility only propagates through Node3D,
    // so a plain Node here would leave a backgrounded world's trails on screen.
    private sealed partial class TrailProcessNode : Node3D
    {
        private readonly TrailRendererHook _hook;
        public TrailProcessNode(TrailRendererHook hook) => _hook = hook;
        public TrailProcessNode() => _hook = null!;

        public override void _Process(double delta)
        {
            _hook?.PullRender();
        }
    }
}
