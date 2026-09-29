// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Lumora.Script;

// A JSON object. Keys keep the order they were first set in, so its text comes out the same on every
// machine, and a key set again keeps its place. Looking up, setting or removing a key takes time in the
// log of how many there are, and an edit shares every member it did not touch with the object it came from.
// Each key has a place number that only grows, so removing one never moves the rest.
public sealed class ScriptJsonObject : ScriptJson
{
    private readonly record struct Member(string Key, ScriptJson Value);

    private static readonly ImmutableDictionary<string, long> NoPlaces = ImmutableDictionary.Create<string, long>(StringComparer.Ordinal);

    public static readonly ScriptJsonObject Empty = new(ScriptJsonValue.Shared, NoPlaces, ImmutableSortedDictionary<long, Member>.Empty, 0, 0, 1, 1);

    private readonly ImmutableDictionary<string, long> _places;
    private readonly ImmutableSortedDictionary<long, Member> _members;
    private readonly long _nextPlace;
    private readonly long _keyChars;
    private readonly long _size;
    private readonly int _depth;

    private ScriptJsonObject(ScriptCollectionBudget budget, ImmutableDictionary<string, long> places,
        ImmutableSortedDictionary<long, Member> members, long nextPlace, long keyChars, long size, int depth) : base(budget)
    {
        CheckDepth(depth);
        _places = places;
        _members = members;
        _nextPlace = nextPlace;
        _keyChars = keyChars;
        _size = size;
        _depth = depth;
        TakeWhole(32 + 96L * places.Count + 2 * keyChars);
    }

    // A key given twice keeps its last value in its first place.
    public static ScriptJsonObject Of(ScriptCollectionBudget budget, IEnumerable<KeyValuePair<string, ScriptJson>> members)
    {
        var places = NoPlaces.ToBuilder();
        var ordered = ImmutableSortedDictionary.CreateBuilder<long, Member>();
        long next = 0;
        long keyChars = 0;
        long size = 1;
        int depth = 1;
        bool replaced = false;
        foreach (var (key, value) in members)
        {
            if (places.TryGetValue(key, out long place))
            {
                size -= ordered[place].Value.Size;
                ordered[place] = new Member(key, value);
                replaced = true;
            }
            else
            {
                places.Add(key, next);
                ordered.Add(next++, new Member(key, value));
                keyChars += key.Length;
            }
            size += value.Size;
            depth = System.Math.Max(depth, value.Depth + 1);
            CheckDepth(replaced ? 1 : depth);
        }
        if (replaced)
        {
            depth = 1;
            foreach (var member in ordered.Values)
                depth = System.Math.Max(depth, member.Value.Depth + 1);
        }
        return new ScriptJsonObject(budget, places.ToImmutable(), ordered.ToImmutable(), next, keyChars, size, depth);
    }

    public override ScriptJsonKind Kind => ScriptJsonKind.Object;

    public override long Size => _size;

    public override int Depth => _depth;

    public override int Count => _places.Count;

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var member in _members.Values)
                yield return member.Key;
        }
    }

    public IEnumerable<KeyValuePair<string, ScriptJson>> Members
    {
        get
        {
            foreach (var member in _members.Values)
                yield return new KeyValuePair<string, ScriptJson>(member.Key, member.Value);
        }
    }

    public bool ContainsKey(string key) => _places.ContainsKey(key);

    public bool TryGet(string key, out ScriptJson value)
    {
        if (_places.TryGetValue(key, out long place))
        {
            value = _members[place].Value;
            return true;
        }
        value = null!;
        return false;
    }

    public ScriptJsonObject Set(ScriptCollectionBudget budget, string key, ScriptJson value)
    {
        if (_places.TryGetValue(key, out long place))
        {
            var old = _members[place].Value;
            var members = _members.SetItem(place, new Member(key, value));
            return new(budget, _places, members, _nextPlace, _keyChars, _size - old.Size + value.Size, DepthAfter(members, old, value));
        }
        return new(budget, _places.Add(key, _nextPlace), _members.Add(_nextPlace, new Member(key, value)), _nextPlace + 1,
            _keyChars + key.Length, _size + value.Size, System.Math.Max(_depth, value.Depth + 1));
    }

    // Itself when it has no such key.
    public ScriptJsonObject Remove(ScriptCollectionBudget budget, string key, out bool removed)
    {
        removed = _places.TryGetValue(key, out long place);
        if (!removed)
            return this;
        var old = _members[place].Value;
        var members = _members.Remove(place);
        return new(budget, _places.Remove(key), members, _nextPlace, _keyChars - key.Length, _size - old.Size,
            DepthAfter(members, old, null));
    }

    // Every key of other set on this one in other's order, other's values winning. Deep merges an object
    // under a key both have into the object already there, all the way down; anything else is replaced.
    // Built in one go, so a big merge makes one object and not one per key.
    public ScriptJsonObject Merge(ScriptCollectionBudget budget, ScriptJsonObject other, bool deep)
    {
        var places = _places.ToBuilder();
        var members = _members.ToBuilder();
        long next = _nextPlace;
        long keyChars = _keyChars;
        long size = _size;
        int depth = _depth;
        bool shallower = false;
        foreach (var member in other._members.Values)
        {
            var value = member.Value;
            if (places.TryGetValue(member.Key, out long place))
            {
                var existing = members[place].Value;
                if (deep && value is ScriptJsonObject incoming && existing is ScriptJsonObject present)
                    value = present.Merge(budget, incoming, true);
                size += value.Size - existing.Size;
                members[place] = new Member(member.Key, value);
                shallower |= existing.Depth + 1 >= depth;
            }
            else
            {
                places.Add(member.Key, next);
                members.Add(next++, new Member(member.Key, value));
                keyChars += member.Key.Length;
                size += value.Size;
            }
            depth = System.Math.Max(depth, value.Depth + 1);
            CheckDepth(shallower ? 1 : depth);
        }
        if (shallower)
        {
            depth = 1;
            foreach (var member in members.Values)
                depth = System.Math.Max(depth, member.Value.Depth + 1);
        }
        return new ScriptJsonObject(budget, places.ToImmutable(), members.ToImmutable(), next, keyChars, size, depth);
    }

    private int DepthAfter(ImmutableSortedDictionary<long, Member> members, ScriptJson removed, ScriptJson? added)
    {
        int depth = added == null ? 1 : System.Math.Max(1, added.Depth + 1);
        if (removed.Depth + 1 < _depth)
            return System.Math.Max(_depth, depth);
        foreach (var member in members.Values)
            depth = System.Math.Max(depth, member.Value.Depth + 1);
        return depth;
    }

    private protected override bool Same(ScriptJson other)
    {
        var obj = (ScriptJsonObject)other;
        if (obj._places.Count != _places.Count || obj._size != _size)
            return false;
        foreach (var member in _members.Values)
        {
            if (!obj.TryGet(member.Key, out var value) || !value.Equals(member.Value))
                return false;
        }
        return true;
    }

    // The order of the keys is not part of what an object holds, so the members add up rather than chain.
    private protected override int Hash()
    {
        int hash = 0x6B9;
        foreach (var member in _members.Values)
            hash = unchecked(hash + HashCode.Combine(StringComparer.Ordinal.GetHashCode(member.Key), member.Value.GetHashCode()));
        return hash;
    }

    internal override void ReachInside(Action<ScriptCollection> reach)
    {
        foreach (var member in _members.Values)
            reach(member.Value);
    }
}
