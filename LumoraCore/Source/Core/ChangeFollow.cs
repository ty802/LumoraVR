// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

// What a component's work reads but does not own: the field a reference of it points at, the fields its
// drives hold, the entries of a list of it. Every change heard marks the component changed, so its OnChanges
// runs in this frame's change pass, and a driver built on this costs nothing on a frame where nothing it
// reads moved instead of a turn in the per-frame update.
//
// The owner says what it follows each time it works: Begin, then Add, AddRef and AddList for each, then End.
// Following the same things as last time costs a compare. A reference is heard when it comes to point at
// something late, which a field's change event does not say: on a join the thing it names can arrive after
// the reference does. A list is heard as entries come and go; each entry that is a field or a reference is
// followed too. What the owner writes itself between BeginQuiet and EndQuiet is not heard, so following the
// field it drives only hears somebody else writing there, and the drive wins again at once. -xlinka
public sealed class ChangeFollow
{
    private interface IRefWatch
    {
        void Stop();
    }

    private sealed class RefWatch<T> : IRefWatch where T : class, IWorldElement
    {
        private readonly SyncRef<T> _reference;
        private readonly ReferenceEvent<T> _available;

        public RefWatch(SyncRef<T> reference, Action<IChangeable?> heard)
        {
            _reference = reference;
            _available = _ => heard(null);
            reference.OnObjectAvailable += _available;
        }

        public void Stop() => _reference.OnObjectAvailable -= _available;
    }

    private readonly ComponentBase<Component> _owner;
    private readonly Action<IChangeable> _heard;
    private readonly Action<IChangeable?> _heardAny;
    private readonly SyncListElementsEvent _listChanged;
    private readonly SyncListEvent _listCleared;
    private List<IChangeable> _followed = new();
    private List<IChangeable> _next = new();
    private readonly List<ISyncList> _lists = new();
    private readonly List<ISyncList> _nextLists = new();
    private readonly Dictionary<object, IRefWatch> _refs = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _nextRefs = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _gone = new();
    private int _quiet;

    public ChangeFollow(ComponentBase<Component> owner)
    {
        _owner = owner;
        _heard = Heard;
        _heardAny = Heard;
        _listChanged = (_, _, _) => Heard(null);
        _listCleared = _ => Heard(null);
    }

    private void Heard(IChangeable? _)
    {
        if (_quiet == 0 && !_owner.IsDestroyed)
            _owner.MarkChangeDirty();
    }

    public void BeginQuiet() => _quiet++;

    public void EndQuiet()
    {
        if (_quiet > 0)
            _quiet--;
    }

    public void Begin()
    {
        _next.Clear();
        _nextLists.Clear();
        _nextRefs.Clear();
    }

    public void Add(IChangeable? element)
    {
        if (element != null && !element.IsDestroyed && !_next.Contains(element))
            _next.Add(element);
    }

    // The reference itself, the moment it resolves, and the element it points at now.
    public void AddRef<T>(SyncRef<T>? reference) where T : class, IWorldElement
    {
        if (reference == null)
            return;
        Add(reference);
        if (_nextRefs.Add(reference) && !_refs.ContainsKey(reference))
            _refs[reference] = new RefWatch<T>(reference, _heardAny);
        Add(reference.Target as IChangeable);
    }

    // The list's entries coming and going, and each entry that is itself a changeable member.
    public void AddList(ISyncList? list)
    {
        if (list == null || _nextLists.Contains(list))
            return;
        _nextLists.Add(list);
        for (int i = 0; i < list.Count; i++)
            Add(list.GetElement(i) as IChangeable);
    }

    public void End()
    {
        if (!Same(_followed, _next))
        {
            foreach (var old in _followed)
            {
                if (!_next.Contains(old))
                    old.Changed -= _heard;
            }
            foreach (var added in _next)
            {
                if (!_followed.Contains(added))
                    added.Changed += _heard;
            }
            (_followed, _next) = (_next, _followed);
        }
        _next.Clear();

        if (!Same(_lists, _nextLists))
        {
            foreach (var old in _lists)
            {
                if (_nextLists.Contains(old))
                    continue;
                old.ElementsAdded -= _listChanged;
                old.ElementsRemoved -= _listChanged;
                old.ListCleared -= _listCleared;
            }
            foreach (var added in _nextLists)
            {
                if (_lists.Contains(added))
                    continue;
                added.ElementsAdded += _listChanged;
                added.ElementsRemoved += _listChanged;
                added.ListCleared += _listCleared;
            }
            _lists.Clear();
            _lists.AddRange(_nextLists);
        }
        _nextLists.Clear();

        if (_refs.Count != _nextRefs.Count)
        {
            _gone.Clear();
            foreach (var (reference, watch) in _refs)
            {
                if (_nextRefs.Contains(reference))
                    continue;
                watch.Stop();
                _gone.Add(reference);
            }
            foreach (var reference in _gone)
                _refs.Remove(reference);
            _gone.Clear();
        }
        _nextRefs.Clear();
    }

    public void Clear()
    {
        Begin();
        End();
    }

    private static bool Same<T>(List<T> a, List<T> b) where T : class
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
                return false;
        }
        return true;
    }
}
