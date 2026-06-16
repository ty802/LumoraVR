// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;

namespace Helio.UI;

public readonly struct MaterialMap : IEquatable<MaterialMap>
{
    public readonly IAssetProvider<MaterialAsset>? Material;
    public readonly IAssetProvider<MaterialPropertyBlockAsset>? PropertyBlock;

    public IAssetProvider<MaterialAsset>? FilteredMaterial
    {
        get
        {
            if (Material == null || Material.IsDestroyed)
            {
                return null;
            }
            return Material;
        }
    }

    public IAssetProvider<MaterialPropertyBlockAsset>? FilteredPropertyBlock
    {
        get
        {
            if (PropertyBlock == null || PropertyBlock.IsDestroyed)
            {
                return null;
            }
            return PropertyBlock;
        }
    }

    public MaterialMap(IAssetProvider<MaterialAsset>? material)
    {
        Material = material;
        PropertyBlock = null;
    }

    public MaterialMap(IAssetProvider<MaterialAsset>? material, IAssetProvider<MaterialPropertyBlockAsset>? propertyBlock)
    {
        Material = material;
        PropertyBlock = propertyBlock;
    }

    public bool Equals(MaterialMap other)
    {
        return Material == other.Material
            && PropertyBlock == other.PropertyBlock;
    }

    public override bool Equals(object? obj) => obj is MaterialMap other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Material, PropertyBlock);
    }

    public static bool operator ==(MaterialMap left, MaterialMap right) => left.Equals(right);
    public static bool operator !=(MaterialMap left, MaterialMap right) => !left.Equals(right);
}
