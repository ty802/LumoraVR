// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets;

/// <summary>
/// Tracks the shared asset instances for one (URL, type) pair. Each distinct variant descriptor
/// gets its own instance; requesters with equal descriptors share one. Variants are matched
/// exactly (no cloud/compression variant generation).
/// </summary>
public abstract class AssetVariantManager
{
    public readonly Uri AssetURL;
    public readonly AssetManager AssetManager;

    // Serializes variant add/remove and unload scheduling for this (URL, type).
    internal readonly object VariantLock = new();

    public Engine Engine => AssetManager.Engine;

    public abstract int RequestCount { get; }
    public abstract Type AssetType { get; }

    protected AssetVariantManager(Uri assetUrl, AssetManager manager)
    {
        AssetURL = assetUrl;
        AssetManager = manager;
    }

    public abstract void RequestAsset(IAssetRequester requester, IAssetVariantDescriptor? descriptor);
    public abstract void RemoveRequest(IAssetRequester requester, IAssetVariantDescriptor? descriptor);

    internal abstract void OnAssetUnloaded(LoadableAsset asset);

    /// <summary>
    /// Unload the asset after a delay, but only if it is still unreferenced when the delay
    /// elapses. <see cref="Asset.UnloadKey"/> guards against a request that arrives in between.
    /// </summary>
    internal void ScheduleUnload(LoadableAsset asset, float seconds)
    {
        asset.UnloadKey++;
        int key = asset.UnloadKey;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                lock (VariantLock)
                {
                    if (asset.UnloadKey == key && asset.ActiveRequestCount <= 0)
                    {
                        asset.QueueUnload();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"AssetVariantManager: error scheduling unload for {AssetURL}: {ex.Message}");
            }
        });
    }
}

public sealed class AssetVariantManager<A> : AssetVariantManager where A : LoadableAsset, new()
{
    // Key for the "no descriptor" default variant (descriptors can't be null dictionary keys).
    private static readonly object DefaultKey = new();

    private readonly Dictionary<object, A> _variants = new();

    public override Type AssetType => typeof(A);

    public override int RequestCount
    {
        get
        {
            lock (VariantLock)
            {
                int count = 0;
                foreach (var variant in _variants.Values)
                    count += variant.ActiveRequestCount;
                return count;
            }
        }
    }

    public AssetVariantManager(Uri assetUrl, AssetManager manager)
        : base(assetUrl, manager)
    {
    }

    public override void RequestAsset(IAssetRequester requester, IAssetVariantDescriptor? descriptor)
    {
        object key = descriptor ?? DefaultKey;
        lock (VariantLock)
        {
            if (!_variants.TryGetValue(key, out var asset))
            {
                asset = new A { VariantManager = this, TargetVariant = descriptor };
                asset.InitializeStatic(AssetURL, AssetManager);
                _variants[key] = asset;
            }
            asset.TryAddRequest(requester);
        }
    }

    public override void RemoveRequest(IAssetRequester requester, IAssetVariantDescriptor? descriptor)
    {
        object key = descriptor ?? DefaultKey;
        lock (VariantLock)
        {
            if (_variants.TryGetValue(key, out var asset))
            {
                asset.RemoveRequest(requester);
                if (asset.ActiveRequestCount <= 0)
                    ScheduleUnload(asset, asset.UnloadDelay);
            }
        }
    }

    internal override void OnAssetUnloaded(LoadableAsset asset)
    {
        bool emptied;
        lock (VariantLock)
        {
            object? foundKey = null;
            foreach (var entry in _variants)
            {
                if (entry.Value == asset)
                {
                    foundKey = entry.Key;
                    break;
                }
            }
            if (foundKey == null)
                return;

            _variants.Remove(foundKey);
            emptied = _variants.Count == 0;
        }

        // Schedule manager removal outside VariantLock. ScheduleVariantManagerRemoval takes the
        // manager lock, and AssetManager.Update takes the manager lock then reads RequestCount
        // (VariantLock); keeping these unnested here avoids the lock-order inversion.
        if (emptied)
            AssetManager.ScheduleVariantManagerRemoval(new AssetID(AssetURL, typeof(A)));
    }
}
