// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;

namespace Lumora.Godot.Hooks;

public abstract class AssetHook : IAssetHook
{
    protected IAsset asset = null!;

    // assets are global and not tied to a specific world
    public Engine Engine => Lumora.Core.Engine.Current;

    // may be null if no world is focused
    public World FocusedWorld => Engine?.WorldManager?.FocusedWorld!;

    public IAsset Asset => asset;

    public void Initialize(IAsset asset)
    {
        this.asset = asset;
    }

    public abstract void Unload();
}

