// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumora.Core.Networking;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets;

/// <summary>
/// Central coordinator for the asset system. Owns asset gathering (file/network resolution)
/// and is the single handle assets use to reach engine services. Assets hold a back-reference
/// to this manager and route their loads through <see cref="RequestGather"/>.
/// </summary>
public class AssetManager : IDisposable
{
    private bool _disposing;

    // Shared asset instances, deduplicated by (URL, type). Guarded by _managerLock.
    private readonly Dictionary<AssetID, AssetVariantManager> _variantManagers = new();
    private readonly List<AssetID> _managersToRemove = new();
    private readonly object _managerLock = new();

    /// <summary>
    /// The engine this manager belongs to. Assets reach the engine via <c>AssetManager.Engine</c>
    /// rather than the global <c>Engine.Current</c>.
    /// </summary>
    public Engine Engine { get; }

    public AssetManager(Engine engine)
    {
        Engine = engine;
    }

    public Task InitializeAsync()
    {
        Logger.Log("AssetManager: Initialized");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fetch the raw bytes for an asset URL. Scheme resolution (local/file/http/builtin/peer)
    /// is handled by <see cref="AssetFetcher"/>; the completion callback fires on whichever
    /// thread pumps <see cref="AssetFetcher.ProcessQueue"/> (currently the world update loop).
    /// </summary>
    public Task<byte[]> RequestGather(Uri assetURL)
    {
        if (assetURL == null)
            return Task.FromResult<byte[]>(null!);

        var tcs = new TaskCompletionSource<byte[]>();
        AssetFetcher.FetchAsset(assetURL.ToString(), bytes => tcs.TrySetResult(bytes));
        return tcs.Task;
    }

    /// <summary>
    /// Request the shared asset for <paramref name="assetURL"/>. All requesters for the same
    /// (URL, type) share a single instance; the asset loads itself on the first request and
    /// stays alive until every requester releases it via <see cref="ReleaseAsset{A}"/>.
    /// </summary>
    public void RequestAsset<A>(Uri assetURL, IAssetRequester requester, IAssetVariantDescriptor? descriptor = null) where A : LoadableAsset, new()
    {
        if (assetURL == null || requester == null)
            return;

        var key = new AssetID(assetURL, typeof(A));
        AssetVariantManager manager;
        lock (_managerLock)
        {
            if (!_variantManagers.TryGetValue(key, out manager!))
            {
                manager = new AssetVariantManager<A>(assetURL, this);
                _variantManagers[key] = manager;
            }
        }
        manager.RequestAsset(requester, descriptor);
    }

    /// <summary>Release a requester's hold on the shared asset for <paramref name="assetURL"/>.</summary>
    public void ReleaseAsset<A>(Uri assetURL, IAssetRequester requester, IAssetVariantDescriptor? descriptor = null) where A : LoadableAsset, new()
    {
        if (assetURL == null || requester == null)
            return;

        AssetVariantManager? manager;
        lock (_managerLock)
        {
            _variantManagers.TryGetValue(new AssetID(assetURL, typeof(A)), out manager);
        }
        manager?.RemoveRequest(requester, descriptor);
    }

    // Called by a variant manager once its asset unloads. Removal is deferred to Update so we
    // don't mutate the dictionary from an unload task thread mid-iteration.
    internal void ScheduleVariantManagerRemoval(AssetID id)
    {
        lock (_managerLock)
        {
            _managersToRemove.Add(id);
        }
    }

    /// <summary>
    /// Per-frame tick, driven by the engine update loop. Gather completion is presently pumped
    /// by the world loop; the gather and engine-integration pumps move here once the integration
    /// queue lands.
    /// </summary>
    public void Update(float deltaTime)
    {
        lock (_managerLock)
        {
            if (_managersToRemove.Count == 0)
                return;

            foreach (var id in _managersToRemove)
            {
                if (_variantManagers.TryGetValue(id, out var manager) && manager.RequestCount == 0)
                    _variantManagers.Remove(id);
            }
            _managersToRemove.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposing)
            return;

        _disposing = true;
        Logger.Log("AssetManager: Disposed");
    }
}
