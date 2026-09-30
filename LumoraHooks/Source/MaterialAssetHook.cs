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

[ImplementableHook(typeof(MaterialAsset))]
public class MaterialAssetHook : AssetHook, IMaterialAssetHook
{
    private StandardMaterial3D _standardMaterial = null!;
    private ShaderMaterial _shaderMaterial = null!;
    // Second material chained onto the first. Godot 4 shaders have no multi-pass, so an effect that
    // genuinely needs two passes (the toon outline's inverted hull) gets one here. -xlinka
    private ShaderMaterial _nextPassMaterial = null!;
    private MaterialType _materialType;
    private bool _uiZWriteOn;
    private bool _uiZTestOff;
    private bool _usesShaderMaterial;

    // The metallic family ships one shader per (blend, cull) pair because both are compile-time
    // render_modes. The two arrive through separate setters, so whichever fires second has to know what
    // the first one chose or it would swap back to the default for the other axis. -xlinka
    private Culling _culling = Culling.Back;
    private BlendMode _blendMode = BlendMode.Opaque;
    private string _customShaderPath = null!;
    // The custom source the shader material wears now; null when it wears none, or a built-in one.
    private string? _customSource;
    private int _renderQueue = -1;

    // Pending properties to apply
    private readonly Dictionary<string, object> _pendingProperties = new();

    public object GodotMaterial => _usesShaderMaterial
        ? (object)_shaderMaterial
        : (object)_standardMaterial;

    public int RenderQueue => _renderQueue;

    public bool IsValid => _shaderMaterial != null || _standardMaterial != null;

    public void SetMaterialType(MaterialType type)
    {
        _materialType = type;
        _customSource = null;

        _standardMaterial?.Dispose();
        _standardMaterial = null!;
        _shaderMaterial?.Dispose();
        _shaderMaterial = null!;
        _nextPassMaterial?.Dispose();
        _nextPassMaterial = null!;

        switch (type)
        {
            case MaterialType.Fresnel:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial(FresnelShaderPath(_culling, _blendMode), MaterialType.Fresnel);
                break;

            case MaterialType.Reflection:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial(ReflectionShaderPath(_culling, _blendMode), MaterialType.Reflection);
                break;

            case MaterialType.PBS_Metallic:
                // WAS the one PBS material on Godot's StandardMaterial3D while PBS_Specular, DualSided,
                // Triplanar and VertexColor all ran real shaders. That built-in covers most of the
                // metallic workflow, which is why it lasted, but it does not shade the way PBS_Metallic
                // content expects, and the two drift wherever Godot made a different call - the detail
                // albedo being a straight multiply rather than a 2x one, and the metallic map's
                // smoothness being read out of a different channel. -xlinka
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial(MetallicShaderPath(_culling, _blendMode), MaterialType.PBS_Metallic);
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

            case MaterialType.UI_ColorGradient:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/UI_ColorGradient.gdshader", MaterialType.UI_ColorGradient);
                break;

            case MaterialType.Wireframe:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_Wireframe.gdshader", MaterialType.Wireframe);
                break;

            case MaterialType.Matcap:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_Matcap.gdshader", MaterialType.Matcap);
                break;

            case MaterialType.FlatToon:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_FlatToon.gdshader", MaterialType.FlatToon);
                // The outline hull is a separate material chained after the lit pass. It discards
                // itself at width 0, so it costs a degenerate draw and nothing else when unused.
                _nextPassMaterial = CreateShaderMaterial("res://Shaders/Mat_FlatToonOutline.gdshader", MaterialType.FlatToon);
                _shaderMaterial.NextPass = _nextPassMaterial;
                break;

            case MaterialType.PBS_Triplanar:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_PBS_Triplanar.gdshader", MaterialType.PBS_Triplanar);
                break;

            case MaterialType.PBS_DualSided:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_PBS_DualSided.gdshader", MaterialType.PBS_DualSided);
                break;

            case MaterialType.PBS_VertexColor:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_PBS_VertexColor.gdshader", MaterialType.PBS_VertexColor);
                break;

            case MaterialType.PBS_ColorMask:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial(ColorMaskShaderPath(_blendMode), MaterialType.PBS_ColorMask);
                break;

            case MaterialType.ChannelMatrix:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_ChannelMatrix.gdshader", MaterialType.ChannelMatrix);
                break;

            case MaterialType.FresnelLerp:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_FresnelLerp.gdshader", MaterialType.FresnelLerp);
                break;

            case MaterialType.OverlayFresnel:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_OverlayFresnel.gdshader", MaterialType.OverlayFresnel);
                break;

            case MaterialType.Portal:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial("res://Shaders/Mat_Portal.gdshader", MaterialType.Portal);
                break;

            case MaterialType.Toon:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial(ToonShaderPath(Culling.Back), MaterialType.Toon);
                _nextPassMaterial = CreateShaderMaterial("res://Shaders/Mat_ToonOutline.gdshader", MaterialType.Toon);
                _shaderMaterial.NextPass = _nextPassMaterial;
                break;

            case MaterialType.PBS_Specular:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial(SpecularShaderPath(Culling.Back), MaterialType.PBS_Specular);
                break;

            case MaterialType.XiexeToon:
                _usesShaderMaterial = true;
                _shaderMaterial = CreateShaderMaterial(XiexeShaderPath(Culling.Back), MaterialType.XiexeToon);
                _nextPassMaterial = CreateShaderMaterial("res://Shaders/Mat_XiexeOutline.gdshader", MaterialType.XiexeToon);
                _shaderMaterial.NextPass = _nextPassMaterial;
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

    public void SetCustomShader(string shaderPath)
    {
        _customShaderPath = NormalizeShaderPath(shaderPath);

        if (_shaderMaterial != null && !string.IsNullOrEmpty(_customShaderPath))
        {
            if (ResourceLoader.Exists(_customShaderPath))
            {
                _shaderMaterial.Shader = GD.Load<Shader>(_customShaderPath);
                _customSource = null;
            }
            else
            {
                LumoraLogger.Warn($"MaterialAssetHook: Custom shader not found: {_customShaderPath}");
            }
        }
    }

    public void SetCustomShaderSource(string shaderSource)
    {
        if (string.IsNullOrWhiteSpace(shaderSource))
        {
            // Empty is the caller's "clear" signal (e.g. a sandbox-rejected update), not "leave the
            // last compile alone" - a rejected shader must never keep rendering a stale previous
            // compile. Drop back to the un-shaded state a fresh Custom material starts in. -xlinka
            _customSource = null;
            if (_shaderMaterial != null)
            {
                _shaderMaterial.Shader = null;
            }
            return;
        }

        if (_shaderMaterial == null)
        {
            _usesShaderMaterial = true;
            _shaderMaterial = new ShaderMaterial();
        }

        // Every material update hands the source over again, a slider tweak included. The same text
        // keeps the shader it already has; a new one comes from the cache, compiled once however many
        // materials wear it. -xlinka
        if (_customSource == shaderSource && _shaderMaterial.Shader != null)
            return;
        _customSource = shaderSource;
        _shaderMaterial.Shader = CustomShaderCache.Get(shaderSource);
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

    public void SetBlendMode(BlendMode mode)
    {
        _blendMode = mode;

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
            // Moving PBS_Metallic off StandardMaterial3D took its additive and multiply blending with
            // it: that material honoured BlendMode through properties, a shader honours it through a
            // compile-time render_mode. Without this an additive surface renders as an ordinary mix
            // layer, so a subtle sheen becomes a solid panel. -xlinka
            if (_materialType == MaterialType.PBS_Metallic)
            {
                SwapShader(MetallicShaderPath(_culling, mode));
            }

            if (_materialType == MaterialType.Fresnel)
            {
                SwapShader(FresnelShaderPath(_culling, mode));
            }

            if (_materialType == MaterialType.Reflection)
            {
                SwapShader(ReflectionShaderPath(_culling, mode));
            }

            // The colour-mask material has no Culling member, so its only axis is the pass split.
            if (_materialType == MaterialType.PBS_ColorMask)
            {
                SwapShader(ColorMaskShaderPath(mode));
            }

            // A custom material's blend reaches it as a render_mode written into its source before it gets
            // here. A blend_mode uniform would do nothing there, or overwrite one the shader declared itself.
            if (_materialType != MaterialType.Custom)
                _shaderMaterial.SetShaderParameter("blend_mode", (int)mode);
        }
    }

    public void SetCulling(Culling culling)
    {
        _culling = culling;

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
            // Culling is a compile-time render_mode in gdshader, not a uniform. The toon family ships one
            // variant per cull mode over a shared include, so it swaps shaders the way the UI unlit family
            // does for ZTest; everything else keeps setting the parameter it has always set.
            if (_materialType == MaterialType.Toon)
            {
                SwapShader(ToonShaderPath(culling));
                return;
            }

            if (_materialType == MaterialType.XiexeToon)
            {
                SwapShader(XiexeShaderPath(culling));
                return;
            }

            if (_materialType == MaterialType.PBS_Specular)
            {
                SwapShader(SpecularShaderPath(culling));
                return;
            }

            if (_materialType == MaterialType.PBS_Metallic)
            {
                SwapShader(MetallicShaderPath(culling, _blendMode));
                return;
            }

            if (_materialType == MaterialType.Fresnel)
            {
                SwapShader(FresnelShaderPath(culling, _blendMode));
                return;
            }

            if (_materialType == MaterialType.Reflection)
            {
                SwapShader(ReflectionShaderPath(culling, _blendMode));
                return;
            }

            // Same as the blend: a custom material's culling is written into its source.
            if (_materialType != MaterialType.Custom)
                _shaderMaterial.SetShaderParameter("cull_mode", (int)culling);
        }
    }

    private static string ColorMaskShaderPath(BlendMode blend)
        => blend is BlendMode.Alpha or BlendMode.Transparent
            ? "res://Shaders/Mat_PBS_ColorMaskAlpha.gdshader"
            : "res://Shaders/Mat_PBS_ColorMask.gdshader";

    private static string ReflectionShaderPath(Culling culling, BlendMode blend)
        => $"res://Shaders/Mat_Reflection{BlendVariant(blend)}{CullVariant(culling)}.gdshader";

    private static string FresnelShaderPath(Culling culling, BlendMode blend)
        => $"res://Shaders/Mat_Fresnel{BlendVariant(blend)}{CullVariant(culling)}.gdshader";

    // FOUR VARIANTS, NOT THREE. Opaque and Cutout share the empty one, which writes no ALPHA and so
    // stays in Godot's OPAQUE pass - that is the whole point of splitting them off Alpha rather than
    // letting the blend_mode uniform sort it out at runtime. A material that writes ALPHA is transparent
    // no matter what it writes, gets no depth write, and is sorted per object, which reads as surfaces
    // flickering in and out as the camera moves. -xlinka
    private static string BlendVariant(BlendMode blend) => blend switch
    {
        BlendMode.Additive => "Add",
        BlendMode.Multiply => "Mul",
        BlendMode.Alpha or BlendMode.Transparent => "Alpha",
        _ => string.Empty
    };

    private static string CullVariant(Culling culling) => culling switch
    {
        Culling.Front => "FrontCull",
        Culling.None => "DoubleSided",
        _ => string.Empty
    };

    private static string MetallicShaderPath(Culling culling, BlendMode blend)
        => $"res://Shaders/Mat_PBS_Metallic{BlendVariant(blend)}{CullVariant(culling)}.gdshader";

    private static string SpecularShaderPath(Culling culling) => culling switch
    {
        Culling.Front => "res://Shaders/Mat_PBS_SpecularFrontCull.gdshader",
        Culling.None => "res://Shaders/Mat_PBS_SpecularDoubleSided.gdshader",
        _ => "res://Shaders/Mat_PBS_Specular.gdshader"
    };

    private static string XiexeShaderPath(Culling culling) => culling switch
    {
        Culling.Front => "res://Shaders/Mat_XiexeFrontCull.gdshader",
        Culling.None => "res://Shaders/Mat_XiexeDoubleSided.gdshader",
        _ => "res://Shaders/Mat_Xiexe.gdshader"
    };

    private static string ToonShaderPath(Culling culling) => culling switch
    {
        Culling.Front => "res://Shaders/Mat_ToonFrontCull.gdshader",
        Culling.None => "res://Shaders/Mat_ToonDoubleSided.gdshader",
        _ => "res://Shaders/Mat_Toon.gdshader"
    };

    public void SetFloat(string property, float value)
    {
        if (property == "RenderQueue")
        {
            _renderQueue = (int)value;
        }

        _pendingProperties[property] = value;
    }

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

        // Same variant swap for UI TEXT: overlay canvases (context menu) need their glyphs to skip the depth
        // test too, or the labels get cut by world geometry while the arcs draw on top of it. -xlinka
        if (_materialType == MaterialType.UI_Text && _shaderMaterial != null && property == "ZTest")
        {
            _uiZTestOff = value == 0 || value == 8; // Disabled / Always
            string shaderPath = _uiZTestOff
                ? "res://Shaders/UI_TextOverlay.gdshader"
                : "res://Shaders/UI_Text.gdshader";
            var shader = ResourceLoader.Load<Shader>(shaderPath);
            if (shader != null && _shaderMaterial.Shader != shader)
                _shaderMaterial.Shader = shader;
        }

        _pendingProperties[property] = value;
    }

    public void SetBool(string property, bool value)
    {
        // Both of these are compile-time render_modes in gdshader, not uniforms, so they swap the
        // shader instead of setting a parameter. Uniform values live on the material and survive the
        // swap, same as the UI unlit family's ZTest variants. -xlinka
        if (_shaderMaterial != null)
        {
            if (_materialType == MaterialType.Wireframe && property == "DrawOverDepth")
            {
                SwapShader(value
                    ? "res://Shaders/Mat_WireframeOverlay.gdshader"
                    : "res://Shaders/Mat_Wireframe.gdshader");
                return;
            }

            if (_materialType == MaterialType.PBS_VertexColor && property == "UseVertexAlpha")
            {
                SwapShader(value
                    ? "res://Shaders/Mat_PBS_VertexColorTransparent.gdshader"
                    : "res://Shaders/Mat_PBS_VertexColor.gdshader");
                return;
            }
        }

        _pendingProperties[property] = value;
    }

    private void SwapShader(string shaderPath)
    {
        var shader = ResourceLoader.Load<Shader>(shaderPath);
        if (shader != null && _shaderMaterial.Shader != shader)
        {
            _shaderMaterial.Shader = shader;
        }
    }

    public void SetColor(string property, colorHDR value)
    {
        _pendingProperties[property] = new Color(value.r, value.g, value.b, value.a);
    }

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

    public void SetFloat3(string property, float3 value)
    {
        _pendingProperties[property] = new Vector3(value.x, value.y, value.z);
    }

    public void SetFloat4(string property, float4 value)
    {
        _pendingProperties[property] = new Vector4(value.x, value.y, value.z, value.w);
    }

    // A TEXTURE THAT IS ASSIGNED BUT NOT YET ON THE GPU IS NOT THE SAME AS NO TEXTURE.
    //
    // An asset reaching FullyLoaded is what re-drives the material, but the hook's upload to the GPU
    // is deferred to the main thread and can land AFTER that. The material then applies, finds
    // IsValid false, writes null, and no further notification is coming - so an imported prop kept
    // its material and lost its albedo for the rest of the session. Textures already in the GPU cache
    // from an earlier run are valid immediately, which is exactly why some things were textured and
    // others were not.
    //
    // Remember that we wrote a null over a real assignment, so the material can come back for it.
    // -xlinka
    private readonly HashSet<string> _texturesNotReady = new();

    public bool HasTexturesStillArriving => _texturesNotReady.Count > 0;

    public string TexturesStillArriving => string.Join(",", _texturesNotReady);

    public void SetTexture(string property, TextureAsset texture)
    {
        if (texture?.Hook is IGodotTexture textureHook && textureHook.IsValid)
        {
            _pendingProperties[property] = textureHook.GodotTexture2D!;
            _texturesNotReady.Remove(property);
        }
        else
        {
            _pendingProperties[property] = null!;
            if (texture != null)
                _texturesNotReady.Add(property);
            else
                _texturesNotReady.Remove(property);
        }

        // A texture that carries a second picture for the right eye binds it beside the first, under
        // <Property>Right, which the name mapping turns into <property>_right. Everything else in the
        // engine only ever reads the left one and is none the wiser. -xlinka
        var rightEye = texture?.Hook is IStereoGodotTexture stereo ? stereo.RightEyeTexture2D : null;
        _pendingProperties[property + "Right"] = rightEye!;

        // A canvas material bound to a frame strip plays it; bound to anything else it shows a still.
        if (_materialType == MaterialType.UI_Unlit && property == "Texture")
        {
            int frames = texture != null && texture.AnimationFrames > 1 ? texture.AnimationFrames : 1;
            _pendingProperties["FrameCount"] = frames;
            _pendingProperties["FrameRate"] = texture != null && frames > 1 && texture.AnimationFps > 0f ? texture.AnimationFps : 10f;
        }
    }

    public void Clear()
    {
        _pendingProperties.Clear();
        ApplyRenderPriority(-1);
    }

    public void ApplyChanges(Action callback)
    {
        LumoraLogger.Debug($"MaterialAssetHook.ApplyChanges: Applying {_pendingProperties.Count} properties, usesShader={_usesShaderMaterial}");
        foreach (var (property, value) in _pendingProperties)
        {
            ApplyProperty(property, value);
        }

        callback?.Invoke();
    }

    private void ApplyProperty(string property, object value)
    {
        // Outline controls belong to the chained hull material, not the lit surface - the base shader
        // has no such uniforms and would swallow the write.
        if (_nextPassMaterial != null
            && property is "OutlineWidth" or "OutlineColor" or "OutlineMask" or "UseOutlineMask")
        {
            MaterialPropertyApplicator.Apply(_nextPassMaterial, _materialType, property, value);
            return;
        }

        MaterialPropertyApplicator.Apply(GodotMaterial as Material, _materialType, property, value);
    }

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

            // Detail. StandardMaterial3D keeps one UV set for the detail pair and takes its tiling
            // from UV2, so the detail scale/offset go on UV2Scale/UV2Offset rather than a second
            // transform of their own.
            case "DetailAlbedoTexture":
                _standardMaterial.DetailEnabled = value != null || _standardMaterial.DetailNormal != null;
                _standardMaterial.DetailAlbedo = value as Texture2D;
                if (value != null)
                {
                    _standardMaterial.DetailUVLayer = BaseMaterial3D.DetailUV.UV2;
                    // Multiply is what a detail albedo means: darken and tint the base, never replace it.
                    _standardMaterial.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul;
                }
                break;
            case "DetailNormalMap":
                _standardMaterial.DetailEnabled = value != null || _standardMaterial.DetailAlbedo != null;
                _standardMaterial.DetailNormal = value as Texture2D;
                if (value != null)
                    _standardMaterial.DetailUVLayer = BaseMaterial3D.DetailUV.UV2;
                break;
            // UV2Scale, not Uv2Scale. The binding generator capitalised the UV1 pair and the UV2 pair
            // differently in this build, so the obvious spelling does not compile.
            case "DetailTextureScale":
                if (value is Vector2 dts) _standardMaterial.UV2Scale = new Vector3(dts.X, dts.Y, 1);
                break;
            case "DetailTextureOffset":
                if (value is Vector2 dto) _standardMaterial.UV2Offset = new Vector3(dto.X, dto.Y, 0);
                break;
            case "DetailNormalScale":
                // No separate strength for the detail normal in StandardMaterial3D; the one normal
                // scale covers both, so a detail-only material still gets its depth.
                if (value is float dns && _standardMaterial.DetailNormal != null)
                    _standardMaterial.NormalScale = dns;
                break;

            // Parallax
            case "HeightMap":
                _standardMaterial.HeightmapEnabled = value != null;
                _standardMaterial.HeightmapTexture = value as Texture2D;
                break;
            case "HeightScale":
                if (value is float hs) _standardMaterial.HeightmapScale = hs;
                break;

            // Depth bias. Godot exposes only a single depth-draw offset rather than the slope-scaled
            // factor/units pair, so the two are folded: anything non-zero turns on the bias and the
            // sign decides which way it goes.
            case "OffsetFactor":
            case "OffsetUnits":
                if (value is float off && off != 0f)
                    _standardMaterial.RenderPriority = System.Math.Clamp(
                        _standardMaterial.RenderPriority + (off < 0f ? 1 : -1), -128, 127);
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

    private void ApplyShaderMaterialProperty(string property, object value)
    {
        if (_shaderMaterial == null)
            return;

        if (property == "RenderQueue")
        {
            ApplyRenderPriority(value);
            return;
        }

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
            // A texture member that went null has to say so, or the shader keeps sampling the last
            // one it was given.
            if (IsClearableTextureMember(property))
                _shaderMaterial.SetShaderParameter("use_" + mappedParam, false);
            return;
        }

        // EVERY TEXTURE UNIFORM GETS ITS COMPANION FLAG, not just albedo.
        //
        // Our shader materials gate each optional map on a `use_<name>` bool, and until now the only
        // one anything ever set was use_albedo_texture, and then only for a member literally called
        // "Texture". So use_normal_map and use_metallic_map sat at their `false` default forever:
        // every normal map and every metallic map on every shader-based material in the engine was
        // bound to the sampler and then never read. Setting a uniform a shader does not declare is
        // harmless, so this needs no per-shader list.
        //
        // Driven off the VALUE being a texture rather than off the member's name, because the names
        // collide: ColorMask is the mask TEXTURE on the four-way tint material and a channel WRITE
        // MASK enum on the channel-matrix filter. Only the null case has to fall back to the name,
        // and there the name test is narrow enough to keep them apart. -xlinka
        if (value is Texture2D)
            _shaderMaterial.SetShaderParameter("use_" + mappedParam, true);

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

    // Named by what it is rather than by its type, because the value arrives as a bare object and is
    // null exactly when we most need to know it was a texture.
    private static bool IsTextureMember(string property)
        => property.EndsWith("Texture", StringComparison.Ordinal)
        || property.EndsWith("Map", StringComparison.Ordinal)
        || (property.EndsWith("Right", StringComparison.Ordinal) && IsTextureMember(property[..^5]))
        || property == "ColorMask";

    // Only reached when the value is null, so a name test is all there is to go on.
    private static bool IsClearableTextureMember(string property) => IsTextureMember(property);

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

    public override void Unload()
    {
        _standardMaterial?.Dispose();
        _standardMaterial = null!;

        _shaderMaterial?.Dispose();
        _shaderMaterial = null!;

        _nextPassMaterial?.Dispose();
        _nextPassMaterial = null!;

        _customSource = null;
        _pendingProperties.Clear();
    }
}

