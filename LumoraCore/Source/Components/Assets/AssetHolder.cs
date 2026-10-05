// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Components.Assets;

// Keeps an asset built while nothing else is using it. A provider frees its asset when the last reference
// lets go, so the material a swap will come back to, or a texture only shown now and then, is rebuilt from
// scratch every time it returns; holding it here keeps it ready. Holding is all this does. -xlinka
[ComponentCategory("Assets")]
[ComponentGenericTypes(typeof(MaterialAsset), typeof(TextureAsset), typeof(CubemapAsset), typeof(AudioClip), typeof(MeshDataAsset))]
public sealed class AssetHolder<A> : Component where A : Asset
{
    public readonly AssetRef<A> Asset;

    public AssetHolder()
    {
        Asset = new AssetRef<A>(this);
    }
}
