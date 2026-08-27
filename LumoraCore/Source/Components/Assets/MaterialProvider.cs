// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Materials")]
public abstract class MaterialProvider : DynamicAssetProvider<MaterialAsset>
{
    private Action _assetUpdatedCallback = null!;

    protected abstract MaterialType MaterialType { get; }

    protected override void OnAssetCreated(MaterialAsset asset)
    {
        asset.SetMaterialType(MaterialType);
    }

    protected override void UpdateAsset(MaterialAsset asset)
    {
        Lumora.Core.Logging.Logger.Debug($"MaterialProvider.UpdateAsset: [{GetType().Name}] Updating material");
        asset.Clear();
        UpdateMaterial(asset);

        if (_assetUpdatedCallback == null)
        {
            _assetUpdatedCallback = () => AssetUpdated();
        }
        Lumora.Core.Logging.Logger.Debug($"MaterialProvider.UpdateAsset: [{GetType().Name}] Calling ApplyChanges");
        asset.ApplyChanges(_assetUpdatedCallback);
    }

    protected abstract void UpdateMaterial(MaterialAsset asset);

    protected override void OnAssetCleared()
    {
    }

    public void ForceUpdate()
    {
        MarkChangeDirty();
    }
}
