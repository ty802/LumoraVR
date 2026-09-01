// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Persistence;

// What a saved worker type resolved to, with its member data after any migration transform. A null
// Type means nothing in this build can load it.
public readonly struct ResolvedWorkerType
{
    public Type? Type { get; }
    public DataTreeNode Data { get; }

    public ResolvedWorkerType(Type? type, DataTreeNode data)
    {
        Type = type;
        Data = data;
    }
}

// Context threaded through a world load. Resolves GUID references back to the freshly-allocated local
// RefIDs (via the ReferenceTranslator), exposes saved type versions for migration, and runs deferred
// post-load actions once the whole tree is in place.
public sealed class LoadControl
{
    private readonly Dictionary<Type, int> _typeVersions = new();

    // The same numbers keyed by the name the FILE used. Needed because the two questions are different:
    // a type's own Load asks "what version am I reading" and gets it by type, while type replacement
    // asks "what version was this name written at" before there is a type at all - including for names
    // that no longer resolve to anything. -xlinka
    private Dictionary<string, int>? _savedTypeVersions;

    private readonly List<(int order, Action action)> _onLoaded = new();

    public World World { get; }
    public ReferenceTranslator ReferenceTranslator { get; }

    public LoadControl(World world, ReferenceTranslator referenceTranslator)
    {
        World = world;
        ReferenceTranslator = referenceTranslator;
    }

    // Bind a rebuilt element's new local RefID to the GUID it was saved under.
    public void AssociateReference(RefID localReference, DataTreeNode globalReference)
    {
        if (localReference == RefID.Null || globalReference is not DataTreeValue value || value.IsNull)
            return;
        if (Guid.TryParse(value.Extract<string>(), out var global))
            ReferenceTranslator.Associate(localReference, global);
    }

    // Resolve a reference to its saved target, deferring until that target loads if needed.
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
            if (node is not DataTreeValue value)
                continue;
            int version = value.Extract<int>();

            _savedTypeVersions ??= new Dictionary<string, int>(StringComparer.Ordinal);
            _savedTypeVersions[key] = version;

            // Quiet resolution: a version entry for a type this build does not have is a normal thing
            // to find in a file and must not shout. The component itself reports what happened when it
            // reaches the load path.
            if (WorkerManager.TryGetType(key, out var type))
            {
                // A rename can point two saved names at one type. The higher version wins; a loader
                // that needs to tell the two apart reads the name it cares about directly.
                if (!_typeVersions.TryGetValue(type, out var existing) || version > existing)
                    _typeVersions[type] = version;
            }
        }
    }

    public int GetTypeVersion<T>() => GetTypeVersion(typeof(T));

    public int GetTypeVersion(Type type)
        => _typeVersions.TryGetValue(type, out var version) ? version : 0;

    // The version the FILE stamped against a type name, whether or not that name still resolves. 0 for
    // a name the file never stamped, which is every type in every save written before it declared a
    // [SaveTypeVersion].
    public int GetSavedTypeVersion(string savedTypeName)
    {
        if (_savedTypeVersions != null && savedTypeName != null
            && _savedTypeVersions.TryGetValue(savedTypeName, out var version))
        {
            return version;
        }
        return 0;
    }

    // TYPE REPLACEMENT
    // Checked BEFORE the rename alias map, so "this class was folded into that one" wins over "this
    // class got a new name" and the two never have to know about each other.

    public Type? ResolveReplacement(string savedTypeName, int savedVersion, out Func<DataTreeNode, DataTreeNode>? transformData)
    {
        transformData = null;
        if (!TypeMigrations.HasAny || string.IsNullOrEmpty(savedTypeName))
            return null;
        if (!TypeMigrations.TryResolve(savedTypeName, savedVersion, out var migration))
            return null;

        transformData = migration.TransformData;
        return migration.ReplacementTypeName != null && WorkerManager.TryGetType(migration.ReplacementTypeName, out var replacement)
            ? replacement
            : null;
    }

    // Full resolution for one saved worker: migrations first, then the ordinary name lookup (which
    // carries the rename aliases), with the migration's data transform applied on the way through.
    public ResolvedWorkerType ResolveReplacement(string savedTypeName, DataTreeNode data)
        => Resolve(savedTypeName, data, this);

    // For a load with no control in reach. Version-ranged migrations see 0, which is what an
    // unstamped save carries anyway.
    internal static ResolvedWorkerType ResolveWithoutVersion(string savedTypeName, DataTreeNode data)
        => Resolve(savedTypeName, data, null);

    private static ResolvedWorkerType Resolve(string savedTypeName, DataTreeNode data, LoadControl? control)
    {
        var name = savedTypeName;
        var node = data;

        // The version is read once, and only if the name is registered at all, so a load of content
        // with nothing to migrate pays a single dictionary miss per worker and nothing else.
        int savedVersion = -1;

        // Bounded: A -> B -> C is a real migration chain, A -> B -> A is a registration bug and must
        // not spin.
        for (int step = 0; step < 8; step++)
        {
            if (!TypeMigrations.TryGetEntries(name, out var candidates))
                break;

            if (savedVersion < 0)
                savedVersion = control?.GetSavedTypeVersion(savedTypeName) ?? 0;

            if (!TypeMigrations.TryPick(candidates, savedVersion, out var migration))
                break;

            if (migration.TransformData != null)
                node = migration.TransformData(node) ?? node;

            if (migration.ReplacementTypeName == null || migration.ReplacementTypeName == name)
                break;

            // The version travels with the chain: it is what the file stamped for the name it actually
            // wrote, and a later link in the chain was never in that file to be stamped.
            name = migration.ReplacementTypeName;
        }

        WorkerManager.TryGetType(name, out var type);
        return new ResolvedWorkerType(type, node);
    }

    // Queue an action to run after the full tree has loaded (lower order runs first).
    public void OnLoaded(Action action, int order = 0) => _onLoaded.Add((order, action));

    // Run deferred post-load actions and report any references that never resolved.
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
