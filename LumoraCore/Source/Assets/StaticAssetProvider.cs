// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Assets;

/// <summary>
/// Component base for providers backed by a shared, URL-loaded asset. The provider is a thin
/// <see cref="IAssetRequester"/>: it resolves its URL and asks the <see cref="AssetManager"/> for
/// the asset, which loads itself and is shared across every requester for the same URL.
/// </summary>
public abstract class StaticAssetProvider<A> : AssetProvider<A>, IAssetRequester
    where A : LoadableAsset, new()
{
    public readonly Sync<Uri> URL;

    private A? _asset;
    private Uri? _requestedSourceUrl;                  // URL.Value the current request was issued for
    private Uri? _resolvedUrl;                          // ProcessURL result, the RequestAsset key
    private IAssetVariantDescriptor? _requestedVariant; // descriptor the current request was issued for
    private readonly object _lock = new();

    public override A Asset =>
        (_asset != null && IsLoaded(_asset)) ? _asset : null!;

    public override bool IsAssetAvailable =>
        _asset != null && URL.Value == _requestedSourceUrl && IsLoaded(_asset);

    private static bool IsLoaded(A asset) =>
        asset.LoadState is AssetLoadState.PartiallyLoaded or AssetLoadState.FullyLoaded;

    protected StaticAssetProvider()
    {
        URL = new Sync<Uri>(this, null!);
        URL.OnChanged += _ => OnUrlChanged();
    }

    private void OnUrlChanged()
    {
        if (AssetReferenceCount > 0)
            UpdateAsset();
    }

    /// <summary>
    /// The variant descriptor (wrap/mipmap options, etc.) for this provider's request, or null
    /// for the default variant. Recomputed on every refresh; if it changes, the asset is
    /// re-requested for the new variant.
    /// </summary>
    protected virtual IAssetVariantDescriptor? GetVariantDescriptor() => null;

    protected override void UpdateAsset()
    {
        Uri resolved = ProcessURL(URL.Value);
        IAssetVariantDescriptor? variant = GetVariantDescriptor();

        lock (_lock)
        {
            if (resolved == _resolvedUrl && Equals(variant, _requestedVariant))
                return; // already requested for this resolved URL + variant

            ReleaseCurrent();
            _requestedSourceUrl = URL.Value;
            _resolvedUrl = resolved;
            _requestedVariant = variant;
        }

        if (resolved == null)
        {
            AssetRemoved();
            return;
        }

        var manager = Engine.Current?.AssetManager;
        if (manager == null)
        {
            LumoraLogger.Warn($"StaticAssetProvider[{GetType().Name}]: no AssetManager; cannot request {resolved}");
            return;
        }

        manager.RequestAsset<A>(resolved, this, variant);
    }

    protected override void FreeAsset()
    {
        lock (_lock)
        {
            ReleaseCurrent();
        }
        AssetRemoved();
    }

    // Must be called under _lock.
    private void ReleaseCurrent()
    {
        if (_resolvedUrl != null)
            Engine.Current?.AssetManager?.ReleaseAsset<A>(_resolvedUrl, this, _requestedVariant);

        _asset = null;
        _requestedSourceUrl = null;
        _resolvedUrl = null;
        _requestedVariant = null;
    }

    // IAssetRequester. Called by the asset's variant manager.

    void IAssetRequester.AssignAsset(Asset asset) => _asset = (A)asset;

    void IAssetRequester.AssetLoadStateUpdated(Asset asset)
    {
        if (asset != _asset)
            return;

        // AssetCreated/AssetUpdated/AssetRemoved marshal to the main thread internally, so this
        // is safe to run on the asset's background load thread.
        switch (asset.LoadState)
        {
            case AssetLoadState.PartiallyLoaded:
            case AssetLoadState.FullyLoaded:
                AssetCreated();
                AssetUpdated();
                break;
            case AssetLoadState.Unloaded:
                if (_asset == asset)
                    _asset = null;
                AssetRemoved();
                break;
        }
    }
}
