// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.Meshes;

// A provider only loads its mesh for its references, and a renderer is one, but a collider or a hull that
// reads the geometry without drawing it was not. A floor whose collision mesh nothing draws never loaded,
// so its collider solved to nothing and people fell through it. Whatever reads a mesh holds one of these on
// the provider it points at: that keeps the mesh loaded and hands on the provider's "data landed", which
// arrives on the world thread. -xlinka
public sealed class MeshSourceWatch : IAssetRef
{
    private readonly Action _updated;
    private IAssetProvider? _watched;

    public MeshSourceWatch(Action updated) => _updated = updated;

    public IAssetProvider Target { get; set; } = null!;

    public void AssetUpdated() => _updated();

    public void Watch(IAssetProvider? provider)
    {
        if (ReferenceEquals(provider, _watched))
            return;
        if (_watched != null && !_watched.IsDestroyed)
            _watched.ReferenceFreed(this);
        _watched = provider;
        Target = provider!;
        provider?.ReferenceSet(this);
    }
}
