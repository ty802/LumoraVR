// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Assets;

/// <summary>
/// Static registry for asset hook types.
/// Maps asset types to their hook implementations.
/// </summary>
public static class AssetHookRegistry
{
    private static readonly Dictionary<Type, Type> _assetToHook = new();

    /// <summary>
    /// Register a hook type for an asset type.
    /// </summary>
    public static void Register<TAsset, THook>()
        where TAsset : IAsset
        where THook : IAssetHook
    {
        Register(typeof(TAsset), typeof(THook));
    }

    /// <summary>
    /// Register a hook type for an asset type.
    /// </summary>
    public static void Register(Type assetType, Type hookType)
    {
        if (!typeof(IAsset).IsAssignableFrom(assetType))
        {
            throw new ArgumentException($"Asset type {assetType} must implement IAsset");
        }

        if (!typeof(IAssetHook).IsAssignableFrom(hookType))
        {
            throw new ArgumentException($"Hook type {hookType} must implement IAssetHook");
        }

        _assetToHook[assetType] = hookType;
    }

    /// <summary>
    /// Get the hook type for an asset type.
    /// </summary>
    public static Type GetHookType(Type assetType)
    {
        if (_assetToHook.TryGetValue(assetType, out var hookType))
            return hookType;

        // Walk up inheritance chain
        var baseType = assetType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_assetToHook.TryGetValue(baseType, out hookType))
                return hookType;
            baseType = baseType.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Clear all registrations.
    /// </summary>
    public static void Clear() => _assetToHook.Clear();
}

/// <summary>
/// Base class for dynamic assets that have engine-specific hooks.
/// Runtime-generated assets that bridge to engine implementations through hooks.
/// </summary>
public abstract class DynamicImplementableAsset<C> : DynamicAsset where C : class, IAssetHook
{
    /// <summary>
    /// The engine-specific hook that implements this asset.
    /// For materials, this would be a Godot ShaderMaterial wrapper.
    /// </summary>
    public C Hook { get; private set; }

    /// <summary>
    /// Create the hook instance for this asset.
    /// Override to provide custom hook instantiation.
    /// </summary>
    protected virtual C InstantiateHook()
    {
        Type hookType = GetHookType();
        if (hookType == null)
        {
            return null;
        }
        return (C)Activator.CreateInstance(hookType);
    }

    /// <summary>
    /// Get the hook type for this asset.
    /// Uses the AssetHookRegistry by default.
    /// </summary>
    protected virtual Type GetHookType()
    {
        return AssetHookRegistry.GetHookType(GetType());
    }

    public override void InitializeDynamic()
    {
        base.InitializeDynamic();
        InitializeHook();
    }

    private void InitializeHook()
    {
        Hook = InstantiateHook();
        Hook?.Initialize(this);
    }

    public override void Unload()
    {
        Hook?.Unload();
    }
}
