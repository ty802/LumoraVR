// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

public class MaterialPropertyBlockAsset : DynamicImplementableAsset<IMaterialPropertyBlockAssetHook>
{
    private int _activeRequestCount;

    public override int ActiveRequestCount => _activeRequestCount;

    public bool IsValid => Hook?.IsValid ?? false;

    public void SetFloat(string property, float value)
    {
        Hook?.SetFloat(property, value);
        Version++;
    }

    public void SetInt(string property, int value)
    {
        Hook?.SetInt(property, value);
        Version++;
    }

    public void SetBool(string property, bool value)
    {
        Hook?.SetBool(property, value);
        Version++;
    }

    public void SetColor(string property, colorHDR value)
    {
        Hook?.SetColor(property, value);
        Version++;
    }

    public void SetFloat2(string property, float2 value)
    {
        Hook?.SetFloat2(property, value);
        Version++;
    }

    public void SetFloat3(string property, float3 value)
    {
        Hook?.SetFloat3(property, value);
        Version++;
    }

    public void SetFloat4(string property, float4 value)
    {
        Hook?.SetFloat4(property, value);
        Version++;
    }

    public void SetTexture(string property, TextureAsset texture)
    {
        Hook?.SetTexture(property, texture);
        Version++;
    }

    public void Clear()
    {
        Hook?.Clear();
        Version++;
    }

    public void ApplyChanges(Action callback)
    {
        Hook?.ApplyChanges(callback);
    }

    public object ApplyToMaterial(MaterialAsset material)
    {
        if (material == null)
        {
            return null!;
        }

        return Hook?.ApplyToMaterial(material.GodotMaterial, material.MaterialType) ?? material.GodotMaterial;
    }

    public void AddRequest()
    {
        _activeRequestCount++;
    }

    public void RemoveRequest()
    {
        _activeRequestCount = System.Math.Max(0, _activeRequestCount - 1);
    }

    public override void Unload()
    {
        _activeRequestCount = 0;
        base.Unload();
    }
}
