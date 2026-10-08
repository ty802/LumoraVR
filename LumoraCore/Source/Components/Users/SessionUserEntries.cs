// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Listing;
using Lumora.Core.Components.UI;

namespace Lumora.Core.Components;

// The people in this session, one entry each in the order they joined, for a template list to show. An
// entry's key is the user's reference id, its name the user's name and its element the user. Nothing else
// about them is handed out: whatever the row shows beyond the name it reads from the user itself.
//
// Followed on every machine, so a list built by a guest sees joins and leaves as well as one built by the
// host. A rename is raised as an update; on a guest that includes the name arriving after the user record.
// Search matches the name, case ignored, anywhere in it. -xlinka
[ComponentCategory("Users")]
public class SessionUserEntries : Component, IListEntrySource
{
    public event Action<ListEntry, ListingChange>? EntriesChanged;

    private readonly List<User> _scratch = new();
    private readonly Dictionary<User, Action<string>> _nameWatches = new(ReferenceEqualityComparer.Instance);
    private World? _followed;

    public override void OnStart()
    {
        base.OnStart();
        Follow(World);
    }

    public override void OnDestroy()
    {
        Follow(null);
        base.OnDestroy();
    }

    public void CollectEntries(string search, List<ListEntry> into)
    {
        var world = World;
        if (world == null || into == null)
            return;
        search = search?.Trim() ?? string.Empty;
        world.GetAllUsers(_scratch);
        foreach (var user in _scratch)
        {
            if (user == null || user.IsDisposed)
                continue;
            var entry = EntryOf(user);
            if (Matches(entry, search))
                into.Add(entry);
        }
        _scratch.Clear();
    }

    public bool Matches(in ListEntry entry, string search)
    {
        search = search?.Trim() ?? string.Empty;
        return search.Length == 0 || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static ListEntry EntryOf(User user) => new(user.ReferenceID.ToString(), user.UserName.Value ?? string.Empty, user);

    private void Follow(World? world)
    {
        if (_followed != null)
        {
            _followed.UserAdded -= OnUserAdded;
            _followed.UserRemoved -= OnUserRemoved;
            foreach (var (user, watch) in _nameWatches)
                user.UserName.OnChanged -= watch;
            _nameWatches.Clear();
        }
        _followed = world;
        if (world == null)
            return;
        world.UserAdded += OnUserAdded;
        world.UserRemoved += OnUserRemoved;
        world.GetAllUsers(_scratch);
        foreach (var user in _scratch)
            WatchName(user);
        _scratch.Clear();
    }

    private void WatchName(User user)
    {
        if (user == null || _nameWatches.ContainsKey(user))
            return;
        Action<string> watch = _ => Raise(user, ListingChange.Updated);
        _nameWatches[user] = watch;
        user.UserName.OnChanged += watch;
    }

    private void OnUserAdded(User user)
    {
        WatchName(user);
        Raise(user, ListingChange.Added);
    }

    private void OnUserRemoved(User user)
    {
        if (user != null && _nameWatches.Remove(user, out var watch))
            user.UserName.OnChanged -= watch;
        Raise(user, ListingChange.Removed);
    }

    private void Raise(User? user, ListingChange change)
    {
        if (user == null || IsDestroyed)
            return;
        try
        {
            EntriesChanged?.Invoke(EntryOf(user), change);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"SessionUserEntries on {ParentHierarchyToString()}: a listener failed on {change}: {ex.Message}");
        }
    }
}
