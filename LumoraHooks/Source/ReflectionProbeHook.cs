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
// than being missed.
//
// A Once probe also bakes again as the world settles: it first comes up while meshes and textures are
// still streaming in, and a capture of untextured white rooms is what every glossy surface then
// reflects for good (black glass came out as white mirrors of the room). Settled is the count of assets
// in flight holding still for three seconds, whatever it is, so one asset stuck mid-load does not hold
// it off forever; a world that loads in waves gets a bake after each, at most four, for five minutes
// after the probe came up. Local only; nothing is synced. A FromCubemap probe whose cubemap has loaded
// captures that cubemap instead of the scene (SyncSeed), so no rebake of any kind can overwrite it. -xlinka
[ImplementableHook(typeof(LumoraProbe))]
public sealed class ReflectionProbeHook : NodeBackedComponentHook<LumoraProbe, GodotProbe>
{
    private int _lastBakeGeneration;
    private bool _rebaking;
    private bool _frameHooked;
    private bool _settled;
    private bool _settleHooked;
    private double _watchSince = -1;
    private double _stableSince = -1;
    private int _lastLoading = -1;
    private bool _changedSinceBake = true;
    private int _settleBakes;
    private const double StableSeconds = 3;
    private const double WatchSeconds = 300;
    private const int MaxSettleBakes = 4;
    private MeshInstance3D? _debugBounds;
    private ImmediateMesh? _debugMesh;
    private StandardMaterial3D? _debugMaterial;
    private bool _seeded;
    private MeshInstance3D? _seed;
    private global::Godot.ShaderMaterial? _seedMaterial;
    private static global::Godot.Shader? _seedShader;
    private static global::Godot.BoxMesh? _seedBox;
    private static readonly StringName SeedPicture = "picture";

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

        var picture = Owner.UpdateMode.Value == ProbeUpdateMode.FromCubemap
            ? (Owner.Cubemap.Asset?.Hook as CubemapAssetHook)?.GodotCubemap : null;
        _seeded = picture != null && GodotObject.IsInstanceValid(picture);
        SyncSeed(probe, _seeded ? picture : null);
        probe.EnableShadows = Owner.CaptureShadows.Value && !_seeded;

        // Capture the focused world and the real local head. Diagnostic geometry, private overlays,
        // headless eye-view twins and planar mirror surfaces must not contaminate the cubemap. A seeded
        // probe captures the hidden band alone, where its cubemap box is.
        int cullMask = Owner.CullMask.Value;
        probe.CullMask = _seeded ? (uint)Helpers.RenderHelper.HIDDEN_LAYER
            : (uint)((cullMask != 0 ? cullMask : Helpers.RenderHelper.PUBLIC_RENDER_MASK)
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

        if (rebakeRequested && enabled && Owner.UpdateMode.Value != ProbeUpdateMode.Always)
        {
            _lastBakeGeneration = generation;
            BeginRebake(probe);
        }
        else if (!_rebaking)
            probe.UpdateMode = Owner.UpdateMode.Value == ProbeUpdateMode.Always
                ? GodotProbe.UpdateModeEnum.Always
                : GodotProbe.UpdateModeEnum.Once;

        if (!_settled && !_settleHooked && enabled && CapturesSceneOnce)
        {
            _settleHooked = true;
            _watchSince = -1;
            _stableSince = -1;
            _lastLoading = -1;
            _changedSinceBake = true;
            _settleBakes = 0;
            RenderingServer.FramePostDraw += WatchSettle;
        }
    }

    // Only a probe whose one capture is of the scene goes stale as the world settles.
    private bool CapturesSceneOnce => !_seeded && Owner.UpdateMode.Value != ProbeUpdateMode.Always;

    private void WatchSettle()
    {
        var probe = PlatformNode;
        var owner = Owner;
        if (probe == null || !GodotObject.IsInstanceValid(probe) || owner == null || owner.IsDestroyed
            || !CapturesSceneOnce)
        {
            UnhookSettle();
            return;
        }
        if (!probe.IsVisibleInTree() || owner.World is not { } world)
            return;
        double now = world.TotalTime;
        if (_watchSince < 0)
            _watchSince = now;
        int loading = Lumora.Core.Assets.Asset.LoadingCount;
        if (loading != _lastLoading)
        {
            _lastLoading = loading;
            _stableSince = now;
            _changedSinceBake = true;
        }
        if (_changedSinceBake && now - _stableSince >= StableSeconds && !_rebaking)
        {
            _changedSinceBake = false;
            _settleBakes++;
            BeginRebake(probe);
        }
        if (_settleBakes >= MaxSettleBakes || now - _watchSince >= WatchSeconds)
        {
            _settled = true;
            UnhookSettle();
        }
    }

    private void UnhookSettle()
    {
        if (!_settleHooked)
            return;
        _settleHooked = false;
        RenderingServer.FramePostDraw -= WatchSettle;
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

    // Godot's probe takes no texture, so a cubemap reaches it the one way a probe sees anything: drawn. A
    // small box sits at the capture point with the cubemap painted along every ray from its centre, on the
    // hidden band, and a seeded probe captures that band alone, so all six faces come out as the cubemap
    // with its stored light values untouched (unshaded, no fog, and a capture is never tone-mapped; the
    // eye's own tone mapping is the only one it meets). The box stays up for good: a probe captures again
    // whenever any of its settings or its transform changes, and with the box always there every capture
    // is the cubemap again. Nothing else draws it. The eyes leave the hidden band out, and a camera that
    // does draw that band stands outside the box, where the vertex stage folds the box to a point; so does
    // every other probe. Godot's own box projection, blending and roughness filtering apply as for any
    // capture. -xlinka
    private void SyncSeed(GodotProbe probe, global::Godot.Cubemap? picture)
    {
        if (picture == null)
        {
            if (_seed != null && GodotObject.IsInstanceValid(_seed))
                _seed.Visible = false;
            return;
        }
        if (_seed == null || !GodotObject.IsInstanceValid(_seed))
        {
            _seedShader ??= new global::Godot.Shader { Code = SeedShaderCode };
            _seedBox ??= new global::Godot.BoxMesh { Size = new Vector3(2f, 2f, 2f) };
            _seedMaterial = new global::Godot.ShaderMaterial { Shader = _seedShader };
            _seed = new MeshInstance3D
            {
                Name = "Probe cubemap",
                Mesh = _seedBox,
                MaterialOverride = _seedMaterial,
                Layers = Helpers.RenderHelper.HIDDEN_LAYER,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            probe.AddChild(_seed);
        }
        _seedMaterial!.SetShaderParameter(SeedPicture, picture);
        var offset = Owner.OriginOffset.Value;
        _seed.Position = new Vector3(offset.x, offset.y, offset.z);
        float half = SeedHalfSize(Owner.Slot);
        _seed.Scale = new Vector3(half, half, half);
        _seed.Visible = true;
    }

    // Ten centimetres across in the world, and never inside the capture's near plane, which sits a
    // hundredth of a unit out in the probe's own space.
    private static float SeedHalfSize(Slot? slot)
    {
        if (slot == null)
            return 0.05f;
        var scale = slot.GlobalScale;
        float largest = System.MathF.Max(System.MathF.Abs(scale.x), System.MathF.Max(System.MathF.Abs(scale.y), System.MathF.Abs(scale.z)));
        return largest > 1e-6f ? System.MathF.Max(0.05f / largest, 0.02f) : 0.05f;
    }

    private const string SeedShaderCode = @"shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled;

uniform samplerCube picture : source_color, filter_linear_mipmap;

varying vec3 ray;

void vertex() {
	vec3 eye = (inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
	if (any(greaterThan(abs(eye), vec3(1.0)))) {
		VERTEX = vec3(0.0);
	}
	ray = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz - CAMERA_POSITION_WORLD;
}

void fragment() {
	ALBEDO = texture(picture, ray).rgb;
}
";

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
            && Owner.UpdateMode.Value != ProbeUpdateMode.Always)
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
        UnhookSettle();
        if (_debugBounds != null && GodotObject.IsInstanceValid(_debugBounds)) _debugBounds.QueueFree();
        _debugBounds = null;
        if (_seed != null && GodotObject.IsInstanceValid(_seed)) _seed.QueueFree();
        _seed = null;
        _seedMaterial?.Dispose();
        _seedMaterial = null;
        _debugMesh?.Dispose();
        _debugMaterial?.Dispose();
        base.Destroy(destroyingWorld);
    }
}
