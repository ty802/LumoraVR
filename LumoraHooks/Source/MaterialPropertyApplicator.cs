// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Godot;
using Lumora.Core.Assets;

namespace Lumora.Godot.Hooks;

internal static class MaterialPropertyApplicator
{
    public static void Apply(Material material, MaterialType materialType, string property, object value)
    {
        if (material is StandardMaterial3D standardMaterial)
        {
            ApplyStandard(standardMaterial, property, value);
        }
        else if (material is ShaderMaterial shaderMaterial)
        {
            ApplyShader(shaderMaterial, materialType, property, value);
        }
    }

    public static Material CloneWithBlock(Material material, MaterialType materialType, IReadOnlyDictionary<string, object> properties)
    {
        if (material == null)
        {
            return null;
        }

        var clone = material.Duplicate(true) as Material;
        if (clone == null)
        {
            return material;
        }

        foreach (var (property, value) in properties)
        {
            Apply(clone, materialType, property, value);
        }

        return clone;
    }

    public static void ApplyRenderPriority(Material material, int renderQueue)
    {
        if (material == null)
        {
            return;
        }

        material.RenderPriority = renderQueue < 0 ? 0 : System.Math.Clamp(renderQueue, -128, 127);
    }

    public static Variant ToVariant(object value)
    {
        return value switch
        {
            float f => Variant.From(f),
            double d => Variant.From((float)d),
            int i => Variant.From(i),
            byte byteValue => Variant.From((int)byteValue),
            bool boolValue => Variant.From(boolValue),
            string s => Variant.From(s),
            Vector2 v2 => Variant.From(v2),
            Vector3 v3 => Variant.From(v3),
            Vector4 v4 => Variant.From(v4),
            Color c => Variant.From(c),
            Texture2D tex => Variant.From(tex),
            Lumora.Core.Math.float2 f2 => Variant.From(new Vector2(f2.x, f2.y)),
            Lumora.Core.Math.float3 f3 => Variant.From(new Vector3(f3.x, f3.y, f3.z)),
            Lumora.Core.Math.float4 f4 => Variant.From(new Vector4(f4.x, f4.y, f4.z, f4.w)),
            Lumora.Core.Math.colorHDR hdr => Variant.From(new Color(hdr.r, hdr.g, hdr.b, hdr.a)),
            _ => default
        };
    }

    private static void ApplyStandard(StandardMaterial3D material, string property, object value)
    {
        switch (property)
        {
            case "TintColor":
            case "AlbedoColor":
                if (value is Color c) material.AlbedoColor = c;
                break;
            case "Texture":
            case "AlbedoTexture":
                material.AlbedoTexture = value as Texture2D;
                break;
            case "Metallic":
                if (value is float metallic) material.Metallic = metallic;
                break;
            case "Smoothness":
                if (value is float smoothness) material.Roughness = 1.0f - smoothness;
                break;
            case "MetallicMap":
                material.MetallicTexture = value as Texture2D;
                if (material.MetallicTexture != null)
                {
                    material.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red;
                }
                break;
            case "NormalMap":
                material.NormalEnabled = value != null;
                material.NormalTexture = value as Texture2D;
                break;
            case "NormalScale":
                if (value is float normalScale) material.NormalScale = normalScale;
                break;
            case "EmissiveColor":
                if (value is Color emission)
                {
                    bool hasEmission = emission.R > 0 || emission.G > 0 || emission.B > 0;
                    material.EmissionEnabled = hasEmission;
                    if (hasEmission)
                    {
                        material.Emission = emission;
                        material.EmissionEnergyMultiplier = 1.0f;
                    }
                }
                break;
            case "EmissiveMap":
                material.EmissionTexture = value as Texture2D;
                material.EmissionEnabled = material.EmissionTexture != null;
                break;
            case "OcclusionMap":
                material.AOTexture = value as Texture2D;
                material.AOEnabled = material.AOTexture != null;
                break;
            case "AlphaCutoff":
                if (value is float alphaCutoff) material.AlphaScissorThreshold = alphaCutoff;
                break;
            case "TextureScale":
                if (value is Vector2 scale) material.Uv1Scale = new Vector3(scale.X, scale.Y, 1);
                break;
            case "TextureOffset":
                if (value is Vector2 offset) material.Uv1Offset = new Vector3(offset.X, offset.Y, 0);
                break;
            case "RenderQueue":
                if (value is int renderQueue) ApplyRenderPriority(material, renderQueue);
                if (value is float renderQueueFloat) ApplyRenderPriority(material, (int)renderQueueFloat);
                break;
        }
    }

    private static void ApplyShader(ShaderMaterial material, MaterialType materialType, string property, object value)
    {
        if (property == "RenderQueue")
        {
            if (value is int renderQueue) ApplyRenderPriority(material, renderQueue);
            if (value is float renderQueueFloat) ApplyRenderPriority(material, (int)renderQueueFloat);
            return;
        }

        string mappedParam = MapShaderProperty(materialType, property);

        if (value == null)
        {
            if (property is "Texture" or "AlbedoTexture")
            {
                material.SetShaderParameter("use_albedo_texture", false);
            }
            return;
        }

        Variant variantValue = ToVariant(value);
        if (variantValue.VariantType == Variant.Type.Nil)
        {
            return;
        }

        material.SetShaderParameter(mappedParam, variantValue);
        if (property is "Texture" or "AlbedoTexture")
        {
            material.SetShaderParameter("use_albedo_texture", value is Texture2D);
        }
    }

    private static string MapShaderProperty(MaterialType materialType, string property)
    {
        bool isUnlit = materialType is MaterialType.Unlit or MaterialType.UI_Unlit or MaterialType.UI_Text;
        bool isMetaball = materialType == MaterialType.Metaball;

        return property switch
        {
            "TintColor" when isUnlit => "albedo_color",
            "Texture" when isUnlit => "albedo_texture",
            "AlbedoColor" => "albedo_color",
            "AlbedoTexture" => "albedo_texture",
            "TextureScale" when isUnlit => "uv_scale",
            "TextureOffset" when isUnlit => "uv_offset",
            "AlphaCutoff" when isUnlit => "alpha_cutoff",
            "AlphaClip" when isUnlit => "alpha_clip",
            "UseVertexColor" when isUnlit => "use_vertex_color",
            "PixelRange" => "pixel_range",
            "RectClip" => "rect_clip",
            "ColorMask" => "color_mask",
            "StencilComparison" => "stencil_comparison",
            "StencilOperation" => "stencil_operation",
            "StencilID" => "stencil_id",
            "StencilWriteMask" => "stencil_write_mask",
            "StencilReadMask" => "stencil_read_mask",
            "ZWrite" => "z_write",
            "ZTest" => "z_test",
            "TintA" when isMetaball => "tint_a",
            "TintB" when isMetaball => "tint_b",
            "BlobRadius" when isMetaball => "blob_radius",
            "BlobSmoothness" when isMetaball => "blob_smoothness",
            "BlobCount" when isMetaball => "blob_count",
            "RiseSpeed" when isMetaball => "rise_speed",
            "VolumeExtents" when isMetaball => "volume_extents",
            "VolumeHeight" when isMetaball => "volume_height",
            "VolumeOffset" when isMetaball => "volume_offset",
            "RimStrength" when isMetaball => "rim_strength",
            "RimFalloff" when isMetaball => "rim_falloff",
            "FresnelPower" when isMetaball => "fresnel_power",
            "AlphaScale" when isMetaball => "alpha_scale",
            "EmissionStrength" when isMetaball => "emission_strength",
            "TimeScale" when isMetaball => "time_scale",
            _ => ToSnakeCase(property)
        };
    }

    private static string ToSnakeCase(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLowerInvariant(text[0]));

        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]))
            {
                result.Append('_');
                result.Append(char.ToLowerInvariant(text[i]));
            }
            else
            {
                result.Append(text[i]);
            }
        }

        return result.ToString();
    }
}
