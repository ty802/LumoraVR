// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Lumora.Core.Components;

// One per world: every ColliderUserTracker looked at every fifth update, a fifth of them on each update,
// from one coroutine. A tracker used to take a turn in the per-frame update only to count to five, and a
// world with six hundred of them paid six hundred calls a frame for a hundred and twenty looks. -xlinka
internal sealed class ColliderUserRound
{
    public const int Interval = 5;

    private static readonly ConditionalWeakTable<World, ColliderUserRound> Rounds = new();

    private readonly World _world;
    private readonly List<ColliderUserTracker> _trackers = new();
    private int _turn;
    private bool _running;

    private ColliderUserRound(World world)
    {
        _world = world;
    }

    public static ColliderUserRound For(World world) => Rounds.GetValue(world, static w => new ColliderUserRound(w));

    public void Add(ColliderUserTracker tracker)
    {
        if (_trackers.Contains(tracker))
            return;
        _trackers.Add(tracker);
        if (_running)
            return;
        _running = true;
        _world.StartCoroutine(Run());
    }

    public void Remove(ColliderUserTracker tracker) => _trackers.Remove(tracker);

    private IEnumerator Run()
    {
        while (_trackers.Count > 0 && !_world.IsDisposed)
        {
            int turn = _turn;
            _turn = (_turn + 1) % Interval;
            for (int i = turn; i < _trackers.Count; i += Interval)
            {
                try
                {
                    _trackers[i].Look();
                }
                catch (Exception ex)
                {
                    Logging.Logger.Error($"ColliderUserTracker: look failed on {_trackers[i].Slot?.Name.Value}: {ex.Message}");
                }
            }
            yield return null;
        }
        _running = false;
    }
}
