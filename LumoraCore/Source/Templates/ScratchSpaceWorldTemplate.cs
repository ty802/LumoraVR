// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Templates;

internal sealed class ScratchSpaceWorldTemplate : WorldTemplateDefinition
{
    public ScratchSpaceWorldTemplate() : base("Scratch") { }

    protected override void Build(World world)
    {
        CreateSpawn(world);
        CreateLighting(world);
        CreateGround(world);
        CreateShaderOrbs(world);
        CreateDevTool(world);
        CreatePhysicsProps(world);
        CreateSquishyTest(world);
    }

    private static void CreateSpawn(World world)
    {
        var spawnSlot = world.RootSlot.AddSlot("SpawnArea");
        spawnSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
        spawnSlot.Tag.Value = "spawn";
        spawnSlot.AttachComponent<SimpleUserSpawn>();
    }

    private static void CreateLighting(World world)
    {
        var lightSlot = world.RootSlot.AddSlot("DirectionalLight");
        lightSlot.LocalPosition.Value = new float3(0f, 10f, 0f);
        lightSlot.LocalRotation.Value = floatQ.Euler(-2.55f, -0.181f, 0f);
        var dirLight = lightSlot.AttachComponent<Light>();
        dirLight.Type.Value = LightType.Directional;
        dirLight.LightColor.Value = new color(1.00f, 0.91f, 0.82f, 1f);
        dirLight.Intensity.Value = 1.0f;
        dirLight.Shadows.Value = ShadowType.Soft;

        var skySlot = world.RootSlot.AddSlot("GradientSkybox");
        var skybox = skySlot.AttachComponent<GradientSkybox>();
        skybox.TopColor.Value = new color(0.18f, 0.22f, 0.34f, 1f);
        skybox.HorizonColor.Value = new color(0.44f, 0.48f, 0.56f, 1f);
        skybox.BottomColor.Value = new color(0.18f, 0.16f, 0.14f, 1f);
        skybox.SunColor.Value = new color(1.00f, 0.88f, 0.68f, 1f);
        skybox.SunDirection.Value = new float3(0.62f, 0.38f, -0.68f);
        skybox.SunSize.Value = 0.018f;
        skybox.SunIntensity.Value = 0.75f;
        skybox.SunGlowPower.Value = 24f;
        skybox.AmbientEnergy.Value = 0.85f;
    }

    private static void CreateGround(World world)
    {
        var groundSlot = world.RootSlot.AddSlot("Ground");
        groundSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
        groundSlot.Tag.Value = "floor";

        var groundMesh = groundSlot.AttachComponent<BoxMesh>();
        groundMesh.Size.Value = new float3(100f, 0.1f, 100f);
        groundMesh.UVScale.Value = new float3(100f, 1f, 100f);

        var groundMaterial = groundSlot.AttachComponent<GridSpaceGroundMaterial>();
        groundMaterial.BlendMode.Value = BlendMode.Opaque;
        groundMaterial.Culling.Value = Culling.Back;

        var groundRenderer = groundSlot.AttachComponent<MeshRenderer>();
        groundRenderer.Mesh.Target = groundMesh;
        groundRenderer.Material.Target = groundMaterial;
        groundRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        var groundCollider = groundSlot.AttachComponent<BoxCollider>();
        groundCollider.Type.Value = ColliderType.Static;
        groundCollider.Size.Value = groundMesh.Size.Value;
        groundCollider.Offset.Value = new float3(0f, -groundMesh.Size.Value.y * 0.5f, 0f);
    }

    private static void CreateShaderOrbs(World world)
    {
        var root = world.RootSlot.AddSlot("ShaderMaterialOrbs");
        root.LocalPosition.Value = new float3(0f, 1.2f, -2.4f);

        int index = 0;
        CreateOrb<PBS_Metallic>(root, "PBS_Metallic", index++, material =>
        {
            material.AlbedoColor.Value = new colorHDR(0.86f, 0.32f, 0.22f, 1f);
            material.Metallic.Value = 1.0f;
            material.Smoothness.Value = 0.82f;
        });

        CreateOrb<PBS_Specular>(root, "PBS_Specular", index++, material =>
        {
            material.AlbedoColor.Value = new colorHDR(0.22f, 0.44f, 0.92f, 1f);
            material.SpecularColor.Value = new colorHDR(1.0f, 0.92f, 0.72f, 1f);
            material.Smoothness.Value = 0.9f;
        });

        CreateOrb<UnlitMaterial>(root, "Unlit", index++, material =>
        {
            material.TintColor.Value = new colorHDR(0.42f, 1.0f, 0.52f, 1f);
            material.UseVertexColor.Value = false;
            material.Culling.Value = Culling.None;
        });

        CreateOrb<UIUnlitMaterial>(root, "UI_Unlit", index++, material =>
        {
            material.TintColor.Value = new colorHDR(0.34f, 0.92f, 1.0f, 0.86f);
            material.UseVertexColor.Value = false;
            material.AlphaClip.Value = false;
            material.BlendMode.Value = BlendMode.Alpha;
            material.Culling.Value = Culling.None;
        });

        CreateOrb<GridSpaceGroundMaterial>(root, "GridSpaceGround", index++, material =>
        {
            material.BaseNearColor.Value = new colorHDR(0.045f, 0.040f, 0.035f, 1f);
            material.LineNearColor.Value = new colorHDR(0.34f, 0.58f, 0.98f, 1f);
            material.LineWidth.Value = 1.3f;
        });

        CreateOrb<MetaballMaterial>(root, "Metaball", index++, material =>
        {
            material.BlobCount.Value = 36;
            material.VolumeExtents.Value = new float2(1.4f, 1.4f);
            material.VolumeHeight.Value = 1.4f;
            material.VolumeOffset.Value = new float3(0f, -0.3f, 0f);
            material.Culling.Value = Culling.None;
        });

        CreateBoxPreview<LocalHomeRisingMaterial>(root, "LocalHomeRising", index++, new float3(0.42f, 0.42f, 0.42f), material =>
        {
            material.BlobCount.Value = 36;
            material.VolumeExtents.Value = new float2(0.2f, 0.2f);
            material.VolumeHeight.Value = 0.42f;
            material.VolumeOffset.Value = new float3(0f, -0.21f, 0f);
            material.Culling.Value = Culling.None;
        });

        CreateOrb<FresnelMaterial>(root, "Fresnel", index++, material =>
        {
            material.NearColor.Value = new colorHDR(0.04f, 0.04f, 0.06f, 1f);
            material.FarColor.Value = new colorHDR(0.4f, 0.95f, 1.0f, 1f);
        });

        CreateCustomOrb(root, "UnlitTransparent", index++, "res://Shaders/UnlitTransparent.gdshader", BlendMode.Alpha, Culling.None, material =>
        {
            AddColorParam(material, "albedo_color", new colorHDR(0.34f, 0.92f, 1.0f, 0.45f));
            AddBoolParam(material, "use_vertex_color", false);
        });
        CreateCustomOrb(root, "EngineParticle", index++, "res://Shaders/EngineParticle.gdshader", BlendMode.Additive, Culling.Back, material =>
        {
            AddFloatParam(material, "emission_strength", 1.8f);
        });
        CreateCustomOrb(root, "DebugGrid", index++, "res://Assets/Shaders/DebugGrid.gdshader", BlendMode.Opaque, Culling.Back);
    }

    private static T CreateOrb<T>(Slot parent, string name, int index, Action<T>? configure = null)
        where T : MaterialProvider, new()
    {
        var slot = parent.AddSlot(name);
        slot.LocalPosition.Value = GetOrbPosition(index);
        slot.AttachComponent<Grabbable>();

        var mesh = slot.AttachComponent<SphereMesh>();
        mesh.Radius.Value = 0.18f;
        mesh.Segments.Value = 32;
        mesh.Rings.Value = 16;
        mesh.UVScale.Value = new float2(5f, 2.5f);

        var collider = slot.AttachComponent<SphereCollider>();
        collider.Radius.Value = mesh.Radius.Value;
        collider.Type.Value = ColliderType.Trigger;

        var material = slot.AttachComponent<T>();
        configure?.Invoke(material);

        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.On;

        return material;
    }

    private static T CreateBoxPreview<T>(Slot parent, string name, int index, float3 size, Action<T>? configure = null)
        where T : MaterialProvider, new()
    {
        var slot = parent.AddSlot(name);
        slot.LocalPosition.Value = GetOrbPosition(index);
        slot.AttachComponent<Grabbable>();

        var mesh = slot.AttachComponent<BoxMesh>();
        mesh.Size.Value = size;
        mesh.UVScale.Value = new float3(2f, 2f, 2f);

        var collider = slot.AttachComponent<BoxCollider>();
        collider.Size.Value = size;
        collider.Type.Value = ColliderType.Trigger;

        var material = slot.AttachComponent<T>();
        configure?.Invoke(material);

        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.On;

        return material;
    }

    private static void CreateCustomOrb(Slot parent, string name, int index, string shaderPath, BlendMode blendMode, Culling culling, Action<CustomShaderMaterial>? configure = null)
    {
        CreateOrb<CustomShaderMaterial>(parent, name, index, material =>
        {
            material.ShaderPath.Value = shaderPath;
            material.BlendMode.Value = blendMode;
            material.Culling.Value = culling;
            configure?.Invoke(material);
        });
    }

    private static void AddColorParam(CustomShaderMaterial material, string name, colorHDR value)
    {
        var param = material.Parameters.Add();
        param.Name.Value = name;
        param.Type.Value = ShaderUniformType.Vec4;
        param.IsColor.Value = true;
        param.Value.Value = new float4(value.r, value.g, value.b, value.a);
    }

    private static void AddFloatParam(CustomShaderMaterial material, string name, float value)
    {
        var param = material.Parameters.Add();
        param.Name.Value = name;
        param.Type.Value = ShaderUniformType.Float;
        param.Value.Value = new float4(value, 0f, 0f, 0f);
    }

    private static void AddBoolParam(CustomShaderMaterial material, string name, bool value)
    {
        var param = material.Parameters.Add();
        param.Name.Value = name;
        param.Type.Value = ShaderUniformType.Bool;
        param.Value.Value = new float4(value ? 1f : 0f, 0f, 0f, 0f);
    }

    private static float3 GetOrbPosition(int index)
    {
        const int columns = 6;
        const float spacing = 0.58f;
        int column = index % columns;
        int row = index / columns;
        float x = (column - (columns - 1) * 0.5f) * spacing;
        float z = row * spacing;
        return new float3(x, 0f, z);
    }

    // A grabbable dev tool on a stand: grip it with the hand and the dev actions come alive
    // (the radial menu shows Inspector while it's equipped).
    private static void CreateDevTool(World world)
    {
        var toolSlot = world.RootSlot.AddSlot("Dev Tool");
        toolSlot.LocalPosition.Value = new float3(-1.6f, 1.0f, -1.6f);

        // DevToolItem builds its own cone visual - adding one here doubled it.
        var collider = toolSlot.AttachComponent<BoxCollider>();
        collider.Size.Value = new float3(0.12f, 0.26f, 0.12f);

        toolSlot.AttachComponent<Grabbable>();
        toolSlot.AttachComponent<Components.Interaction.DevToolItem>();
    }

    private static void CreatePhysicsProps(World world)
    {
        var props = world.RootSlot.AddSlot("Physics Props");
        props.LocalPosition.Value = new float3(2.4f, 0f, -1.2f);

        CreatePropCube(props, "Cube A", new float3(0f, 1.2f, 0f), new colorHDR(0.9f, 0.45f, 0.2f, 1f));
        CreatePropCube(props, "Cube B", new float3(0.15f, 2.0f, 0.1f), new colorHDR(0.3f, 0.8f, 0.45f, 1f));
        CreatePropCube(props, "Cube C", new float3(-0.12f, 2.8f, -0.08f), new colorHDR(0.4f, 0.5f, 0.95f, 1f));
    }

    private static void CreatePropCube(Slot parent, string name, float3 position, colorHDR tint)
    {
        var cube = parent.AddSlot(name);
        cube.LocalPosition.Value = position;

        var mesh = cube.AttachComponent<BoxMesh>();
        mesh.Size.Value = float3.One * 0.35f;

        var material = cube.AttachComponent<PBS_Metallic>();
        material.AlbedoColor.Value = tint;
        material.Metallic.Value = 0.1f;
        material.Smoothness.Value = 0.5f;

        var renderer = cube.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;

        var collider = cube.AttachComponent<BoxCollider>();
        collider.Size.Value = mesh.Size.Value;

        var body = cube.AttachComponent<RigidBody>();
        body.Mass.Value = 1f;

        cube.AttachComponent<Grabbable>();
    }

    // OUR CPU soft bodies (SquishyBody) - front and centre. A jelly ball that falls + squishes on the
    // floor, and a cloth pinned at the top that drapes. Both collide via OUR sim (the thing Jolt can't).
    private static void CreateSquishyTest(World world)
    {
        // Jelly ball: falls from a height right in front of spawn, lands and squishes on the floor.
        var jelly = world.RootSlot.AddSlot("Squishy Jelly");
        jelly.LocalPosition.Value = new float3(-0.7f, 1.8f, -2.2f);

        var jellyMesh = jelly.AttachComponent<SphereMesh>();
        jellyMesh.Radius.Value = 0.4f;
        jellyMesh.Segments.Value = 18;

        var jellyMat = jelly.AttachComponent<PBS_Metallic>();
        jellyMat.AlbedoColor.Value = new colorHDR(0.45f, 0.9f, 0.6f, 1f);
        jellyMat.Metallic.Value = 0f;
        jellyMat.Smoothness.Value = 0.55f;

        var squishy = jelly.AttachComponent<SquishyBody>();
        squishy.SourceMesh.Target = jellyMesh;
        squishy.Material.Target = jellyMat;
        squishy.Stiffness.Value = 0.3f;        // light - shape retention does the shaping
        squishy.Damping.Value = 0.06f;
        squishy.Iterations.Value = 6;
        squishy.Pressure.Value = 0f;
        squishy.ShapeRetention.Value = 0.3f;   // holds the ball form, squishes + springs back, never explodes
        squishy.ParticleRadius.Value = 0.05f;
        squishy.GroundY.Value = 0f;             // the scratch floor top sits at y=0 (no raycast, just a plane)
        squishy.CollideWithWorld.Value = false; // world raycast per particle is costly; ground plane is enough here

        // A solid box for the cloth to drape over (the drape target).
        var table = world.RootSlot.AddSlot("Drape Box");
        table.LocalPosition.Value = new float3(0.9f, 0.45f, -2.2f);
        var tableMesh = table.AttachComponent<BoxMesh>();
        tableMesh.Size.Value = new float3(0.7f, 0.7f, 0.7f);
        var tableMat = table.AttachComponent<PBS_Metallic>();
        tableMat.AlbedoColor.Value = new colorHDR(0.5f, 0.5f, 0.55f, 1f);
        var tableRenderer = table.AttachComponent<MeshRenderer>();
        tableRenderer.Mesh.Target = tableMesh;
        tableRenderer.Material.Target = tableMat;
        var tableCollider = table.AttachComponent<BoxCollider>();
        tableCollider.Type.Value = ColliderType.Static;
        tableCollider.Size.Value = tableMesh.Size.Value;
        // The cloth finds this BoxCollider (and any other world collider) through CollideWithWorld's
        // resting-contact resolve - no per-object soft collider needed anymore. -xlinka

        // HORIZONTAL cloth dropped onto the box -> drapes over it like a tablecloth. Grid is on the
        // local XY plane (normal +Z); rotate -90 about X so it lies flat, normal up.
        var cloth = world.RootSlot.AddSlot("Squishy Cloth");
        cloth.LocalPosition.Value = new float3(0.9f, 1.3f, -2.2f);
        cloth.LocalRotation.Value = floatQ.AxisAngleRad(float3.Right, -MathF.PI * 0.5f);

        var clothMesh = cloth.AttachComponent<GridMesh>();
        clothMesh.Size.Value = new float2(1.6f, 1.6f);
        clothMesh.SegmentsX.Value = 20;
        clothMesh.SegmentsY.Value = 20;

        var clothMat = cloth.AttachComponent<PBS_Metallic>();
        clothMat.AlbedoColor.Value = new colorHDR(0.9f, 0.35f, 0.55f, 1f);
        clothMat.Metallic.Value = 0f;
        clothMat.Smoothness.Value = 0.4f;
        clothMat.Culling.Value = Culling.None;

        var clothSquishy = cloth.AttachComponent<SquishyBody>();
        clothSquishy.SourceMesh.Target = clothMesh;
        clothSquishy.Material.Target = clothMat;
        clothSquishy.Stiffness.Value = 0.9f;   // stiff edges hold the sheet; the folds come from draping
        clothSquishy.Damping.Value = 0.18f;    // bleeds off swing energy so it settles instead of buzzing
        clothSquishy.Iterations.Value = 14;
        clothSquishy.Pressure.Value = 0f;      // cloth, not a balloon
        clothSquishy.ParticleRadius.Value = 0.05f;
        clothSquishy.GroundY.Value = 0f;
        clothSquishy.CollideWithWorld.Value = true; // rests on the box AND any orb/prop/avatar with a collider
        // no pins - it's a free sheet that falls onto the box
    }
}
