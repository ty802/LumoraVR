// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

// Shared plumbing for undo records that reverse a destroy by serializing what they removed.
//
// The whole scheme rests on ONE ReferenceTranslator living for the life of the undo
// record. Saving mints a GUID for every RefID the snapshot touched, so restoring resolves a
// reference to a still-live element straight back to it.
//
// That same map is a trap on the way back. A GUID minted for an element the operation then
// DESTROYED still resolves - to the dead RefID. Anything restored before its target gets that dead
// ID handed to it instead of waiting, and the reference lands empty. So every identity that is
// about to die is collected while it still exists and forgotten before the restore begins: the GUID
// goes back to unresolved, the request queues, and the rebuilt element's own Associate satisfies
// it. Order inside the restore stops mattering. -xlinka
internal static class UndoSerialization
{
    // every RefID inside a worker: the worker itself, its members, and nested collection
    // elements; collect BEFORE the destroy, feed to Forget before the restore
    public static void CollectIdentities(Worker worker, List<RefID> into)
    {
        if (worker == null)
            return;
        into.Add(worker.ReferenceID);
        for (int i = 0; i < worker.SyncMemberCount; i++)
        {
            var member = worker.GetSyncMember(i);
            if (member != null)
                CollectIdentities(member, into);
        }
    }

    public static void CollectIdentities(ISyncMember member, List<RefID> into)
    {
        if (member == null)
            return;
        into.Add(member.ReferenceID);
        switch (member)
        {
            case ISyncList list:
                foreach (var element in list.Elements)
                {
                    if (element is ISyncMember child)
                        CollectIdentities(child, into);
                }
                break;
            case ISyncObject syncObject:
                foreach (var child in syncObject.SyncMembers)
                    CollectIdentities(child, into);
                break;
        }
    }

    // release the collected identities so their GUIDs resolve against the REBUILT elements
    public static void Forget(ReferenceTranslator translator, List<RefID> ids)
    {
        foreach (var id in ids)
            translator.Forget(id);
    }

    // null when the component can't be captured, which the caller must treat as "not undoable"
    public static DataTreeNode? SaveComponent(Component component, ReferenceTranslator translator)
    {
        try
        {
            var control = new SaveControl(component.Slot, translator) { SaveNonPersistent = true };
            control.ReserveWorkerIdentities(component);
            return component.Save(control);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Undo: snapshot of {component.GetType().Name} failed: {ex.Message}");
            return null;
        }
    }

    public static DataTreeNode? SaveMember(ISyncMember member, IWorldElement? saveRoot, ReferenceTranslator translator)
    {
        var root = saveRoot ?? member as IWorldElement;
        if (root == null)
            return null;
        try
        {
            var control = new SaveControl(root, translator) { SaveNonPersistent = true };
            control.ReserveMemberIdentities(member);
            return member.Save(control);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Undo: snapshot of member '{member.Name}' failed: {ex.Message}");
            return null;
        }
    }
}
