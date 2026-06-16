// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Persistence;

/// <summary>
/// Context threaded through a world load. Resolves GUID references back to the freshly-allocated
/// local <see cref="RefID"/>s (via the <see cref="ReferenceTranslator"/>), exposes saved type
/// versions for migration, and runs deferred post-load actions once the whole tree is in place.
/// </summary>
public sealed class LoadControl
{
    private readonly Dictionary<Type, int> _typeVersions = new();
    private readonly List<(int order, Action action)> _onLoaded = new();

    public World World { get; }
    public ReferenceTranslator ReferenceTranslator { get; }

    public LoadControl(World world, ReferenceTranslator referenceTranslator)
    {
        World = world;
        ReferenceTranslator = referenceTranslator;
    }

    /// <summary>Bind a rebuilt element's new local RefID to the GUID it was saved under.</summary>
    public void AssociateReference(RefID localReference, DataTreeNode globalReference)
    {
        if (localReference == RefID.Null || globalReference is not DataTreeValue value || value.IsNull)
            return;
        if (Guid.TryParse(value.Extract<string>(), out var global))
            ReferenceTranslator.Associate(localReference, global);
    }

    /// <summary>Resolve a reference to its saved target, deferring until that target loads if needed.</summary>
    public void RequestReference(DataTreeNode globalReference, ISyncRef requestee)
    {
        if (globalReference is not DataTreeValue value || value.IsNull)
        {
            requestee.Value = RefID.Null;
            return;
        }
        if (Guid.TryParse(value.Extract<string>(), out var global))
            ReferenceTranslator.Request(global, requestee);
        else
            requestee.Value = RefID.Null;
    }

    public void LoadTypeVersions(DataTreeDictionary dictionary)
    {
        foreach (var (key, node) in dictionary.Children)
        {
            var type = Type.GetType(key);
            if (type != null && node is DataTreeValue value)
                _typeVersions[type] = value.Extract<int>();
        }
    }

    public int GetTypeVersion<T>() => GetTypeVersion(typeof(T));

    public int GetTypeVersion(Type type)
        => _typeVersions.TryGetValue(type, out var version) ? version : 0;

    /// <summary>Queue an action to run after the full tree has loaded (lower order runs first).</summary>
    public void OnLoaded(Action action, int order = 0) => _onLoaded.Add((order, action));

    /// <summary>Run deferred post-load actions and report any references that never resolved.</summary>
    internal void FinishLoad()
    {
        foreach (var (_, action) in _onLoaded.OrderBy(entry => entry.order))
        {
            try { action(); }
            catch (Exception ex) { LumoraLogger.Error($"LoadControl: post-load action failed: {ex.Message}"); }
        }

        var unresolved = ReferenceTranslator.TakeUnresolved();
        if (unresolved.Count > 0)
        {
            int total = unresolved.Sum(entry => entry.Value.Count);
            LumoraLogger.Warn($"LoadControl: {total} reference(s) across {unresolved.Count} target(s) never resolved.");
        }
    }
}
