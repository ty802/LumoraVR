// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Math;
using LumoraParticleSystem = Lumora.Core.Components.ParticleSystem;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// Renders a ParticleSystem's particles as one MultiMesh draw call. The simulation lives in LumoraSimulation
// and is driven by the engine component (emitters/modules are engine components too); this hook only pulls
// the finished position/size/rotation/color buffers each frame and writes the instance buffer in a single
// upload - per-instance Set* calls cost a marshalling round-trip each, which at particle counts IS the
// frame budget. -xlinka
[ImplementableHook(typeof(LumoraParticleSystem))]
public sealed partial class ParticleSystemHook : ComponentHook<LumoraParticleSystem>
{
    private const string ShaderPath = "res://Shaders/EngineParticle.gdshader";
    private const int InstanceStride = 16; // 12 transform + 4 color

    private ParticleProcessNode _processNode = null!;
    private MultiMeshInstance3D _instance = null!;
    private MultiMesh _multiMesh = null!;
    private SphereMesh _particleMesh = null!;
    private ShaderMaterial _material = null!;
    private float[] _buffer = System.Array.Empty<float>();
    private int _capacity;
    private int _lastRenderVersion = -1;

    public static IHook<LumoraParticleSystem> Constructor() => new ParticleSystemHook();

    public override void Initialize()
    {
        base.Initialize();

        _processNode = new ParticleProcessNode(this) { Name = "ParticleSystem" };
        attachedNode.AddChild(_processNode);

        _particleMesh = new SphereMesh
        {
            Radius = 1.0f,
            Height = 2.0f,
            RadialSegments = 8,
            Rings = 4
        };

        _material = new ShaderMaterial();
        if (ResourceLoader.Exists(ShaderPath))
            _material.Shader = GD.Load<Shader>(ShaderPath);
        else
            LumoraLogger.Warn($"ParticleSystemHook: Particle shader not found at {ShaderPath}");

        _multiMesh = new MultiMesh { Mesh = _particleMesh };
        _instance = new MultiMeshInstance3D
        {
            Name = "ParticleMultiMesh",
            Multimesh = _multiMesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _processNode.AddChild(_instance);

        ApplyChanges();
    }

    public override void ApplyChanges()
    {
        if (_multiMesh == null || _instance == null)
            return;

        var renderQueue = System.Math.Clamp(Owner.RenderQueue.Value, -100, 10000);
        _material.RenderPriority = System.Math.Clamp(renderQueue, -128, 127);
        _instance.SortingOffset = renderQueue;
        _instance.Visible = Owner.Enabled.Value;
        _material.SetShaderParameter("emission_strength", Owner.EmissionStrength.Value);
        ApplyBounds();
    }

    private void ApplyBounds()
    {
        // Generous custom AABB: particles are sim'd engine-side in slot-local space, so the extents plus
        // travel headroom keeps Godot from frustum-culling a system whose node origin is off screen.
        var extents = Owner.EmitterExtents.Value;
        float height = System.Math.Max(Owner.InitialSpeed.Value * Owner.Lifetime.Value * 1.8f, 1f);
        float pad = System.Math.Max(System.Math.Max(extents.x, extents.z), 1f);
        _instance.CustomAabb = new Aabb(
            new Vector3(-pad, -height, -pad),
            new Vector3(pad * 2f, height * 2f, pad * 2f));
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld && _processNode != null && GodotObject.IsInstanceValid(_processNode))
            _processNode.QueueFree();
        base.Destroy(destroyingWorld);
    }

    internal void PullRender()
    {
        if (Owner == null || Owner.IsDestroyed || _multiMesh == null)
            return;
        if (Owner.RenderVersion == _lastRenderVersion)
            return;
        _lastRenderVersion = Owner.RenderVersion;

        var positions = Owner.RenderPositions;
        var sizes = Owner.RenderSizes;
        var colors = Owner.RenderColors;
        int count = Owner.ParticleCount;
        if (positions == null || count <= 0)
        {
            _multiMesh.VisibleInstanceCount = 0;
            return;
        }

        if (_capacity < count)
        {
            _capacity = count;
            _multiMesh.InstanceCount = 0;
            _multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            _multiMesh.UseColors = true;
            _multiMesh.InstanceCount = _capacity;
            _buffer = new float[_capacity * InstanceStride];
        }

        // Two loops rather than one with a branch inside: the rotation path costs a quaternion-to-basis
        // per particle and most systems never spin anything, so the sim tells us whether it is even
        // integrating rotation and the common case stays a plain diagonal write. -xlinka
        if (Owner.HasRotations)
            WriteRotatedInstances(positions, sizes, colors, Owner.RenderRotations, count);
        else
            WriteAxisAlignedInstances(positions, sizes, colors, count);

        for (int i = count; i < _capacity; i++)
            System.Array.Clear(_buffer, i * InstanceStride, InstanceStride);

        _multiMesh.Buffer = _buffer;
        _multiMesh.VisibleInstanceCount = count;
    }

    private void WriteAxisAlignedInstances(float3[] positions, float3[] sizes, colorHDR[] colors, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int o = i * InstanceStride;
            var p = positions[i];
            var s = sizes[i];
            // Row-major 3x4: per-axis scale on the diagonal, position in the 4th column.
            _buffer[o + 0] = s.x; _buffer[o + 1] = 0f; _buffer[o + 2] = 0f; _buffer[o + 3] = p.x;
            _buffer[o + 4] = 0f; _buffer[o + 5] = s.y; _buffer[o + 6] = 0f; _buffer[o + 7] = p.y;
            _buffer[o + 8] = 0f; _buffer[o + 9] = 0f; _buffer[o + 10] = s.z; _buffer[o + 11] = p.z;
            var c = colors[i];
            _buffer[o + 12] = c.r;
            _buffer[o + 13] = c.g;
            _buffer[o + 14] = c.b;
            _buffer[o + 15] = c.a;
        }
    }

    private void WriteRotatedInstances(float3[] positions, float3[] sizes, colorHDR[] colors, floatQ[] rotations, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int o = i * InstanceStride;
            var p = positions[i];
            var s = sizes[i];
            var q = rotations[i];

            // Basis columns straight from the quaternion, each scaled by its own axis size.
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
            float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;

            float m00 = (1f - 2f * (yy + zz)) * s.x;
            float m10 = (2f * (xy + wz)) * s.x;
            float m20 = (2f * (xz - wy)) * s.x;
            float m01 = (2f * (xy - wz)) * s.y;
            float m11 = (1f - 2f * (xx + zz)) * s.y;
            float m21 = (2f * (yz + wx)) * s.y;
            float m02 = (2f * (xz + wy)) * s.z;
            float m12 = (2f * (yz - wx)) * s.z;
            float m22 = (1f - 2f * (xx + yy)) * s.z;

            _buffer[o + 0] = m00; _buffer[o + 1] = m01; _buffer[o + 2] = m02; _buffer[o + 3] = p.x;
            _buffer[o + 4] = m10; _buffer[o + 5] = m11; _buffer[o + 6] = m12; _buffer[o + 7] = p.y;
            _buffer[o + 8] = m20; _buffer[o + 9] = m21; _buffer[o + 10] = m22; _buffer[o + 11] = p.z;
            var c = colors[i];
            _buffer[o + 12] = c.r;
            _buffer[o + 13] = c.g;
            _buffer[o + 14] = c.b;
            _buffer[o + 15] = c.a;
        }
    }

    // Node3D, NOT a plain Node: Godot only propagates visibility down through Node3D ancestors, so a plain
    // Node between WorldRoot and the MultiMeshInstance severs the chain - backgrounding a world (WorldRoot
    // Visible=false) then failed to hide the particles while ProcessMode still froze them, leaving frozen
    // particles bleeding into the next world. Kept at identity; particle transforms live in the buffer. -xlinka
    private sealed partial class ParticleProcessNode : Node3D
    {
        private readonly ParticleSystemHook _hook;
        public ParticleProcessNode(ParticleSystemHook hook) => _hook = hook;
        public ParticleProcessNode() => _hook = null!;

        public override void _Process(double delta)
        {
            _hook?.PullRender();
        }
    }
}
