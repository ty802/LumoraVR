// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Persistence;

// Context threaded through a world save. Provides GUID-stable reference encoding (via
// ReferenceTranslator) and collects type versions so a load can migrate older data.
public sealed class SaveControl
{
    private readonly Dictionary<Type, int> _typeVersions = new();
    private Dictionary<string, int>? _savedTypeVersions;

    public ReferenceTranslator ReferenceTranslator { get; }

    public IWorldElement SaveRoot { get; }

    // false (default) saves only persistent content; graph saves set this true while writing
    // collected asset dependencies so they're emitted regardless of their slot's persistence
    public bool SaveNonPersistent { get; set; }

    // Optional filter applied to every saved reference; return RefID.Null to drop a reference. A
    // graph save uses this to null out references pointing outside the saved subtree (and its
    // collected dependencies), keeping the produced graph self-contained.
    public Func<RefID, RefID>? ReferenceFilter { get; set; }

    public SaveControl(IWorldElement saveRoot, ReferenceTranslator referenceTranslator)
    {
        SaveRoot = saveRoot;
        ReferenceTranslator = referenceTranslator;
    }

    // a null reference encodes as a null value
    public DataTreeValue SaveReference(RefID reference)
    {
        if (ReferenceFilter != null)
            reference = ReferenceFilter(reference);
        if (reference == RefID.Null)
            return new DataTreeValue((string?)null);
        return DataTreeValue.RawString(ReferenceTranslator.Fetch(reference).ToString());
    }


    // REFERENCE IDENTITY RESERVATION
    // A sync member's saved value carries no identity of its own, so Worker.Save only writes the
    // "<name>-ID" beside it for members something is known to point at. "Known" comes from the
    // translator, which holds an entry only once a reference has ALREADY been written - so a plain ref
    // that happens to serialize AFTER its target leaves that target with no identity in the file and
    // loads back empty. Whether it does is an accident of where the two sit in the tree, which nobody
    // authoring content can see or control.
    //
    // Fix it up front: before a byte is written, walk the references under the save root and claim the
    // GUID for whatever each one points at. Every referenced member then reaches its own Save with an
    // identity already reserved and writes it, whichever side of the referrer it sits on. Only
    // reference TARGETS get claimed, never every member, so the extra keys scale with how many
    // references a world holds rather than with how big it is. -xlinka

    // call once, before saving
    public void ReserveSubtreeIdentities(Slot root)
    {
        if (root == null || !root.IsPersistent)
            return;

        ReserveWorkerIdentities(root);

        foreach (var component in root.Components)
        {
            if (component != null && component.IsPersistent)
                ReserveWorkerIdentities(component);
        }

        foreach (var child in root.Children)
            ReserveSubtreeIdentities(child);
    }

    // for workers written outside a slot walk, e.g. a graph save's collected asset dependencies
    public void ReserveWorkerIdentities(Worker worker)
    {
        if (worker == null)
            return;

        // The reserve walk is the one pass that sees every worker a save is about to write, so the
        // version stamps ride along with it rather than costing a second traversal. Types that declare
        // no version add no entry and no work beyond a memoised lookup. -xlinka
        RegisterTypeVersion(worker.WorkerType, TypeVersioning.GetDeclaredVersion(worker.WorkerType));

        // A preserved component is written back out under the type it came from, so the version stamp
        // that type had in the ORIGINAL file has to be written back out with it. Losing it would leave
        // the build that can read the type reading it as version 0 and migrating data that was already
        // current. -xlinka
        if (worker is UnresolvedComponent { HasPreservedData: true } preserved)
            RegisterSavedTypeVersion(preserved.MissingType.Value, preserved.MissingTypeVersion.Value);

        for (int i = 0; i < worker.SyncMemberCount; i++)
        {
            var member = worker.GetSyncMember(i);
            if (member != null)
                ReserveMemberIdentities(member);
        }
    }

    // for a save rooted at a single member rather than a worker, e.g. an undo record of one list element
    public void ReserveMemberIdentities(ISyncMember member)
    {
        // A member whose value never reaches the tree has nothing to resolve on load either.
        if (member is SyncElement { IsPersistent: false })
            return;

        switch (member)
        {
            case ISyncRef syncRef:
                // The raw RefID, not the resolved target: it is exactly what SyncRef.Save writes, and it
                // costs no registry lookup.
                Reserve(syncRef.Value);
                break;
            case ISyncList list:
                foreach (var element in list.Elements)
                {
                    if (element is ISyncMember child)
                        ReserveMemberIdentities(child);
                }
                break;
            case ISyncObject syncObject:
                foreach (var child in syncObject.SyncMembers)
                    ReserveMemberIdentities(child);
                break;
        }
    }

    private void Reserve(RefID target)
    {
        if (target == RefID.Null)
            return;

        // A graph save nulls references that leave the subtree, and nothing they point at gets written,
        // so reserving for them would burn GUIDs on elements the file never mentions.
        if (ReferenceFilter != null && ReferenceFilter(target) == RefID.Null)
            return;

        ReferenceTranslator.Fetch(target);
    }

    // lets the loader detect and migrate older serialized data
    public void RegisterTypeVersion(Type type, int version)
    {
        if (version > 0)
            _typeVersions.TryAdd(type, version);
    }

    // For a stamp that has no local Type behind it - a preserved component carrying forward the version
    // its own file recorded.
    public void RegisterSavedTypeVersion(string typeName, int version)
    {
        if (version <= 0 || string.IsNullOrEmpty(typeName))
            return;
        _savedTypeVersions ??= new Dictionary<string, int>(StringComparer.Ordinal);
        _savedTypeVersions.TryAdd(typeName, version);
    }

    public void StoreTypeVersions(DataTreeDictionary dictionary)
    {
        foreach (var (type, version) in _typeVersions)
        {
            if (type.FullName != null)
                dictionary.AddOrUpdate(type.FullName, version);
        }

        if (_savedTypeVersions == null)
            return;

        foreach (var (typeName, version) in _savedTypeVersions)
        {
            // A live type that resolves under the same name has already spoken for it; the carried
            // stamp only fills names this build has nothing for.
            if (!dictionary.ContainsKey(typeName))
                dictionary.Add(typeName, version);
        }
    }
}
