using System;
using System.Threading;
using System.Threading.Tasks;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Assets;

/// <summary>
/// Base class for asset providers that load assets from URLs.
/// Handles URL-based loading with metadata fetching and cancellation support.
/// </summary>
/// <typeparam name="A">The asset type</typeparam>
/// <typeparam name="M">The metadata type</typeparam>
public abstract class UrlAssetProvider<A, M> : AssetProvider<A>, IAssetConsumer
    where A : Asset, new()
    where M : class, IAssetMetadata, new()
{
    public readonly Sync<Uri> URL;

    private A _asset;
    private M _metadata;
    private Uri _loadedUrl;
    private bool _isReady;
    private CancellationTokenSource _loadCancellation;
    private readonly object _loadLock = new object();

    public override A Asset => _isReady ? _asset : null;

    public override bool IsAssetAvailable
    {
        get
        {
            if (_asset == null) return false;
            if (URL.Value != _loadedUrl) return false;
            return _asset.LoadState == AssetLoadState.PartiallyLoaded ||
                   _asset.LoadState == AssetLoadState.FullyLoaded;
        }
    }

    /// <summary>
    /// The loaded metadata for the current asset.
    /// </summary>
    public M Metadata => _metadata;

    /// <summary>
    /// The raw asset instance regardless of load state.
    /// </summary>
    public A RawAsset => _asset;

    protected UrlAssetProvider()
    {
        URL = new Sync<Uri>(this, null);
        URL.OnChanged += _ => OnUrlChanged();
    }

    private void OnUrlChanged()
    {
        if (AssetReferenceCount > 0 || AlwaysLoad)
        {
            RefreshAssetState();
        }
    }

    /// <summary>
    /// Trigger refresh of asset state. Call when references or URL changes.
    /// </summary>
    protected void RefreshAssetState()
    {
        if (ForceUnload)
        {
            FreeAsset();
        }
        else if (AlwaysLoad)
        {
            UpdateAsset();
        }
        else if (AssetReferenceCount == 0 && IsAssetAvailable)
        {
            TryFreeAsset();
        }
        else if (AssetReferenceCount > 0)
        {
            UpdateAsset();
        }
    }

    protected override void UpdateAsset()
    {
        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;

        Task.Run(async () =>
        {
            try
            {
                await LoadAssetAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Expected when URL changes during load
            }
            catch (Exception ex)
            {
                AquaLogger.Log($"Error loading asset from {URL.Value}: {ex.Message}");
            }
        });
    }

    private async Task LoadAssetAsync(CancellationToken token)
    {
        Uri url = ProcessURL(URL.Value);
        if (url == null)
        {
            FreeAsset();
            return;
        }

        bool urlChanged;
        lock (_loadLock)
        {
            urlChanged = _loadedUrl != url;
            if (urlChanged)
            {
                FreeAssetInternal();
                _loadedUrl = url;
            }
        }

        if (token.IsCancellationRequested || IsDestroyed) return;

        // Load metadata first
        M metadata = await LoadMetadata(url, token);
        if (token.IsCancellationRequested || IsDestroyed) return;

        // Load the actual asset data
        A asset = await LoadAssetData(url, metadata, token);
        if (token.IsCancellationRequested || IsDestroyed)
        {
            asset?.Unload();
            return;
        }

        lock (_loadLock)
        {
            if (token.IsCancellationRequested || IsDestroyed)
            {
                asset?.Unload();
                return;
            }

            asset.InitializeStatic(url);
            asset.SetOwner(this);
            asset.RegisterConsumer(this);

            _asset = asset;
            _metadata = metadata;
            _isReady = true;
        }

        asset.NotifyAssigned(this);
        AssetCreated();
        OnAssetLoaded();
    }

    protected override void FreeAsset()
    {
        lock (_loadLock)
        {
            FreeAssetInternal();
        }
    }

    private void FreeAssetInternal()
    {
        _loadCancellation?.Cancel();
        if (_asset != null)
        {
            _isReady = false;
            _asset.UnregisterConsumer(this);
            _asset.Unload();
            _asset = null;
            _metadata = null;
            _loadedUrl = null;
            AssetRemoved();
            OnAssetUnloaded();
        }
    }

    /// <summary>
    /// Load metadata for the asset at the given URL.
    /// Override to implement metadata loading (e.g., reading image headers).
    /// </summary>
    protected abstract Task<M> LoadMetadata(Uri url, CancellationToken token);

    /// <summary>
    /// Load the actual asset data from the URL using the metadata.
    /// </summary>
    protected abstract Task<A> LoadAssetData(Uri url, M metadata, CancellationToken token);

    /// <summary>
    /// Called when the asset has been successfully loaded.
    /// </summary>
    protected virtual void OnAssetLoaded()
    {
    }

    /// <summary>
    /// Called when the asset state changes (partially loaded, fully loaded, etc.).
    /// </summary>
    protected virtual void OnAssetUpdated()
    {
    }

    /// <summary>
    /// Called when the asset is unloaded.
    /// </summary>
    protected virtual void OnAssetUnloaded()
    {
    }

    // IAssetConsumer implementation

    void IAssetConsumer.OnAssetAssigned(Asset asset)
    {
        // Asset was assigned through LoadAssetAsync, nothing extra needed here
    }

    void IAssetConsumer.OnAssetStateChanged(Asset asset)
    {
        if (asset != _asset) return;

        switch (asset.LoadState)
        {
            case AssetLoadState.PartiallyLoaded:
            case AssetLoadState.FullyLoaded:
                if (!_isReady)
                {
                    _isReady = true;
                    AssetCreated();
                    OnAssetLoaded();
                }
                else
                {
                    AssetUpdated();
                    OnAssetUpdated();
                }
                break;

            case AssetLoadState.Failed:
                _isReady = false;
                AssetRemoved();
                break;

            case AssetLoadState.Unloaded:
                _isReady = false;
                AssetRemoved();
                OnAssetUnloaded();
                break;
        }
    }

    public override void OnDestroy()
    {
        _loadCancellation?.Cancel();
        FreeAsset();
        base.OnDestroy();
    }
}
