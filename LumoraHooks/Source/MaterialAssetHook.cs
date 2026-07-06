// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

/// <summary>
/// Godot implementation of material asset hook.
/// Creates and manages Godot StandardMaterial3D or ShaderMaterial resources.
/// </summary>
[ImplementableHook(typeof(MaterialAsset))]
public class MaterialAssetHook : AssetHook, IMaterialAssetHook
{
    private StandardMaterial3D _standardMaterial = null!;
    private ShaderMaterial _shaderMaterial = null!;
    private MaterialType _materialType;
    private bool _uiZWriteOn;
    private bool _uiZTestOff;
    private bool _usesShaderMaterial;
    private string _customShaderPath = null!;
    private int _renderQueue = -1;

    // Pending properties to apply
    private readonly Dictionary<string, object> _pendingProperties = new();

    /// <summary>
    /// Get the underlying Godot material for assignment to renderers.
    /// </summary>
    public object GodotMaterial => _usesShaderMaterial
        ? (object)_shaderMaterial
        : (object)_standardMaterial;

    /// <summary>
    /// Renderer queue requested by the owning material provider.
    /// </summary>
    public int RenderQueue => _renderQueue;

    /// <summary>
    /// Whether the material is valid and ready for use.
    /// </summary>
    public bool IsValid => _shaderMaterial != null || _standardMaterial != null;

    /// <summary>
    /// Set the material type/shader.
    /// </summary>
    public void SetMaterialType(MaterialType type)
    {
        _materialType = type;

        // Dispose existing materials
        _standardMaterial?.Dispose();
        _standardMaterial = null!;
        _shaderMaterial?.Dispose();
        _shaderMaterial = null!;

        // Create appropriate material type
        switch (type)
        {
            case MaterialType.PBS_Metallic:
                _usesShaderMaterial = false;
                _standardMaterial = new StandardMaterial3D();
                break;

            case MaterialType.Unlit:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Unlit.gdshader", MaterialType.Unlit);
                break;

            case MaterialType.OverlayUnlit:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Overlay_Unlit.gdshader", MaterialType.OverlayUnlit);
                break;

            case MaterialType.UI_Unlit:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/UI_Unlit.gdshader", MaterialType.UI_Unlit);
                break;

            case MaterialType.UI_DualColor:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/UI_DualColor.gdshader", MaterialType.UI_DualColor);
                break;

            case MaterialType.UI_StencilWrite:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/UI_StencilWrite.gdshader", MaterialType.UI_StencilWrite);
                break;

            case MaterialType.UI_StencilTest:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/UI_UnlitStencil.gdshader", MaterialType.UI_StencilTest);
                break;

            case MaterialType.UI_Text:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/UI_Text.gdshader", MaterialType.UI_Text);
                break;

            case MaterialType.UI_TextStencil:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/UI_TextStencil.gdshader", MaterialType.UI_TextStencil);
                break;

            case MaterialType.Text:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Text_Unlit.gdshader", MaterialType.Text);
                break;

            case MaterialType.Custom:
                _usesShaderMaterial = true;
                _shaderMaterial = new ShaderMaterial();
                // Custom shader will be set via SetCustomShader
                break;

            case MaterialType.Metaball:
                _usesShaderMaterial = true;
                _shaderMaterial = new ShaderMaterial();
                const string metaballShaderPath = "res://Shaders/Metaball.gdshader";
                if (ResourceLoader.Exists(metaballShaderPath))
                    _shaderMaterial.Shader = GD.Load<Shader>(metaballShaderPath);
                else
                    LumoraLogger.Warn($"MaterialAssetHook: Metaball shader not found at {metaballShaderPath}");
                break;

            case MaterialType.GridSpaceGround:
                _usesShaderMaterial = true;
                _shaderMaterial = new ShaderMaterial();
                const string gridGroundShaderPath = "res://Shaders/GridSpaceGround.gdshader";
                if (ResourceLoader.Exists(gridGroundShaderPath))
                    _shaderMaterial.Shader = GD.Load<Shader>(gridGroundShaderPath);
                else
                    LumoraLogger.Warn($"MaterialAssetHook: Grid ground shader not found at {gridGroundShaderPath}");
                break;

            case MaterialType.LocalHomeRising:
                _usesShaderMaterial = true;
                _shaderMaterial = new ShaderMaterial();
                const string localHomeRisingShaderPath = "res://Shaders/LocalHomeRising.gdshader";
                if (ResourceLoader.Exists(localHomeRisingShaderPath))
                    _shaderMaterial.Shader = GD.Load<Shader>(localHomeRisingShaderPath);
                else
                    LumoraLogger.Warn($"MaterialAssetHook: LocalHomeRising shader not found at {localHomeRisingShaderPath}");
                break;

            case MaterialType.Blur:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Blur.gdshader", MaterialType.Blur);
                break;

            default:
                _usesShaderMaterial = false;
                _standardMaterial = new StandardMaterial3D();
                break;
        }

        ApplyRenderPriority(_renderQueue);
    }

    private static ShaderMaterial CreateShaderMaterial(string shaderPath, MaterialType materialType)
    {
        var material = new ShaderMaterial();
        if (ResourceLoader.Exists(shaderPath))
        {
            material.Shader = GD.Load<Shader>(shaderPath);
        }
        else
        {
            LumoraLogger.Warn($"MaterialAssetHook: {materialType} shader not found at {shaderPath}");
        }

        return material;
    }

    /// <summary>
    /// Set a custom shader path (for Custom material type).
    /// </summary>
    public void SetCustomShader(string shaderPath)
    {
        _customShaderPath = NormalizeShaderPath(shaderPath);

        if (_shaderMaterial != null && !string.IsNullOrEmpty(_customShaderPath))
        {
            if (ResourceLoader.Exists(_customShaderPath))
            {
                _shaderMaterial.Shader = GD.Load<Shader>(_customShaderPath);
            }
            else
            {
                LumoraLogger.Warn($"MaterialAssetHook: Custom shader not found: {_customShaderPath}");
            }
        }
    }

    /// <summary>
    /// Set a custom shader from loaded gdshader source text.
    /// </summary>
    public void SetCustomShaderSource(string shaderSource)
    {
        if (string.IsNullOrWhiteSpace(shaderSource))
        {
            return;
        }

        if (_shaderMaterial == null)
        {
            _usesShaderMaterial = true;
            _shaderMaterial = new ShaderMaterial();
        }

        var shader = new Shader();
        shader.Code = shaderSource;
        _shaderMaterial.Shader = shader;
    }

    private static string NormalizeShaderPath(string shaderPath)
    {
        if (string.IsNullOrWhiteSpace(shaderPath))
        {
            return shaderPath;
        }

        const string lumresPrefix = "lumres://";
        if (shaderPath.StartsWith(lumresPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var relative = shaderPath.Substring(lumresPrefix.Length).TrimStart('/');
            return $"res://{relative}";
        }

        return shaderPath;
    }

    /// <summary>
    /// Set blend mode.
    /// </summary>
    public void SetBlendMode(BlendMode mode)
    {
        if (_standardMaterial != null)
        {
            _standardMaterial.Transparency = mode switch
            {
                BlendMode.Alpha => BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode.Transparent => BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode.Cutout => BaseMaterial3D.TransparencyEnum.AlphaScissor,
                BlendMode.Additive => BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode.Multiply => BaseMaterial3D.TransparencyEnum.Alpha,
                _ => BaseMaterial3D.TransparencyEnum.Disabled
            };

            // Set blend mode for additive
            if (mode == BlendMode.Additive)
            {
                _standardMaterial.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
            }
            else if (mode == BlendMode.Multiply)
            {
                _standardMaterial.BlendMode = BaseMaterial3D.BlendModeEnum.Mul;
            }
            else
            {
                _standardMaterial.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
            }

            _standardMaterial.NoDepthTest = false;
        }
        else if (_shaderMaterial != null)
        {
            // Blend mode is a compile-time render_mode in gdshader, NOT a uniform - the parameter
            // below never did anything. For the plain unlit shader, additive swaps the shader.
            if (_materialType == MaterialType.Unlit)
            {
                string shaderPath = mode == BlendMode.Additive
                    ? "res://Shaders/Unlit_Additive.gdshader"
                    : "res://Shaders/Unlit.gdshader";
                var shader = ResourceLoader.Load<Shader>(shaderPath);
                if (shader != null && _shaderMaterial.Shader != shader)
                    _shaderMaterial.Shader = shader; // uniform values persist across the swap
            }
            _shaderMaterial.SetShaderParameter("blend_mode", (int)mode);
        }
    }

    /// <summary>
    /// Set face culling mode.
    /// </summary>
    public void SetCulling(Culling culling)
    {
        if (_standardMaterial != null)
        {
            _standardMaterial.CullMode = culling switch
            {
                Culling.Front => BaseMaterial3D.CullModeEnum.Front,
                Culling.None => BaseMaterial3D.CullModeEnum.Disabled,
                _ => BaseMaterial3D.CullModeEnum.Back
            };
        }
        else if (_shaderMaterial != null)
        {
            _shaderMaterial.SetShaderParameter("cull_mode", (int)culling);
        }
    }

    /// <summary>
    /// Set a float property.
    /// </summary>
    public void SetFloat(string property, float value)
    {
        if (property == "RenderQueue")
        {
            _renderQueue = (int)value;
        }

        _pendingProperties[property] = value;
    }

    /// <summary>
    /// Set an int property.
    /// </summary>
    public void SetInt(string property, int value)
    {
        if (property == "RenderQueue")
        {
            _renderQueue = value;
        }

        // Depth write/test are compile-time render_modes in gdshader, not uniforms - UI materials
        // asking for ZWrite.On (panel backings) or ZTest Always/Disabled (userspace overlays like the
        // dash surface and laser) swap between UI_Unlit shader variants. Uniform values persist.
        if (_materialType == MaterialType.UI_Unlit && _shaderMaterial != null)
        {
            if (property == "ZWrite")
                _uiZWriteOn = value == 2; // ZWrite.On
            else if (property == "ZTest")
                _uiZTestOff = value == 0 || value == 8; // Disabled / Always
            if (property is "ZWrite" or "ZTest")
            {
                string shaderPath = _uiZTestOff
                    ? "res://Shaders/UI_UnlitOverlay.gdshader"
                    : _uiZWriteOn
                        ? "res://Shaders/UI_UnlitZWrite.gdshader"
                        : "res://Shaders/UI_Unlit.gdshader";
                var shader = ResourceLoader.Load<Shader>(shaderPath);
                if (shader != null && _shaderMaterial.Shader != shader)
                    _shaderMaterial.Shader = shader;
            }
        }

        _pendingProperties[property] = value;
    }

    /// <summary>
    /// Set a bool property.
    /// </summary>
    public void SetBool(string property, bool value)
    {
        _pendingProperties[property] = value;
    }

    /// <summary>
    /// Set a color property.
    /// </summary>
    public void SetColor(string property, colorHDR value)
    {
        _pendingProperties[property] = new Color(value.r, value.g, value.b, value.a);
    }

    /// <summary>
    /// Set a float2 property.
    /// </summary>
    public void SetFloat2(string property, float2 value)
    {
        _pendingProperties[property] = new Vector2(value.x, value.y);
    }

    // Record the property (so a later full ApplyChanges stays consistent) AND push just this one to the live
    // material now. SetFloat2 alone only stages into _pendingProperties - the shader param isn't touched until
    // ApplyChanges, which re-pushes ALL properties. This flushes one, for per-frame scroll clip_offset. -xlinka
    public void ApplyFloat2Now(string property, float2 value)
    {
        var v = new Vector2(value.x, value.y);
        _pendingProperties[property] = v;
        ApplyProperty(property, v);
    }

    /// <summary>
    /// Set a float3 property.
    /// </summary>
    public void SetFloat3(string property, float3 value)
    {
        _pendingProperties[property] = new Vector3(value.x, value.y, value.z);
    }

    /// <summary>
    /// Set a float4 property.
    /// </summary>
    public void SetFloat4(string property, float4 value)
    {
        _pendingProperties[property] = new Vector4(value.x, value.y, value.z, value.w);
    }

    /// <summary>
    /// Set a texture property.
    /// </summary>
    public void SetTexture(string property, TextureAsset texture)
    {
        if (texture?.Hook is IGodotTexture textureHook && textureHook.IsValid)
        {
            _pendingProperties[property] = textureHook.GodotTexture2D!;
        }
        else
        {
            _pendingProperties[property] = null!;
        }
    }

    /// <summary>
    /// Clear all pending properties.
    /// </summary>
    public void Clear()
    {
        _pendingProperties.Clear();
        ApplyRenderPriority(-1);
    }

    /// <summary>
    /// Apply all pending changes.
    /// </summary>
    public void ApplyChanges(Action callback)
    {
        LumoraLogger.Debug($"MaterialAssetHook.ApplyChanges: Applying {_pendingProperties.Count} properties, usesShader={_usesShaderMaterial}");
        foreach (var (property, value) in _pendingProperties)
        {
            ApplyProperty(property, value);
        }

        callback?.Invoke();
    }

    /// <summary>
    /// Apply a single property to the material.
    /// </summary>
    private void ApplyProperty(string property, object value)
    {
        MaterialPropertyApplicator.Apply(GodotMaterial as Material, _materialType, property, value);
    }

    /// <summary>
    /// Apply property to StandardMaterial3D.
    /// </summary>
    private void ApplyStandardMaterialProperty(string property, object value)
    {
        switch (property)
        {
            // Albedo
            case "TintColor":
            case "AlbedoColor":
                if (value is Color c) _standardMaterial.AlbedoColor = c;
                break;
            case "Texture":
            case "AlbedoTexture":
                _standardMaterial.AlbedoTexture = value as Texture2D;
                break;

            // Metallic/Roughness
            case "Metallic":
                if (value is float m) _standardMaterial.Metallic = m;
                break;
            case "Smoothness":
                // Convert smoothness to roughness (inverse)
                if (value is float s) _standardMaterial.Roughness = 1.0f - s;
                break;
            case "MetallicMap":
                if (value is Texture2D metallicTex)
                {
                    _standardMaterial.MetallicTexture = metallicTex;
                    _standardMaterial.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red;
                }
                else
                {
                    _standardMaterial.MetallicTexture = null;
                }
                break;

            // Normal
            case "NormalMap":
                _standardMaterial.NormalEnabled = value != null;
                _standardMaterial.NormalTexture = value as Texture2D;
                break;
            case "NormalScale":
                if (value is float ns) _standardMaterial.NormalScale = ns;
                break;

            // Emission
            case "EmissiveColor":
                if (value is Color ec)
                {
                    bool hasEmission = ec.R > 0 || ec.G > 0 || ec.B > 0;
                    _standardMaterial.EmissionEnabled = hasEmission;
                    if (hasEmission)
                    {
                        _standardMaterial.Emission = ec;
                        _standardMaterial.EmissionEnergyMultiplier = 1.0f;
                    }
                }
                break;
            case "EmissiveMap":
                if (value is Texture2D emissiveTex)
                {
                    _standardMaterial.EmissionEnabled = true;
                    _standardMaterial.EmissionTexture = emissiveTex;
                }
                else if (_standardMaterial.Emission == new Color(0, 0, 0))
                {
                    _standardMaterial.EmissionEnabled = false;
                }
                break;

            // Occlusion
            case "OcclusionMap":
                _standardMaterial.AOEnabled = value != null;
                _standardMaterial.AOTexture = value as Texture2D;
                break;

            // Alpha
            case "AlphaCutoff":
                if (value is float ac) _standardMaterial.AlphaScissorThreshold = ac;
                break;
            case "RenderQueue":
                ApplyRenderPriority(value);
                break;

            // Texture transform
            case "TextureScale":
                if (value is Vector2 scale)
                {
                    _standardMaterial.Uv1Scale = new Vector3(scale.X, scale.Y, 1);
                }
                break;
            case "TextureOffset":
                if (value is Vector2 offset)
                {
                    _standardMaterial.Uv1Offset = new Vector3(offset.X, offset.Y, 0);
                }
                break;
        }
    }

    /// <summary>
    /// Apply property to ShaderMaterial.
    /// </summary>
    private void ApplyShaderMaterialProperty(string property, object value)
    {
        if (_shaderMaterial == null)
            return;

        if (property == "RenderQueue")
        {
            ApplyRenderPriority(value);
            return;
        }

        // Convert property name to shader parameter name (snake_case)
        string shaderParam = ToSnakeCase(property);

        // Map common property names to shader uniform names. The UI/text
        // shaders share the Unlit albedo_* uniform family but keep their own
        // alpha_cutoff (Unlit uses alpha_scissor_threshold).
        bool isUnlit = _materialType == MaterialType.Unlit;
        bool isAlbedoShader = isUnlit || _materialType is MaterialType.UI_Unlit or MaterialType.UI_DualColor or MaterialType.UI_Text or MaterialType.Text;
        bool isMetaball = _materialType == MaterialType.Metaball;
        string mappedParam = property switch
        {
            "TintColor" => isAlbedoShader ? "albedo_color" : "tint_color",
            "Texture" => isAlbedoShader ? "albedo_texture" : "main_texture",
            "AlbedoColor" => "albedo_color",
            "AlbedoTexture" => "albedo_texture",
            "TextureScale" => isAlbedoShader ? "uv_scale" : "texture_scale",
            "TextureOffset" => isAlbedoShader ? "uv_offset" : "texture_offset",
            "AlphaCutoff" => isUnlit ? "alpha_scissor_threshold" : "alpha_cutoff",
            // Metaball uniforms. explicit map so refactors of the C# field names don't break the shader binding - xlinka
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
            _ => shaderParam
        };

        if (value == null)
        {
            if (property == "Texture" && isAlbedoShader)
            {
                _shaderMaterial.SetShaderParameter("use_albedo_texture", false);
            }
            return;
        }

        // Convert value to appropriate Godot Variant based on type
        Variant variantValue = value switch
        {
            float f => Variant.From(f),
            double d => Variant.From((float)d),
            int i => Variant.From(i),
            bool b => Variant.From(b),
            string s => Variant.From(s),
            Vector2 v2 => Variant.From(v2),
            Vector3 v3 => Variant.From(v3),
            Vector4 v4 => Variant.From(v4),
            Color c => Variant.From(c),
            Texture2D tex => Variant.From(tex),
            Lumora.Core.Math.float2 f2 => Variant.From(new Vector2(f2.x, f2.y)),
            Lumora.Core.Math.float3 f3 => Variant.From(new Vector3(f3.x, f3.y, f3.z)),
            Lumora.Core.Math.colorHDR hdr => Variant.From(new Color(hdr.r, hdr.g, hdr.b, hdr.a)),
            _ => default
        };

        if (variantValue.VariantType != Variant.Type.Nil)
        {
            LumoraLogger.Debug($"MaterialAssetHook.ApplyShaderMaterialProperty: Setting {mappedParam} = {value?.GetType().Name}");
            _shaderMaterial.SetShaderParameter(mappedParam, variantValue);
            if (property == "Texture" && isAlbedoShader)
            {
                _shaderMaterial.SetShaderParameter("use_albedo_texture", value is Texture2D);
            }
        }
        else
        {
            LumoraLogger.Debug($"MaterialAssetHook.ApplyShaderMaterialProperty: Skipping {mappedParam}, value is nil");
        }
    }

    /// <summary>
    /// Convert PascalCase to snake_case.
    /// </summary>
    private static string ToSnakeCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

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

    private void ApplyRenderPriority(object value)
    {
        switch (value)
        {
            case float rqf:
                ApplyRenderPriority((int)rqf);
                break;
            case int rqi:
                ApplyRenderPriority(rqi);
                break;
        }
    }

    private void ApplyRenderPriority(int renderQueue)
    {
        _renderQueue = renderQueue;
        MaterialPropertyApplicator.ApplyRenderPriority(_standardMaterial, renderQueue);
        MaterialPropertyApplicator.ApplyRenderPriority(_shaderMaterial, renderQueue);
    }

    private static int NormalizeRenderQueue(int renderQueue)
    {
        return renderQueue < 0 ? 0 : System.Math.Clamp(renderQueue, -128, 127);
    }

    /// <summary>
    /// Unload and dispose the Godot material.
    /// </summary>
    public override void Unload()
    {
        _standardMaterial?.Dispose();
        _standardMaterial = null!;

        _shaderMaterial?.Dispose();
        _shaderMaterial = null!;

        _pendingProperties.Clear();
    }
}

