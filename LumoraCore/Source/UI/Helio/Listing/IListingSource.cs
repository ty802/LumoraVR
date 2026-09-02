// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Helio.UI.Listing;

// What the view asks a source for. Path is the category stack (empty = root), Search is pushed down
// so a source backed by a query can filter at the far end instead of shipping everything back, and
// Offset/Count are a page window. A list-backed source ignores the window and hands back the lot. -xlinka
public readonly struct ListingQuery
{
    public static readonly IReadOnlyList<string> RootPath = Array.Empty<string>();

    public readonly IReadOnlyList<string> Path;
    public readonly string Search;
    public readonly int Offset;
    // 0 means "everything from Offset on".
    public readonly int Count;

    public ListingQuery(IReadOnlyList<string>? path, string? search, int offset = 0, int count = 0)
    {
        Path = path ?? RootPath;
        Search = search ?? string.Empty;
        Offset = offset < 0 ? 0 : offset;
        Count = count < 0 ? 0 : count;
    }

    public ListingQuery Window(int offset, int count) => new ListingQuery(Path, Search, offset, count);

    public bool SamePathAndSearch(in ListingQuery other)
        => Search == other.Search && PathsEqual(Path, other.Path);

    public static bool PathsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

public enum ListingFetch
{
    // The requested window is in the buffer (possibly short, which means end of data).
    Ready,
    // Source is still resolving. It raises Changed when the page lands; the view retries then. This is
    // the whole reason Fetch isn't a plain "give me a list" call: a cloud-backed source can say
    // "not yet" without the view having to grow an await. -xlinka
    Pending,
}

public interface IListingSource
{
    event Action<IListingSource, ListingChangeEvent>? Changed;

    // Total items for the path+search, or -1 when the source cannot know until it has paged through.
    // The view sizes its scroll extent off this and falls back to what it has materialized.
    int TotalCount(in ListingQuery query);

    // Append the requested window to page. Never clears it: the view owns that buffer.
    ListingFetch Fetch(in ListingQuery query, List<ListingItem> page);
}

public static class ListingSearch
{
    public static bool Matches(ListingItem item, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        return Contains(item.Label, search)
            || Contains(item.Detail, search)
            || Contains(item.Key, search);
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
