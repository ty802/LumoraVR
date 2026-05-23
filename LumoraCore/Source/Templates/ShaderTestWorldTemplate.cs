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

internal sealed class ShaderTestWorldTemplate : WorldTemplateDefinition
{
    public ShaderTestWorldTemplate() : base("ShaderTest") { }

    protected override void Build(World world)
    {
        CreateSpawn(world);
        CreateLighting(world);
        CreateGround(world);
        CreateShaderOrbs(world);
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
}
