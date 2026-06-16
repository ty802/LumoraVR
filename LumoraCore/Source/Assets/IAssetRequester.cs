// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// Something that requests a shared, URL-loaded asset and wants to be notified about it.
/// Providers implement this; the manager hands them the resolved asset and load-state updates.
/// </summary>
public interface IAssetRequester
{
    /// <summary>The asset instance this requester was bound to.</summary>
    void AssignAsset(Asset asset);

    /// <summary>The bound asset's load state changed (loaded, failed, etc.).</summary>
    void AssetLoadStateUpdated(Asset asset);
}
