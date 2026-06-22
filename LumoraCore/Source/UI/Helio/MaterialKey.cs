// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

// How a queued graphic participates in GPU stencil masking. None = normal (rect-clip path). Write = a mask
// shape that stamps the stencil reference (UI_StencilWrite). Test = content clipped to a mask, drawn only
// where the stencil matches (UI_UnlitStencil). The role picks the shader variant at material-clone time and
// keeps write/test/none submeshes distinct so they never batch together. -xlinka
public enum StencilRole : byte
{
    None = 0,
    Write = 1,
    Test = 2,
}

public readonly struct MaterialKey : IEquatable<MaterialKey>
{
    public readonly IAssetProvider<MaterialAsset>? BaseMaterial;
    public readonly object? Key;
    public readonly MaterialMapper? Mapper;
    public readonly Rect? ClipRect;
    public readonly StencilRole Stencil;

    public MaterialKey(IAssetProvider<MaterialAsset>? baseMaterial, object? key, MaterialMapper? mapper, Rect? clipRect = null, StencilRole stencil = StencilRole.None)
    {
        BaseMaterial = baseMaterial;
        Key = key;
        Mapper = mapper;
        ClipRect = clipRect;
        Stencil = stencil;
    }

    public bool Equals(MaterialKey other)
    {
        return BaseMaterial == other.BaseMaterial
            && Key == other.Key
            && Mapper == other.Mapper
            && Nullable.Equals(ClipRect, other.ClipRect)
            && Stencil == other.Stencil;
    }

    public override bool Equals(object? obj) => obj is MaterialKey other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(BaseMaterial, Key, Mapper, ClipRect, Stencil);
    }

    public static bool operator ==(MaterialKey left, MaterialKey right) => left.Equals(right);
    public static bool operator !=(MaterialKey left, MaterialKey right) => !left.Equals(right);
}
