// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Material Property Blocks")]
public abstract class MaterialPropertyBlockProvider : DynamicAssetProvider<MaterialPropertyBlockAsset>
{
    private Action _assetUpdatedCallback;

    protected override void OnAssetCreated(MaterialPropertyBlockAsset asset)
    {
    }

    protected override void UpdateAsset(MaterialPropertyBlockAsset asset)
    {
        asset.Clear();
        UpdateBlock(asset);

        _assetUpdatedCallback ??= () => AssetUpdated();
        asset.ApplyChanges(_assetUpdatedCallback);
    }

    protected abstract void UpdateBlock(MaterialPropertyBlockAsset asset);

    protected override void OnAssetCleared()
    {
    }

    public void ForceUpdate()
    {
        MarkChangeDirty();
    }
}
