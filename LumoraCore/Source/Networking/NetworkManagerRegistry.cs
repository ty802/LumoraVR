// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking;

/// <summary>
/// Central registry of <see cref="INetworkManager"/> instances. Bootstrap code
/// registers each transport once at engine startup; session code queries the
/// registry to find the right manager for a URI.
///
/// Registration order doesn't matter; lookups consult managers in
/// <see cref="INetworkManager.Priority"/> order, highest first.
/// </summary>
public static class NetworkManagerRegistry
{
    private static readonly object _lock = new();
    private static readonly List<INetworkManager> _managers = new();

    public static IReadOnlyList<INetworkManager> Managers
    {
        get
        {
            lock (_lock)
            {
                return _managers.ToArray();
            }
        }
    }

    public static void Register(INetworkManager manager)
    {
        if (manager == null) throw new ArgumentNullException(nameof(manager));
        lock (_lock)
        {
            if (_managers.Contains(manager)) return;
            // Dedup by TYPE, not just instance: registration creates a fresh manager each call, so a
            // second call would otherwise add a duplicate transport (a second listener on the same
            // scheme). One transport per type. - xlinka
            foreach (var existing in _managers)
            {
                if (existing.GetType() == manager.GetType())
                {
                    LumoraLogger.Warn($"NetworkManagerRegistry: {manager.GetType().Name} already registered - ignoring duplicate");
                    return;
                }
            }
            _managers.Add(manager);
            _managers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
        LumoraLogger.Log($"NetworkManagerRegistry: registered {manager.GetType().Name} (priority {manager.Priority})");
    }

    public static void Unregister(INetworkManager manager)
    {
        if (manager == null) return;
        lock (_lock)
        {
            _managers.Remove(manager);
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _managers.Clear();
        }
    }

    /// <summary>
    /// First registered manager that supports the URI's scheme, in priority
    /// order. Null if no transport handles the scheme.
    /// </summary>
    public static INetworkManager FindForUri(Uri uri)
    {
        if (uri == null) return null!;
        return FindForScheme(uri.Scheme);
    }

    public static INetworkManager FindForScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme)) return null!;
        lock (_lock)
        {
            foreach (var manager in _managers)
            {
                if (manager.SupportsScheme(scheme)) return manager;
            }
        }
        return null!;
    }

    /// <summary>
    /// All distinct schemes any registered manager handles. Used for session
    /// listing filtering and discovery validation.
    /// </summary>
    public static List<string> AllSupportedSchemes()
    {
        var result = new List<string>();
        lock (_lock)
        {
            foreach (var manager in _managers)
            {
                manager.GetSupportedSchemes(result);
            }
        }
        return result;
    }

    /// <summary>
    /// Drive every registered manager. Call once per frame from the engine
    /// update loop.
    /// </summary>
    public static void UpdateAll()
    {
        INetworkManager[] snapshot;
        lock (_lock)
        {
            snapshot = _managers.ToArray();
        }
        for (int i = 0; i < snapshot.Length; i++)
        {
            try
            {
                snapshot[i].Update();
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"NetworkManagerRegistry: {snapshot[i].GetType().Name}.Update threw: {ex}");
            }
        }
    }

    public static void StopAll()
    {
        INetworkManager[] snapshot;
        lock (_lock)
        {
            snapshot = _managers.ToArray();
            _managers.Clear();
        }
        foreach (var manager in snapshot)
        {
            try
            {
                manager.Stop();
                manager.Dispose();
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"NetworkManagerRegistry: {manager.GetType().Name}.Stop threw: {ex}");
            }
        }
    }
}
