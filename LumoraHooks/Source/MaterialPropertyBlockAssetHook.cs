// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Godot.Hooks;

public class MaterialPropertyBlockAssetHook : AssetHook, IMaterialPropertyBlockAssetHook
{
    private readonly Dictionary<string, object> _properties = new();
    private readonly Dictionary<ulong, Material> _materialVariants = new();

    public bool IsValid => true;

    public void SetFloat(string property, float value)
    {
        _properties[property] = value;
        ClearVariants();
    }

    public void SetInt(string property, int value)
    {
        _properties[property] = value;
        ClearVariants();
    }

    public void SetBool(string property, bool value)
    {
        _properties[property] = value;
        ClearVariants();
    }

    public void SetColor(string property, colorHDR value)
    {
        _properties[property] = new Color(value.r, value.g, value.b, value.a);
        ClearVariants();
    }

    public void SetFloat2(string property, float2 value)
    {
        _properties[property] = new Vector2(value.x, value.y);
        ClearVariants();
    }

    public void SetFloat3(string property, float3 value)
    {
        _properties[property] = new Vector3(value.x, value.y, value.z);
        ClearVariants();
    }

    public void SetFloat4(string property, float4 value)
    {
        _properties[property] = new Vector4(value.x, value.y, value.z, value.w);
        ClearVariants();
    }

    public void SetTexture(string property, TextureAsset texture)
    {
        if (texture?.Hook is TextureAssetHook textureHook && textureHook.IsValid)
        {
            _properties[property] = textureHook.GodotTexture;
        }
        else
        {
            _properties[property] = null;
        }

        ClearVariants();
    }

    public void Clear()
    {
        _properties.Clear();
        ClearVariants();
    }

    public void ApplyChanges(Action callback)
    {
        callback?.Invoke();
    }

    public object ApplyToMaterial(object baseMaterial, MaterialType materialType)
    {
        if (baseMaterial is not Material material)
        {
            return baseMaterial;
        }

        ulong key = material.GetInstanceId();
        if (!_materialVariants.TryGetValue(key, out var variant) || variant == null || !GodotObject.IsInstanceValid(variant))
        {
            variant = MaterialPropertyApplicator.CloneWithBlock(material, materialType, _properties);
            if (variant == material)
            {
                return material;
            }
            _materialVariants[key] = variant;
        }

        return variant;
    }

    public override void Unload()
    {
        Clear();
    }

    private void ClearVariants()
    {
        foreach (var material in _materialVariants.Values)
        {
            material?.Dispose();
        }

        _materialVariants.Clear();
    }
}
