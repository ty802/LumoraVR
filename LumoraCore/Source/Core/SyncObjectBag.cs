// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

/// <summary>
/// An unordered, network-synchronized collection of world-element members - each entry is a real
/// sub-worker with its own RefID that syncs and persists itself (contrast with <see cref="SyncBag{T}"/>,
/// which stores plain values). Built on the element-list machinery; element creation is owned by the
/// collection (use <see cref="SyncElementList{T}.Add()"/>), so order carries no meaning.
/// </summary>
public class SyncObjectBag<T> : SyncElementList<T>, IEnumerable<T>, IEnumerable where T : class, ISyncMember, new()
{
    public Enumerator GetEnumerator() => GetElementsEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
