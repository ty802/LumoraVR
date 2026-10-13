// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Helio.UI.Listing;

namespace Lumora.Core.Components.UI;

// A list a world builds out of its own slots: one copy of a template per entry of a source, under a container
// whose own layout places them. The template stays switched off; each copy is switched on, left out of saves
// and ordered by when its entry came in. A TemplateListRow on the template writes the entry into the copy.
// Nothing is recycled or skipped: every listed entry has its row.
//
// One machine builds, the builder (empty means the host), and everyone else gets its rows as ordinary
// replicated slots. It reads the whole source again, clearing the container first, when the source,
// template, container, search or builder changes and once after loading; between those it follows the
// source one entry at a time. Rows are made a few milliseconds a frame, so a long list spreads over updates
// instead of stalling one.
//
// A guest may not take away what the host made, so clearing is split by owner: a machine that stops
// building takes its own rows away, and a builder clears only what it may (the host clears everything). -xlinka
[ComponentCategory("UI")]
public class TemplateList : Component
{
    public readonly SyncRef<IListEntrySource> Source;

    // Copied per entry. Keep it switched off and outside the container.
    public readonly SyncRef<Slot> Template;

    // Every child is the list's own: a rebuild clears it.
    public new readonly SyncRef<Slot> Container;

    // Trimmed before use; empty lists everything.
    public readonly Sync<string> Search;

    // The user whose machine makes the rows. Empty, or someone who has left, means the host.
    public readonly SyncRef<User> Builder;

    private const double BuildBudgetMs = 4.0;

    private readonly Dictionary<string, Slot> _rows = new(StringComparer.Ordinal);
    private readonly Queue<(ListEntry Entry, ListingChange Change)> _pending = new();
    private readonly List<ListEntry> _collected = new();
    private IListEntrySource? _source;
    private Slot? _template;
    private Slot? _container;
    private string _search = string.Empty;
    private bool _built;
    private bool _drainQueued;
    private bool _waitQueued;
    private long _nextOrder;
    private World? _followed;

    public TemplateList()
    {
        Source = new SyncRef<IListEntrySource>(this);
        Template = new SyncRef<Slot>(this);
        Container = new SyncRef<Slot>(this);
        Search = new Sync<string>(this, string.Empty);
        Builder = new SyncRef<User>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        _followed = World;
        if (_followed != null)
            _followed.UserRemoved += OnUserRemoved;
    }

    public override void OnDestroy()
    {
        if (_followed != null)
            _followed.UserRemoved -= OnUserRemoved;
        _followed = null;
        Forget();
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        var source = Source.Target;
        var template = Template.Target;
        var container = Container.Target;
        if (!BuildsHere())
        {
            RemoveOwnRows();
            Forget();
            return;
        }
        if (template == null || container == null)
        {
            Forget();
            return;
        }
        // Nothing is copied before the world runs: one still coming back may not hold the whole template yet.
        if (World.State != World.WorldState.Running)
        {
            if (!_waitQueued)
            {
                _waitQueued = true;
                RunInUpdates(1, () =>
                {
                    _waitQueued = false;
                    MarkChangeDirty();
                });
            }
            return;
        }
        string search = Search.Value?.Trim() ?? string.Empty;
        if (_built && ReferenceEquals(source, _source) && template == _template && container == _container && search == _search)
            return;
        Rebuild(source, template, container, search);
    }

    private bool BuildsHere()
    {
        var world = World;
        if (world == null)
            return false;
        var chosen = Builder.Target;
        if (chosen is { IsDestroyed: false, IsDisposed: false })
            return chosen == world.LocalUser;
        return world.IsAuthority;
    }

    // The builder leaving hands the list to the host, which hears nothing from the reference itself.
    private void OnUserRemoved(User user)
    {
        if (user != null && ReferenceEquals(Builder.RawTarget, user))
            MarkChangeDirty();
    }

    private void Rebuild(IListEntrySource? source, Slot template, Slot container, string search)
    {
        Listen(source);
        _template = template;
        _container = container;
        _search = search;
        _built = true;
        _pending.Clear();
        _rows.Clear();
        _nextOrder = 0;
        if (!container.IsDestroyed && container != template && !container.IsDescendantOf(template))
            container.DestroyChildren(MayRemove);
        if (source == null)
            return;
        try
        {
            source.CollectEntries(search, _collected);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"TemplateList on {ParentHierarchyToString()}: reading its source failed: {ex.Message}");
        }
        foreach (var entry in _collected)
            _pending.Enqueue((entry, ListingChange.Added));
        _collected.Clear();
        Drain();
    }

    private bool MayRemove(Slot row)
    {
        var world = World;
        if (world == null || row.IsDestroyed)
            return false;
        if (world.IsAuthority)
            return true;
        var local = world.LocalUser;
        return local != null && RefIDConstants.IsValidUserByte(local.AllocationID.Value)
            && row.ReferenceID.GetUserByte() == local.AllocationID.Value;
    }

    private void RemoveOwnRows()
    {
        if (!_built)
            return;
        foreach (var row in _rows.Values)
        {
            if (row != null && MayRemove(row))
                row.Destroy();
        }
    }

    private void Forget()
    {
        Listen(null);
        _built = false;
        _template = null;
        _container = null;
        _rows.Clear();
        _pending.Clear();
    }

    private void Listen(IListEntrySource? source)
    {
        if (ReferenceEquals(source, _source))
            return;
        if (_source != null)
            _source.EntriesChanged -= OnEntriesChanged;
        _source = source;
        if (_source != null)
            _source.EntriesChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(ListEntry entry, ListingChange change)
    {
        if (!_built || IsDestroyed)
            return;
        if (change == ListingChange.Reset)
        {
            _built = false;
            MarkChangeDirty();
            return;
        }
        _pending.Enqueue((entry, change));
        Drain();
    }

    private void Drain()
    {
        if (IsDestroyed || !_built)
        {
            _pending.Clear();
            return;
        }
        long start = Stopwatch.GetTimestamp();
        while (_pending.Count > 0)
        {
            var (entry, change) = _pending.Dequeue();
            Apply(in entry, change);
            if (_pending.Count > 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds >= BuildBudgetMs)
            {
                if (!_drainQueued)
                {
                    _drainQueued = true;
                    RunInUpdates(1, () =>
                    {
                        _drainQueued = false;
                        Drain();
                    });
                }
                return;
            }
        }
    }

    private void Apply(in ListEntry entry, ListingChange change)
    {
        bool matches = _source == null || _source.Matches(in entry, _search);
        bool listed = _rows.TryGetValue(entry.Key, out var row) && row is { IsDestroyed: false };
        switch (change)
        {
            case ListingChange.Removed:
                Remove(entry.Key);
                break;
            case ListingChange.Added:
            case ListingChange.Updated:
                if (listed && matches)
                    Write(row!, in entry);
                else if (listed)
                    Remove(entry.Key);
                else if (matches)
                    Add(in entry);
                break;
        }
    }

    private void Add(in ListEntry entry)
    {
        var template = _template;
        var container = _container;
        if (template == null || template.IsDestroyed || container == null || container.IsDestroyed
            || container == template || container.IsDescendantOf(template))
            return;
        Slot row;
        try
        {
            row = template.Duplicate(container);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"TemplateList on {ParentHierarchyToString()}: could not copy its template: {ex.Message}");
            return;
        }
        if (row == null || row.IsDestroyed)
            return;
        row.Persistent.Value = false;
        row.OrderOffset.Value = _nextOrder++;
        _rows[entry.Key] = row;
        Write(row, in entry);
        row.ActiveSelf.Value = true;
    }

    private static void Write(Slot row, in ListEntry entry)
        => row.GetComponentInChildren<TemplateListRow>()?.Write(in entry);

    private void Remove(string key)
    {
        if (_rows.Remove(key, out var row) && row != null && !row.IsDestroyed)
            row.Destroy();
    }
}
