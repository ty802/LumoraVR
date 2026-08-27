// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Shaders;

public class PBSShader
{
    private static Shader _shaderResource = null!;
    private ShaderMaterial _material = null!;

    private const string SHADER_PATH = "res://Shaders/PBS.gdshader";

    public PBSShader()
    {
        LoadShader();
        CreateMaterial();
    }

    private static void LoadShader()
    {
        if (_shaderResource != null)
            return;

        if (!ResourceLoader.Exists(SHADER_PATH))
        {
            LumoraLogger.Error($"PBSShader: Shader file not found at {SHADER_PATH}");
            return;
        }

        _shaderResource = GD.Load<Shader>(SHADER_PATH);

        if (_shaderResource == null)
        {
            LumoraLogger.Error($"PBSShader: Failed to load shader from {SHADER_PATH}");
        }
        else
        {
            LumoraLogger.Debug($"PBSShader: Loaded shader from {SHADER_PATH}");
        }
    }

    private void CreateMaterial()
    {
        if (_shaderResource == null)
        {
            LumoraLogger.Error("PBSShader: Cannot create material - shader not loaded");
            return;
        }

        _material = new ShaderMaterial();
        _material.Shader = _shaderResource;

        SetAlbedoColor(new Color(1.0f, 1.0f, 1.0f, 1.0f));
        SetMetallic(0.0f);
        SetSmoothness(0.5f);
        SetEmissionColor(new Color(0.0f, 0.0f, 0.0f, 1.0f));
        SetEmissionEnergy(1.0f);

        LumoraLogger.Debug("PBSShader: Created material with default properties");
    }

    public ShaderMaterial GetMaterial()
    {
        return _material;
    }

    public void SetAlbedoColor(Color color)
    {
        _material?.SetShaderParameter("albedo_color", color);
    }

    public Color GetAlbedoColor()
    {
        return _material?.GetShaderParameter("albedo_color").AsColor() ?? Colors.White;
    }

    public void SetAlbedoTexture(Texture2D texture)
    {
        _material?.SetShaderParameter("albedo_texture", texture);
        _material?.SetShaderParameter("use_albedo_texture", texture != null);
    }

    public Texture2D GetAlbedoTexture()
    {
        return _material?.GetShaderParameter("albedo_texture").As<Texture2D>()!;
    }

    public void SetMetallic(float metallic)
    {
        _material?.SetShaderParameter("metallic", Mathf.Clamp(metallic, 0.0f, 1.0f));
    }

    public float GetMetallic()
    {
        return _material?.GetShaderParameter("metallic").AsSingle() ?? 0.0f;
    }

    public void SetMetallicTexture(Texture2D texture, bool useAlphaForSmoothness = false)
    {
        _material?.SetShaderParameter("metallic_texture", texture);
        _material?.SetShaderParameter("use_metallic_texture", texture != null);
        _material?.SetShaderParameter("use_metallic_texture_smoothness", useAlphaForSmoothness);
    }

    public Texture2D GetMetallicTexture()
    {
        return _material?.GetShaderParameter("metallic_texture").As<Texture2D>()!;
    }

    public void SetSmoothness(float smoothness)
    {
        _material?.SetShaderParameter("smoothness", Mathf.Clamp(smoothness, 0.0f, 1.0f));
    }

    public float GetSmoothness()
    {
        return _material?.GetShaderParameter("smoothness").AsSingle() ?? 0.5f;
    }

    public void SetRoughness(float roughness)
    {
        SetSmoothness(1.0f - Mathf.Clamp(roughness, 0.0f, 1.0f));
    }

    public float GetRoughness()
    {
        return 1.0f - GetSmoothness();
    }

    public void SetNormalMap(Texture2D texture, float scale = 1.0f)
    {
        _material?.SetShaderParameter("normal_texture", texture);
        _material?.SetShaderParameter("use_normal_map", texture != null);
        _material?.SetShaderParameter("normal_scale", scale);
    }

    public Texture2D GetNormalMap()
    {
        return _material?.GetShaderParameter("normal_texture").As<Texture2D>()!;
    }

    public void SetNormalScale(float scale)
    {
        _material?.SetShaderParameter("normal_scale", scale);
    }

    public float GetNormalScale()
    {
        return _material?.GetShaderParameter("normal_scale").AsSingle() ?? 1.0f;
    }

    public void SetEmissionColor(Color color)
    {
        _material?.SetShaderParameter("emission_color", color);
    }

    public Color GetEmissionColor()
    {
        return _material?.GetShaderParameter("emission_color").AsColor() ?? Colors.Black;
    }

    public void SetEmissionTexture(Texture2D texture)
    {
        _material?.SetShaderParameter("emission_texture", texture);
        _material?.SetShaderParameter("use_emission_texture", texture != null);
    }

    public Texture2D GetEmissionTexture()
    {
        return _material?.GetShaderParameter("emission_texture").As<Texture2D>()!;
    }

    public void SetEmissionEnergy(float energy)
    {
        _material?.SetShaderParameter("emission_energy", Mathf.Max(0.0f, energy));
    }

    public float GetEmissionEnergy()
    {
        return _material?.GetShaderParameter("emission_energy").AsSingle() ?? 1.0f;
    }

    public void SetOcclusionMap(Texture2D texture, float lightAffect = 0.0f)
    {
        _material?.SetShaderParameter("ao_texture", texture);
        _material?.SetShaderParameter("use_ao_texture", texture != null);
        _material?.SetShaderParameter("ao_light_affect", Mathf.Clamp(lightAffect, 0.0f, 1.0f));
    }

    public Texture2D GetOcclusionMap()
    {
        return _material?.GetShaderParameter("ao_texture").As<Texture2D>()!;
    }

    public void SetAlphaScissor(float threshold, bool enable = true)
    {
        _material?.SetShaderParameter("alpha_scissor_threshold", Mathf.Clamp(threshold, 0.0f, 1.0f));
        _material?.SetShaderParameter("use_alpha_scissor", enable);
    }

    public float GetAlphaScissor()
    {
        return _material?.GetShaderParameter("alpha_scissor_threshold").AsSingle() ?? 0.5f;
    }

    public void SetUVTransform(Vector2 scale, Vector2 offset)
    {
        _material?.SetShaderParameter("uv_scale", scale);
        _material?.SetShaderParameter("uv_offset", offset);
    }

    public Vector2 GetUVScale()
    {
        return _material?.GetShaderParameter("uv_scale").AsVector2() ?? Vector2.One;
    }

    public Vector2 GetUVOffset()
    {
        return _material?.GetShaderParameter("uv_offset").AsVector2() ?? Vector2.Zero;
    }

    public void SetVertexColorAlbedo(bool enable)
    {
        _material?.SetShaderParameter("use_vertex_color_albedo", enable);
    }

    public void SetVertexColorEmission(bool enable)
    {
        _material?.SetShaderParameter("use_vertex_color_emission", enable);
    }

    public static PBSShader CreateInstance()
    {
        return new PBSShader();
    }

    public PBSShader Clone()
    {
        var clone = new PBSShader();

        clone.SetAlbedoColor(GetAlbedoColor());
        clone.SetAlbedoTexture(GetAlbedoTexture());
        clone.SetMetallic(GetMetallic());
        clone.SetSmoothness(GetSmoothness());
        clone.SetNormalMap(GetNormalMap(), GetNormalScale());
        clone.SetEmissionColor(GetEmissionColor());
        clone.SetEmissionTexture(GetEmissionTexture());
        clone.SetEmissionEnergy(GetEmissionEnergy());
        clone.SetOcclusionMap(GetOcclusionMap());
        clone.SetUVTransform(GetUVScale(), GetUVOffset());

        return clone;
    }

    public void Dispose()
    {
        _material?.Dispose();
        _material = null!;
    }
}

