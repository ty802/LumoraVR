// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking;

/// <summary>
/// Platform-agnostic async asset fetcher.
///
/// Supported URI schemes
///   local://machineId/hash  — served from LocalDB; if not held locally, requested
///                             from the active session peer via SessionAssetTransferer.
///   file:///abs/path        — direct disk read.
///   http:// / https://      — HTTP download.
///   builtin://…             — built-in engine assets.
///   test://…                — local test assets (editor/dev only).
/// </summary>
public static class AssetFetcher
{
    private static readonly HttpClient Http = new();

    // In-flight tasks keyed by URI string: (task, pending callbacks)
    private static readonly Dictionary<string, (Task<byte[]> task, List<Action<byte[]>> callbacks)> _active = new();

    /// <summary>
    /// Asynchronously fetch an asset. Calls <paramref name="callback"/> with the
    /// raw bytes on the calling thread once the fetch task completes (via ProcessQueue).
    /// Returns immediately; duplicate requests share a single in-flight task.
    /// </summary>
    public static void FetchAsset(string uri, Action<byte[]> callback)
    {
        // ── local:// — may need peer-to-peer transfer ──────────────────────────
        if (uri.StartsWith("local://", StringComparison.Ordinal))
        {
            FetchLocalAsset(uri, callback);
            return;
        }

        // ── Coalesce duplicate in-flight requests ──────────────────────────────
        if (_active.TryGetValue(uri, out var existing))
        {
            existing.callbacks.Add(callback);
            return;
        }

        var task = Task.Run(() => FetchSync(uri));
        _active[uri] = (task, [callback]);
    }

    /// <summary>
    /// Process completed fetch tasks and invoke their callbacks.
    /// Must be called every frame by the platform driver update loop.
    /// </summary>
    public static void ProcessQueue()
    {
        if (_active.Count == 0)
            return;

        var done = new List<string>();
        foreach (var (uri, (task, callbacks)) in _active)
        {
            if (!task.IsCompleted)
                continue;

            byte[] result = task.IsCompletedSuccessfully ? task.Result : null;
            foreach (var cb in callbacks)
            {
                try { cb(result); }
                catch (Exception ex) { LumoraLogger.Error($"AssetFetcher: callback exception for '{uri}': {ex.Message}"); }
            }
            done.Add(uri);
        }

        foreach (var uri in done)
            _active.Remove(uri);
    }

    // ── local:// ──────────────────────────────────────────────────────────────

    private static void FetchLocalAsset(string uri, Action<byte[]> callback)
    {
        var engine = Engine.Current;
        var localDB = engine?.LocalDB;

        // 1. Served from our own LocalDB?
        if (localDB != null)
        {
            var filePath = localDB.GetFilePath(uri);
            if (filePath != null && File.Exists(filePath))
            {
                // Read async so we don't block the main thread
                var task = Task.Run(() => File.ReadAllBytes(filePath));
                _active[uri] = (task, [callback]);
                return;
            }
        }

        // 2. Request from session peer
        var transferer = engine?.ActiveSessionTransferer;
        if (transferer != null)
        {
            LumoraLogger.Log($"AssetFetcher: requesting remote asset '{uri}' from session");
            transferer.RequestAsset(new Uri(uri), (assetUri, localPath) =>
            {
                if (localPath == null)
                {
                    LumoraLogger.Warn($"AssetFetcher: peer could not provide '{uri}'");
                    callback(null);
                    return;
                }

                try
                {
                    var data = File.ReadAllBytes(localPath);

                    // Cache into LocalDB so future requests are local
                    if (localDB != null)
                        _ = localDB.ImportLocalAssetAsync(localPath, LocalDB.ImportLocation.Move);

                    callback(data);
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"AssetFetcher: error reading received asset '{localPath}': {ex.Message}");
                    callback(null);
                }
            });
            return;
        }

        LumoraLogger.Warn($"AssetFetcher: '{uri}' not in LocalDB and no active session — cannot fetch");
        callback(null);
    }

    // ── Synchronous fetcher for non-local URIs ────────────────────────────────

    private static byte[] FetchSync(string uri)
    {
        try
        {
            if (BuiltinAssetHelper.ValidPath(uri))
                return BuiltinAssetHelper.GetBuiltinAssetData(uri);

            if (LocalTestAssetHelper.ValidPath(uri))
                return LocalTestAssetHelper.GetLocalTestAssetData(uri);

            var u = new Uri(uri);

            if (u.Scheme == Uri.UriSchemeFile)
                return File.ReadAllBytes(u.LocalPath);

            if (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                return Http.GetByteArrayAsync(u).GetAwaiter().GetResult();

            LumoraLogger.Warn($"AssetFetcher: unsupported URI scheme '{u.Scheme}' in '{uri}'");
            return null;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"AssetFetcher: failed to fetch '{uri}': {ex.Message}");
            return null;
        }
    }
}
