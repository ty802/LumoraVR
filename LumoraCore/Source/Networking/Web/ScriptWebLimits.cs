// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumora.Core.Networking.Web;

// How many requests may run at once, per requester and per client, and how fast one requester may start
// them. A request past a limit is refused on the spot with the reason; nothing queues behind a limit, so
// nothing can wait forever on one.
internal sealed class ScriptWebLimits
{
    public const int PerRequester = 4;
    public const int PerMachine = 32;
    public const int StartsPerSecond = 10;

    // Idle requesters are swept once there are more of these than this.
    private const int SweepAbove = 64;

    private readonly object _lock = new();
    private readonly Dictionary<string, Requester> _requesters = new(StringComparer.Ordinal);
    private int _running;

    // What the refusals call the things counted: requests here, open sockets for the websocket limits.
    private readonly string _what;

    public ScriptWebLimits(string what = "web requests")
    {
        _what = what;
    }

    public int Running
    {
        get
        {
            lock (_lock)
                return _running;
        }
    }

    // Null when the request may start, else why it may not.
    public string? TryEnter(string key)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            if (_running >= PerMachine)
                return $"Too many {_what} are running on this machine (limit {PerMachine})";

            if (_requesters.TryGetValue(key, out var requester))
            {
                requester.Forget(now);
                if (requester.Running >= PerRequester)
                    return $"Too many {_what} are running at once (limit {PerRequester})";
                if (requester.Starts.Count >= StartsPerSecond)
                    return $"Too many {_what} were started in the last second (limit {StartsPerSecond})";
            }
            else
            {
                if (_requesters.Count > SweepAbove)
                    Sweep(now);
                requester = new Requester();
                _requesters[key] = requester;
            }

            requester.Running++;
            requester.Starts.Enqueue(now);
            _running++;
            return null;
        }
    }

    public void Exit(string key)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            if (_running > 0)
                _running--;
            if (!_requesters.TryGetValue(key, out var requester))
                return;
            if (requester.Running > 0)
                requester.Running--;
            requester.Forget(now);
            if (requester.IsIdle)
                _requesters.Remove(key);
        }
    }

    private void Sweep(long now)
    {
        List<string>? idle = null;
        foreach (var pair in _requesters)
        {
            pair.Value.Forget(now);
            if (pair.Value.IsIdle)
                (idle ??= new List<string>()).Add(pair.Key);
        }
        if (idle == null)
            return;
        foreach (var key in idle)
            _requesters.Remove(key);
    }

    private sealed class Requester
    {
        public int Running;
        // Stopwatch timestamps of the starts inside the last second.
        public readonly Queue<long> Starts = new();

        public bool IsIdle => Running == 0 && Starts.Count == 0;

        public void Forget(long now)
        {
            while (Starts.Count > 0 && now - Starts.Peek() >= Stopwatch.Frequency)
                Starts.Dequeue();
        }
    }
}
