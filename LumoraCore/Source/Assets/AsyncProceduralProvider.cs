// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;
using System.Threading.Tasks;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Assets;

public delegate void AssetIntegratedCallback(bool isNewInstance);

public abstract class AsyncProceduralProvider<A> : DynamicAssetProvider<A> where A : Asset, new()
{
    private SpinLock _updateLock = new SpinLock(enableThreadOwnerTracking: false);
    private volatile bool _isUpdating;
    private volatile bool _pendingUpdate;
    private volatile bool _safeDisposeComplete;
    private int _completedUpdates;
    private bool _hasError;

    private Action<IAsset> _onWriteLockAcquired = null!;
    private Action _backgroundUpdateAction = null!;
    private Func<Task> _asyncUpdateAction = null!;
    private AssetIntegratedCallback _integratedCallback = null!;

    public int CompletedUpdateCount => _completedUpdates;

    public bool HasError => _hasError;

    // True routes updates through async instead of a background thread.
    protected virtual bool PreferAsyncUpdate => false;

    protected override void UpdateAsset(A asset)
    {
        bool lockTaken = false;
        try
        {
            _updateLock.Enter(ref lockTaken);

            if (IsDestroyed)
            {
                RunSafeDispose();
                return;
            }

            if (_isUpdating)
            {
                _pendingUpdate = true;
                return;
            }

            _isUpdating = true;
        }
        finally
        {
            if (lockTaken) _updateLock.Exit();
        }

        PrepareUpdateState();

        _onWriteLockAcquired ??= OnWriteLockAcquired;
        _backgroundUpdateAction ??= ExecuteBackgroundUpdate;
        _asyncUpdateAction ??= ExecuteAsyncUpdate;
        _integratedCallback ??= OnAssetIntegrated;

        asset.RequestWriteLock(this, _onWriteLockAcquired);
    }

    protected override void FreeAsset()
    {
        bool lockTaken = false;
        try
        {
            _updateLock.Enter(ref lockTaken);
            if (_isUpdating)
            {
                _pendingUpdate = true;
                return;
            }
        }
        finally
        {
            if (lockTaken) _updateLock.Exit();
        }
        base.FreeAsset();
    }

    private void OnWriteLockAcquired(IAsset asset)
    {
        if (IsDestroyed)
        {
            asset.ReleaseWriteLock(this);
            RunSafeDispose();
            return;
        }

        if (PreferAsyncUpdate)
        {
            Task.Run(_asyncUpdateAction);
        }
        else
        {
            Task.Run(_backgroundUpdateAction);
        }
    }

    private async Task ExecuteAsyncUpdate()
    {
        try
        {
            if (!_hasError && !IsDestroyed)
            {
                await GenerateAssetAsync(Asset);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during dispose
        }
        catch (Exception ex)
        {
            _hasError = true;
            LumoraLogger.Log($"Error in async procedural asset update: {ex.Message}");
            OnGenerationFailed(ex.Message);
        }
        FinishUpdate();
    }

    private void ExecuteBackgroundUpdate()
    {
        if (IsDestroyed)
        {
            Asset?.ReleaseWriteLock(this);
            RunSafeDispose();
            return;
        }

        try
        {
            if (!_hasError)
            {
                GenerateAsset(Asset);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during dispose
        }
        catch (Exception ex)
        {
            _hasError = true;
            LumoraLogger.Log($"Error in procedural asset update: {ex.Message}");
            OnGenerationFailed(ex.Message);
        }
        FinishUpdate();
    }

    private void FinishUpdate()
    {
        Asset?.ReleaseWriteLock(this);

        if (IsDestroyed)
        {
            RunSafeDispose();
            return;
        }

        UploadToRenderer(_integratedCallback);
    }

    private void OnAssetIntegrated(bool isNewInstance)
    {
        OnAssetIntegrationComplete(isNewInstance);

        if (isNewInstance)
        {
            AssetCreated();
        }
        else
        {
            AssetUpdated();
        }

        _completedUpdates++;

        bool lockTaken = false;
        try
        {
            _updateLock.Enter(ref lockTaken);

            if (_pendingUpdate)
            {
                MarkChangeDirty();
                _pendingUpdate = false;
            }
            _isUpdating = false;
        }
        finally
        {
            if (lockTaken) _updateLock.Exit();
        }
    }

    private void RunSafeDispose()
    {
        if (!_safeDisposeComplete)
        {
            _safeDisposeComplete = true;
            OnSafeDispose();
        }
    }

    public void ClearError()
    {
        _hasError = false;
    }

    // Main thread, before the write lock is taken.
    protected abstract void PrepareUpdateState();

    // Background thread, holds the write lock.
    protected abstract void GenerateAsset(A asset);

    protected abstract ValueTask GenerateAssetAsync(A asset);

    protected abstract void UploadToRenderer(AssetIntegratedCallback onComplete);

    protected abstract void OnGenerationFailed(string error);

    protected virtual void OnAssetIntegrationComplete(bool isNewInstance)
    {
    }

    // Cleanup path for a disposal that landed mid-update.
    protected virtual void OnSafeDispose()
    {
    }

    public override void OnDestroy()
    {
        bool lockTaken = false;
        try
        {
            _updateLock.Enter(ref lockTaken);
            if (!_isUpdating)
            {
                RunSafeDispose();
            }
        }
        finally
        {
            if (lockTaken) _updateLock.Exit();
        }
        base.OnDestroy();
    }
}

