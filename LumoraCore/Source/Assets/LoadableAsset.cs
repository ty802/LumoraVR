// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lumora.Core.Assets;

/// <summary>
/// Base for assets that can be loaded from a URL. The same class also supports procedural
/// (dynamic) instances: initialized via <see cref="Asset.InitializeStatic"/> it gathers and
/// decodes itself from its URL (<see cref="LoadSelf"/>), tracks the requesters keeping it alive,
/// and is shared across every requester for the same (URL, type); initialized via
/// <see cref="Asset.InitializeDynamic"/> it is procedural, has no requesters, and is loaded
/// immediately.
/// </summary>
public abstract class LoadableAsset : Asset
{
    private readonly HashSet<IAssetRequester> _requests = new();
    private readonly object _stateLock = new();
    private bool _runningUpdate;
    private bool _unload;

    /// <summary>The variant manager tracking this asset; set when the manager creates it.</summary>
    internal AssetVariantManager? VariantManager { get; set; }

    /// <summary>
    /// The descriptor this instance was loaded for (wrap/mipmap options, etc.), or null for the
    /// default variant. Set when the manager creates the asset; read by <see cref="LoadSelf"/>.
    /// </summary>
    public IAssetVariantDescriptor? TargetVariant { get; internal set; }

    public override int ActiveRequestCount
    {
        get { lock (_stateLock) { return _requests.Count; } }
    }

    // Called by the variant manager (under its lock). Kicks off loading on the first request.
    internal void TryAddRequest(IAssetRequester requester)
    {
        CheckStatic();
        lock (_stateLock)
        {
            _requests.Add(requester);
        }

        requester.AssignAsset(this);

        if (LoadState == AssetLoadState.Created)
        {
            RunUpdate();
        }
        else
        {
            requester.AssetLoadStateUpdated(this);
        }
    }

    internal void RemoveRequest(IAssetRequester requester)
    {
        CheckStatic();
        lock (_stateLock)
        {
            _requests.Remove(requester);
        }
    }

    internal void QueueUnload()
    {
        _unload = true;
        RunUpdate();
    }

    protected override void OnLoadStateChanged()
    {
        base.OnLoadStateChanged();

        if (LoadState == AssetLoadState.Unloaded)
        {
            return;
        }

        IAssetRequester[] snapshot;
        lock (_stateLock)
        {
            snapshot = _requests.ToArray();
        }
        foreach (var requester in snapshot)
        {
            requester.AssetLoadStateUpdated(this);
        }
    }

    private void RunUpdate()
    {
        lock (_stateLock)
        {
            if (_runningUpdate)
            {
                return;
            }
            _runningUpdate = true;
        }
        _ = Task.Run(ProcessUpdate);
    }

    private async Task ProcessUpdate()
    {
        try
        {
            if (LoadState == AssetLoadState.Created)
            {
                SetLoadState(AssetLoadState.LoadStarted);
            }

            if (!_unload)
            {
                try
                {
                    await LoadSelf().ConfigureAwait(false);
                    if (LoadState != AssetLoadState.FullyLoaded && LoadState != AssetLoadState.Failed)
                    {
                        SetLoadState(AssetLoadState.FullyLoaded);
                    }
                }
                catch (Exception ex)
                {
                    FailLoad(ex.Message);
                }
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _runningUpdate = false;
            }
        }

        if (_unload)
        {
            Unload();
            if (LoadState != AssetLoadState.Unloaded)
            {
                SetLoadState(AssetLoadState.Unloaded);
            }
            VariantManager?.OnAssetUnloaded(this);
        }
    }

    /// <summary>
    /// Load this asset's contents from <see cref="Asset.AssetURL"/>. Implementations gather the
    /// raw bytes via <c>AssetManager.RequestGather</c> and decode them. Runs off the main thread.
    /// Only invoked for URL (static) instances; procedural instances never call this.
    /// </summary>
    protected abstract Task LoadSelf();
}
