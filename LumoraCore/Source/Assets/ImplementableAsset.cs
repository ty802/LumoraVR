// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

/// <summary>
/// Base for assets that bridge to an engine-specific implementation through a hook and can be
/// either URL-loaded or procedural. The static counterpart shares the hook model with
/// <see cref="DynamicImplementableAsset{C}"/>, but inherits <see cref="LoadableAsset"/>'s
/// request/self-load lifecycle so the same class serves both URL instances (decoded via
/// <see cref="LoadableAsset.LoadSelf"/>) and procedural instances (data pushed in directly).
/// </summary>
public abstract class ImplementableAsset<C> : LoadableAsset where C : class, IAssetHook
{
    /// <summary>The engine-specific hook implementing this asset.</summary>
    public C Hook { get; private set; } = null!;

    protected virtual C InstantiateHook()
    {
        Type hookType = GetHookType();
        if (hookType == null)
        {
            return null!;
        }
        return (C)Activator.CreateInstance(hookType)!;
    }

    protected virtual Type GetHookType() => AssetHookRegistry.GetHookType(GetType());

    public override void InitializeStatic(Uri assetUrl, AssetManager? manager = null)
    {
        base.InitializeStatic(assetUrl, manager);
        InitializeHook();
    }

    public override void InitializeDynamic(AssetManager? manager = null)
    {
        base.InitializeDynamic(manager);
        InitializeHook();
    }

    private void InitializeHook()
    {
        Hook = InstantiateHook();
        Hook?.Initialize(this);
    }

    public override void Unload() => Hook?.Unload();
}
