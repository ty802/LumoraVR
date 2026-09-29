// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Script;

// What one script instance's lists, dictionaries and JSON hold between them, in rough bytes: every item a
// fixed cost, a string its characters on top. Past WarnAt the script is warned, once each time it goes
// over; nothing stops it. A collection nobody holds any more is counted out the next time the count is
// taken, so dropping a big list gives its room back without anyone having to clear it.
//
// Given the instance's context, the budget sees for itself what nobody holds: a script reaches a
// collection only through a handle, or inside a collection it reaches, so anything else is out of the
// count whether or not the garbage collector has freed it yet. Without the context it can only count what
// the collector has not freed, and a list that was held a long time can be freed long after it is
// dropped. -xlinka
public sealed class ScriptCollectionBudget
{
    public const long WarnBytes = 32L * 1024 * 1024;

    // A single list or dictionary past this many items is warned about.
    public const int WarnItems = 65536;

    private const int FirstSweep = 4096;

    private readonly List<WeakReference<ScriptCollection>> _live = new();
    private readonly WeakReference<ScriptContextBase>? _owner;
    private int _sweepAt = FirstSweep;
    private int _reached;
    private long _used;
    private long _countAt;
    private bool _over;

    public ScriptCollectionBudget(long warnAt = WarnBytes)
    {
        WarnAt = warnAt;
        _countAt = warnAt;
    }

    // Counts what the owner's handles reach, and warns the owner. Held weakly: a list carried into a newer
    // instance must not keep the old one's handle table alive.
    public ScriptCollectionBudget(long warnAt, ScriptContextBase owner) : this(warnAt)
    {
        _owner = new WeakReference<ScriptContextBase>(owner);
    }

    public long WarnAt { get; }

    public long Used => _used;

    // As of the last count, for a budget with an owner.
    public int LiveCollections => _owner != null ? _reached : _live.Count;

    internal void Track(ScriptCollection collection)
    {
        if (_owner != null)
            return;
        // Swept each time the tracked count doubles, so a script that holds thousands of collections does
        // not pay for a sweep on every new one.
        if (_live.Count >= _sweepAt)
        {
            Recount();
            _sweepAt = Math.Max(FirstSweep, _live.Count * 2);
        }
        _live.Add(new WeakReference<ScriptCollection>(collection));
    }

    // Past the mark the count is taken again, since what nobody holds leaves it only then. Over WarnAt, the
    // next count waits until the total has doubled, so a script far over pays for a count now and then
    // instead of on every add.
    internal void Take(long bytes)
    {
        if (_used + bytes > _countAt)
        {
            Recount();
            if (_used + bytes > WarnAt)
            {
                if (!_over)
                {
                    _over = true;
                    var room = WarnAt >= 1024 * 1024 ? $"{WarnAt / (1024 * 1024)} MiB" : $"{WarnAt / 1024} KiB";
                    Warn(ScriptWarning.Collections, $"lists, dictionaries and JSON hold more than {room} between them");
                }
                _countAt = 2 * (_used + bytes);
            }
            else
            {
                _over = false;
                _countAt = WarnAt;
            }
        }
        _used += bytes;
    }

    internal void Warn(ScriptWarning kind, string text)
    {
        if (_owner != null && _owner.TryGetTarget(out var owner))
            owner.Warn(kind, text);
    }

    internal void Give(long bytes) => _used = Math.Max(0, _used - bytes);

    private void Recount()
    {
        if (_owner == null)
            CountLive();
        else if (_owner.TryGetTarget(out var owner))
            CountReachable(owner);
    }

    private void CountLive()
    {
        long used = 0;
        _live.RemoveAll(entry =>
        {
            if (!entry.TryGetTarget(out var collection))
                return true;
            used += collection.Bytes;
            return false;
        });
        _used = used;
    }

    // A collection being filled by the node that made it has no handle yet, so a count taken part way
    // through leaves out what it holds so far; it is counted again from the next count on.
    private void CountReachable(ScriptContextBase owner)
    {
        var seen = new HashSet<ScriptCollection>();
        var pending = new Stack<ScriptCollection>();
        void Reach(ScriptCollection collection)
        {
            if (seen.Add(collection))
                pending.Push(collection);
        }

        int end = owner.HandleEnd;
        for (int handle = 1; handle < end; handle++)
        {
            if (owner.GetHandle(handle) is ScriptCollection held)
                Reach(held);
        }

        long used = 0;
        int reached = 0;
        Action<ScriptCollection> reach = Reach;
        while (pending.Count > 0)
        {
            var collection = pending.Pop();
            if (ReferenceEquals(collection.Budget, this))
            {
                used += collection.Bytes;
                reached++;
            }
            collection.ReachInside(reach);
        }
        _used = used;
        _reached = reached;
    }
}
