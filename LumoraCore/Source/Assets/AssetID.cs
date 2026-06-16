// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

/// <summary>
/// Identity of a shared asset: a URL paired with the asset type it loads into. Two requests
/// with the same URL and type resolve to the same asset instance.
/// </summary>
public readonly struct AssetID : IEquatable<AssetID>
{
    public readonly Uri Url;
    public readonly Type AssetType;

    public AssetID(Uri url, Type assetType)
    {
        Url = url;
        AssetType = assetType;
    }

    public bool Equals(AssetID other) => Url == other.Url && AssetType == other.AssetType;
    public override bool Equals(object? obj) => obj is AssetID other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Url, AssetType);
    public override string ToString() => $"{AssetType.Name}@{Url}";
}
