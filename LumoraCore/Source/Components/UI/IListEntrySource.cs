// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Listing;

namespace Lumora.Core.Components.UI;

// One thing a list shows: a key that stays the same while it is listed, the name shown for it, and what it
// stands for, which a row writes into a reference for its own parts to read.
public readonly struct ListEntry
{
    public readonly string Key;
    public readonly string Name;
    public readonly IWorldElement? Entity;

    public ListEntry(string key, string name, IWorldElement? entity)
    {
        Key = key ?? string.Empty;
        Name = name ?? string.Empty;
        Entity = entity;
    }
}

// What a template list reads. Entries come back in the order they are listed in. A change after that is
// raised with the entry and what happened to it, unfiltered: the list applies its own search through
// Matches. Reset means nothing listed so far can be trusted and the list reads everything again.
public interface IListEntrySource : IWorldElement
{
    event Action<ListEntry, ListingChange>? EntriesChanged;

    void CollectEntries(string search, List<ListEntry> into);

    bool Matches(in ListEntry entry, string search);
}
