// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using GodotProbe = Godot.ReflectionProbe;
using LumoraProbe = Lumora.Core.Components.ReflectionProbe;

namespace Lumora.Godot.Hooks;

// Drives a Godot ReflectionProbe from the component's fields. No capture logic of its own - the
// renderer owns that entirely; this only says what and when.
//
// The rebake trick is the one thing worth explaining. A Once probe captures when it comes up and
// then holds that capture forever, and there is no "capture now" call to make. Flipping it to Always
// for a single frame and back is what makes it re-capture, which is why the component publishes a
// counter rather than firing an event: the hook compares the counter it last saw against the current
// one, so a rebake requested while the world was unfocused still happens when it comes back rather
// than being missed. -xlinka
[ImplementableHook(typeof(LumoraProbe))]
public sealed class ReflectionProbeHook : NodeBackedComponentHook<LumoraProbe, GodotProbe>
{
    private int _lastBakeGeneration;
    private bool _rebaking;
    private bool _frameHooked;
    private MeshInstance3D? _debugBounds;
    private ImmediateMesh? _debugMesh;
    private StandardMaterial3D? _debugMaterial;

    public static IHook<LumoraProbe> Constructor() => new ReflectionProbeHook();

    public GodotProbe GodotReflectionProbe => PlatformNode;

    protected override GodotProbe CreatePlatformNode() => new GodotProbe { Name = "ReflectionProbe" };

    protected override void OnAfterAttach()
    {
        _lastBakeGeneration = Owner.BakeGeneration.Value;
        EngineSettings.Changed += OnSettingsChanged;
    }

    protected override void SyncProperties()
    {
        var probe = PlatformNode;
        if (probe == null)
            return;
        SyncDebugBounds();

        // The quality toggle is a hard off: an invisible probe is not captured and contributes
        // nothing, so turning reflections off actually removes the cost rather than dimming it.
        bool enabled = Owner.Enabled && EngineSettings.ReflectionsEnabled;
        probe.Visible = enabled;

        var size = Owner.Size.Value;
        probe.Size = new Vector3(size.x, size.y, size.z);

        var offset = Owner.OriginOffset.Value;
        probe.OriginOffset = new Vector3(offset.x, offset.y, offset.z);

        probe.Intensity = Owner.Intensity.Value;
        probe.BoxProjection = Owner.BoxProjection.Value;
        probe.Interior = Owner.Interior.Value;
        probe.BlendDistance = Owner.BlendDistance.Value;
        probe.MaxDistance = Owner.MaxDistance.Value;
        probe.EnableShadows = Owner.CaptureShadows.Value;

        // Capture the focused world and the real local head. Diagnostic geometry, private overlays,
        // headless eye-view twins and planar mirror surfaces must not contaminate the cubemap.
        int cullMask = Owner.CullMask.Value;
        probe.CullMask = (uint)((cullMask != 0 ? cullMask : Helpers.RenderHelper.PUBLIC_RENDER_MASK)
            & ~Helpers.RenderHelper.EYES_ONLY_LAYER & ~Helpers.RenderHelper.FREECAM_INDICATOR_LAYER
            & ~Helpers.RenderHelper.MIRROR_LAYER & ~Helpers.RenderHelper.TEMP_LAYER
            & ~Helpers.RenderHelper.OVERLAY_LAYER & ~Helpers.RenderHelper.HIDDEN_LAYER);

        probe.AmbientMode = Owner.AmbientMode.Value switch
        {
            ProbeAmbientMode.Disabled => GodotProbe.AmbientModeEnum.Disabled,
            ProbeAmbientMode.Color => GodotProbe.AmbientModeEnum.Color,
            _ => GodotProbe.AmbientModeEnum.Environment,
        };
        var ambient = Owner.AmbientColor.Value;
        probe.AmbientColor = new Color(ambient.r, ambient.g, ambient.b, ambient.a);
        probe.AmbientColorEnergy = Owner.AmbientEnergy.Value;

        // The generation is consumed only where the rebake is ACTUALLY armed.
        //
        // It used to be taken unconditionally, one line above a gate that drops the request when the
        // component is disabled or reflections are off in settings. A rebake pressed in either state
        // was eaten and never replayed, which is the opposite of what this hook's own header promises.
        // The settings-changed path re-marks the component dirty, so a request parked here replays on
        // its own once the gate opens. -xlinka
        int generation = Owner.BakeGeneration.Value;
        bool rebakeRequested = generation != _lastBakeGeneration;

        if (rebakeRequested && enabled && Owner.UpdateMode.Value == ProbeUpdateMode.Once)
        {
            _lastBakeGeneration = generation;
            BeginRebake(probe);
        }
        else if (!_rebaking)
            probe.UpdateMode = Owner.UpdateMode.Value == ProbeUpdateMode.Always
                ? GodotProbe.UpdateModeEnum.Always
                : GodotProbe.UpdateModeEnum.Once;
    }

    private void SyncDebugBounds()
    {
        if (!Owner.DebugVisual.Value)
        {
            if (_debugBounds != null) _debugBounds.Visible = false;
            return;
        }
        if (_debugBounds == null)
        {
            _debugMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.25f, 0.8f, 0.95f),
                DisableFog = true,
            };
            _debugMesh = new ImmediateMesh();
            _debugBounds = new MeshInstance3D
            {
                Name = "Probe influence bounds", Mesh = _debugMesh,
                Layers = Helpers.RenderHelper.TEMP_LAYER,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            attachedNode.AddChild(_debugBounds);
        }
        _debugBounds.Visible = true;
        var wire = new Lumora.Core.Components.Gizmos.GizmoWireBuilder();
        wire.Box(Lumora.Core.Math.float3.Zero, Owner.Size.Value);
        if (Owner.OriginOffset.Value.LengthSquared > 0.001f)
            wire.Line(Lumora.Core.Math.float3.Zero, Owner.OriginOffset.Value);
        _debugMesh!.ClearSurfaces();
        _debugMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _debugMaterial);
        foreach (var point in wire.Vertices)
            _debugMesh.SurfaceAddVertex(new Vector3(point.x, point.y, point.z));
        _debugMesh.SurfaceEnd();
    }

    private void BeginRebake(GodotProbe probe)
    {
        probe.UpdateMode = GodotProbe.UpdateModeEnum.Always;
        if (_rebaking)
            return;

        _rebaking = true;
        var tree = probe.GetTree();
        if (tree == null)
        {
            // No tree to wait a frame on. Leaving it in Always would quietly turn a Once probe into
            // the expensive kind, so give the capture back immediately rather than risk that.
            _rebaking = false;
            probe.UpdateMode = GodotProbe.UpdateModeEnum.Once;
            return;
        }

        _frameHooked = true;
        RenderingServer.FramePostDraw += EndRebake;
    }

    private void EndRebake()
    {
        var probe = PlatformNode;
        UnhookFrame(probe);
        _rebaking = false;

        if (probe != null && GodotObject.IsInstanceValid(probe) && Owner is { IsDestroyed: false }
            && Owner.UpdateMode.Value == ProbeUpdateMode.Once)
        {
            probe.UpdateMode = GodotProbe.UpdateModeEnum.Once;
        }
    }

    private void UnhookFrame(GodotProbe? probe)
    {
        if (!_frameHooked)
            return;
        _frameHooked = false;
        RenderingServer.FramePostDraw -= EndRebake;
    }

    // The settings screen writes from its own world's thread, so bounce the re-apply through ours.
    private void OnSettingsChanged()
    {
        var owner = Owner;
        var world = owner?.World;
        if (owner == null || world == null || owner.IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (!owner.IsDestroyed)
                owner.MarkChangeDirty();
        });
    }

    public override void Destroy(bool destroyingWorld)
    {
        EngineSettings.Changed -= OnSettingsChanged;
        UnhookFrame(PlatformNode);
        if (_debugBounds != null && GodotObject.IsInstanceValid(_debugBounds)) _debugBounds.QueueFree();
        _debugBounds = null;
        _debugMesh?.Dispose();
        _debugMaterial?.Dispose();
        base.Destroy(destroyingWorld);
    }
}
