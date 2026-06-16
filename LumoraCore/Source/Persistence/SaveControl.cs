// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Persistence;

/// <summary>
/// Context threaded through a world save. Provides GUID-stable reference encoding (via the
/// <see cref="ReferenceTranslator"/>) and collects type versions so a load can migrate older data.
/// </summary>
public sealed class SaveControl
{
    private readonly Dictionary<Type, int> _typeVersions = new();

    public ReferenceTranslator ReferenceTranslator { get; }

    /// <summary>The element the save was rooted at (the world's root slot).</summary>
    public IWorldElement SaveRoot { get; }

    /// <summary>
    /// When false (the default), only persistent content is saved. Graph saves set this true while
    /// writing collected asset dependencies so they're emitted regardless of their slot's persistence.
    /// </summary>
    public bool SaveNonPersistent { get; set; }

    /// <summary>
    /// Optional filter applied to every saved reference. Return <see cref="RefID.Null"/> to drop a
    /// reference. A graph save uses this to null out references pointing outside the saved subtree
    /// (and its collected dependencies), keeping the produced graph self-contained.
    /// </summary>
    public Func<RefID, RefID>? ReferenceFilter { get; set; }

    public SaveControl(IWorldElement saveRoot, ReferenceTranslator referenceTranslator)
    {
        SaveRoot = saveRoot;
        ReferenceTranslator = referenceTranslator;
    }

    /// <summary>Encode a reference as its stable GUID (a null reference becomes a null value).</summary>
    public DataTreeValue SaveReference(RefID reference)
    {
        if (ReferenceFilter != null)
            reference = ReferenceFilter(reference);
        if (reference == RefID.Null)
            return new DataTreeValue((string?)null);
        return DataTreeValue.RawString(ReferenceTranslator.Fetch(reference).ToString());
    }

    /// <summary>Record a type's serialization version so the loader can detect and migrate older data.</summary>
    public void RegisterTypeVersion(Type type, int version)
    {
        if (version > 0)
            _typeVersions.TryAdd(type, version);
    }

    /// <summary>Write the collected type versions into the world dictionary.</summary>
    public void StoreTypeVersions(DataTreeDictionary dictionary)
    {
        foreach (var (type, version) in _typeVersions)
        {
            if (type.FullName != null)
                dictionary.Add(type.FullName, version);
        }
    }
}
