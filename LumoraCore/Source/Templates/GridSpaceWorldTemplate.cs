// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.GodotUI.Wizards;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Templates;

internal sealed class GridSpaceWorldTemplate : WorldTemplateDefinition
{
private const string GridGroundShader = @"
shader_type spatial;
render_mode cull_back;

uniform vec4 base_near_color : source_color = vec4(0.15, 0.16, 0.24, 1.0);
uniform vec4 base_far_color : source_color = vec4(0.06, 0.08, 0.14, 1.0);
uniform vec4 line_near_color : source_color = vec4(0.62, 0.69, 1.00, 1.0);
uniform vec4 line_far_color : source_color = vec4(0.34, 0.44, 0.82, 1.0);
uniform float minor_scale : hint_range(0.1, 8.0) = 1.0;
uniform float major_scale : hint_range(1.0, 64.0) = 8.0;
uniform float line_width : hint_range(0.2, 2.5) = 1.6;
uniform float major_line_width : hint_range(0.4, 3.5) = 2.3;
uniform float radial_fade : hint_range(0.2, 3.0) = 1.1;

float grid_line(vec2 uv, float scale, float width_px)
{
    vec2 cell = uv / scale;
    vec2 dist = abs(fract(cell - 0.5) - 0.5) / max(fwidth(cell), vec2(0.0001));
    float line = min(dist.x, dist.y);
    return 1.0 - smoothstep(width_px, width_px + 1.0, line);
}

void fragment()
{
    float minor = grid_line(UV, minor_scale, line_width);
    float major = grid_line(UV, major_scale, major_line_width);
    float grid_mask = clamp(max(minor * 0.6, major), 0.0, 1.0);

    float radial = clamp(length(UV - vec2(0.5)) * radial_fade, 0.0, 1.0);
    vec3 base_col = mix(base_near_color.rgb, base_far_color.rgb, radial);
    vec3 line_col = mix(line_near_color.rgb, line_far_color.rgb, radial);

    ALBEDO = mix(base_col, line_col, grid_mask);
    EMISSION = line_col * grid_mask * 0.2;
    ROUGHNESS = 0.95;
    METALLIC = 0.0;
}
";

    public GridSpaceWorldTemplate() : base("Grid") { }

    protected override void Build(World world)
    {
        var spawnSlot = world.RootSlot.AddSlot("SpawnArea");
        spawnSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
        spawnSlot.Tag.Value = "spawn";
        spawnSlot.AttachComponent<SimpleUserSpawn>();

        var lightSlot = world.RootSlot.AddSlot("DirectionalLight");
        lightSlot.LocalPosition.Value = new float3(0f, 10f, 0f);
        lightSlot.LocalRotation.Value = floatQ.Euler(0.785f, -0.785f, 0f);

        var groundSlot = world.RootSlot.AddSlot("Ground");
        groundSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
        groundSlot.Tag.Value = "floor";

        var groundMesh = groundSlot.AttachComponent<BoxMesh>();
        groundMesh.Size.Value = new float3(100f, 0.1f, 100f);
        groundMesh.UVScale.Value = new float3(100f, 1f, 100f);

        var groundMaterial = groundSlot.AttachComponent<CustomShaderMaterial>();
        groundMaterial.InlineShaderSource.Value = GridGroundShader;
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

        var orbSlot = world.RootSlot.AddSlot("GridMaterialOrb");
        orbSlot.LocalPosition.Value = new float3(0.95f, 1.25f, -1.05f);
        orbSlot.AttachComponent<Grabbable>();

        var orbMesh = orbSlot.AttachComponent<SphereMesh>();
        orbMesh.Radius.Value = 0.17f;
        orbMesh.Segments.Value = 28;
        orbMesh.Rings.Value = 16;
        orbMesh.UVScale.Value = new float2(6f, 3f);

        var orbRenderer = orbSlot.AttachComponent<MeshRenderer>();
        orbRenderer.Mesh.Target = orbMesh;
        orbRenderer.Material.Target = groundMaterial;

        var inspectorSlot = world.RootSlot.AddSlot("GridMaterialInspector");
        inspectorSlot.LocalPosition.Value = new float3(0.35f, 1.35f, -1.2f);
        var inspector = inspectorSlot.AttachComponent<GodotMaterialInspector>();
        inspector.Material.Target = groundMaterial;
        inspector.Size.Value = new float2(430, 560);
        inspector.PixelsPerUnit.Value = 900f;
    }
}
