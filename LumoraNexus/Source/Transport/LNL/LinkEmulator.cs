// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace Lumora.Nexus.Transport.LNL;

// A narrow, far link for testing on one machine, armed only by LUMORA_NET_EMULATE="<KB/s>:<one-way ms>" in the
// sending process. Everything an endpoint sends waits its turn through a bottleneck of that rate, then that much
// longer, and only then reaches LNL, in order. Reliable data is never lost here, since LNL cannot repair a hole
// made above it; unreliable data is let go once a quarter second is queued ahead of it, as a router would.
// -xlinka
internal sealed class LinkEmulator
{
    private static readonly (double Rate, double Latency)? Settings = Read();

    private readonly double _rate;
    private readonly long _latency;
    private readonly Queue<(long Due, Action Send)> _queue = new();
    private readonly object _lock = new();
    private long _lastDepart;
    private Thread? _thread;
    private volatile bool _stopped;

    public long Dropped { get; private set; }

    private LinkEmulator(double rate, double latency)
    {
        _rate = rate;
        _latency = (long)(latency * Stopwatch.Frequency);
    }

    public static LinkEmulator? Create() => Settings is { } s ? new LinkEmulator(s.Rate, s.Latency) : null;

    // What still waits for the bottleneck.
    public long QueuedBytes
    {
        get
        {
            lock (_lock)
            {
                long ahead = _lastDepart - Stopwatch.GetTimestamp();
                return ahead > 0 ? (long)(ahead * _rate / Stopwatch.Frequency) : 0;
            }
        }
    }

    public void Send(int length, bool droppable, Action send)
    {
        lock (_lock)
        {
            long now = Stopwatch.GetTimestamp();
            long start = System.Math.Max(now, _lastDepart);
            if (droppable && start - now > Stopwatch.Frequency / 4)
            {
                Dropped++;
                return;
            }
            _lastDepart = start + (long)(length / _rate * Stopwatch.Frequency);
            _queue.Enqueue((_lastDepart + _latency, send));
            if (_thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "LinkEmulator" };
                _thread.Start();
            }
        }
    }

    public void Stop() => _stopped = true;

    private void Run()
    {
        var due = new List<Action>();
        while (!_stopped)
        {
            lock (_lock)
            {
                long now = Stopwatch.GetTimestamp();
                while (_queue.Count > 0 && _queue.Peek().Due <= now)
                    due.Add(_queue.Dequeue().Send);
            }
            foreach (var send in due)
            {
                try { send(); }
                catch (Exception ex) { Diagnostics.NexusLog.Debug($"[lnl] emulated send threw: {ex.Message}"); }
            }
            if (due.Count == 0)
                Thread.Sleep(1);
            due.Clear();
        }
    }

    private static (double, double)? Read()
    {
        var value = System.Environment.GetEnvironmentVariable("LUMORA_NET_EMULATE");
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var parts = value.Split(':');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var kbps) || kbps <= 0)
            return null;
        double ms = 0;
        if (parts.Length > 1)
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out ms);
        Diagnostics.NexusLog.Warn($"[lnl] LUMORA_NET_EMULATE: sends held to {kbps} KB/s and {ms} ms");
        return (kbps * 1024.0, System.Math.Max(0, ms) / 1000.0);
    }
}
