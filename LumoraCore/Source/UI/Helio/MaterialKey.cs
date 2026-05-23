// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;

namespace Helio.UI;

public readonly struct MaterialKey : IEquatable<MaterialKey>
{
    public readonly IAssetProvider<MaterialAsset>? BaseMaterial;
    public readonly object? Key;
    public readonly MaterialMapper? Mapper;

    public MaterialKey(IAssetProvider<MaterialAsset>? baseMaterial, object? key, MaterialMapper? mapper)
    {
        BaseMaterial = baseMaterial;
        Key = key;
        Mapper = mapper;
    }

    public bool Equals(MaterialKey other)
    {
        return BaseMaterial == other.BaseMaterial
            && Key == other.Key
            && Mapper == other.Mapper;
    }

    public override bool Equals(object? obj) => obj is MaterialKey other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(BaseMaterial, Key, Mapper);
    }

    public static bool operator ==(MaterialKey left, MaterialKey right) => left.Equals(right);
    public static bool operator !=(MaterialKey left, MaterialKey right) => !left.Equals(right);
}
