// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;

namespace Lumora.Core.Networking.Sync;

public delegate void SyncListElementsEvent<T>(SyncElementList<T> list, int index, int count) where T : class, ISyncMember, new();

public delegate void SyncListElementsEvent(ISyncList list, int index, int count);

public delegate void SyncListEvent(ISyncList list);

public interface ISyncList : ISyncMember
{
    int Count { get; }

    IEnumerable Elements { get; }

    ISyncMember GetElement(int index);

    ISyncMember AddElement();

    // Undo needs this to put a removed element back where it was; without it a restore could only append.
    ISyncMember InsertElement(int index);

    void RemoveElement(int index);

    event SyncListElementsEvent ElementsAdded;

    event SyncListElementsEvent ElementsRemoved;

    event SyncListEvent ListCleared;
}
