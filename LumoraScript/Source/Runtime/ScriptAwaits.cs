// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Lumora.Script;

// The work an instance's await nodes have out, and the answers that came back. Answers arrive on any
// thread and wait here until the thread driving the instance takes them. A live graph swap hands all of
// it to the new instance, which runs the answers of the await nodes it still has; stopping the script for
// good cancels whatever is still out and drops what came back. -xlinka
public sealed class ScriptAwaits
{
    private readonly object _lock = new();
    private readonly Queue<ScriptAnswer> _answers = new();
    private readonly List<CancellationTokenSource> _inherited = new();
    private readonly IReadOnlyList<ScriptAwaitEntry> _entries;
    private readonly ConstructorInfo?[] _tickets;
    private readonly int _warnAt;
    private readonly ScriptContextBase _context;
    private CancellationTokenSource _cancel = new();
    private ScriptAwaits? _forward;
    private int _out;
    private bool _stopped;

    internal ScriptAwaits(IReadOnlyList<ScriptAwaitEntry> entries, int warnAt, ScriptContextBase context)
    {
        _entries = entries;
        _tickets = new ConstructorInfo?[entries.Count];
        _warnAt = warnAt;
        _context = context;
    }

    // Work started and not yet answered. None once a newer instance took it over.
    public int Out
    {
        get
        {
            lock (_lock)
                return _forward != null ? 0 : _out;
        }
    }

    // Answers waiting to run here.
    public int Waiting
    {
        get
        {
            lock (_lock)
                return _answers.Count;
        }
    }

    internal AwaitTicket Open(int awaitIndex)
    {
        if ((uint)awaitIndex >= (uint)_entries.Count)
            throw new ScriptLimitException($"no await {awaitIndex}");
        var entry = _entries[awaitIndex];
        var make = _tickets[awaitIndex] ??= typeof(AwaitTicket<>).MakeGenericType(entry.PayloadType).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(ScriptAwaits), typeof(string), typeof(CancellationToken) })!;
        CancellationToken token;
        bool many;
        lock (_lock)
        {
            if (_stopped)
                throw new ScriptLimitException("the script is stopping");
            many = ++_out == _warnAt + 1;
            token = _cancel.Token;
        }
        if (many)
            _context.Warn(ScriptWarning.Awaits, $"more than {_warnAt:N0} awaits out at once");
        return (AwaitTicket)make.Invoke(new object[] { this, entry.NodeId, token });
    }

    internal bool Post(string nodeId, string exit, object payload)
    {
        while (true)
        {
            var owner = Owner();
            lock (owner._lock)
            {
                if (owner._forward != null)
                    continue;
                if (owner._stopped)
                    return false;
                owner._out = Math.Max(0, owner._out - 1);
                owner._answers.Enqueue(new ScriptAnswer(nodeId, exit, payload));
                return true;
            }
        }
    }

    internal void Release()
    {
        while (true)
        {
            var owner = Owner();
            lock (owner._lock)
            {
                if (owner._forward != null)
                    continue;
                owner._out = Math.Max(0, owner._out - 1);
                return;
            }
        }
    }

    internal bool TryTake(out ScriptAnswer answer)
    {
        lock (_lock)
            return _answers.TryDequeue(out answer);
    }

    internal void Take(List<ScriptAnswer> into)
    {
        lock (_lock)
        {
            while (_answers.Count > 0)
                into.Add(_answers.Dequeue());
        }
    }

    // A live swap: what the previous instance had out now answers here, and is cancelled only when this
    // one stops.
    internal void Absorb(ScriptAwaits previous)
    {
        if (ReferenceEquals(previous, this))
            return;
        lock (previous._lock)
        {
            lock (_lock)
            {
                while (previous._answers.Count > 0)
                    _answers.Enqueue(previous._answers.Dequeue());
                _out += previous._out;
                previous._out = 0;
                _inherited.Add(previous._cancel);
                _inherited.AddRange(previous._inherited);
                previous._inherited.Clear();
                previous._cancel = new CancellationTokenSource();
                previous._forward = this;
            }
        }
    }

    internal void Stop()
    {
        List<CancellationTokenSource> cancel;
        lock (_lock)
        {
            if (_stopped)
                return;
            _stopped = true;
            _answers.Clear();
            _out = 0;
            cancel = new List<CancellationTokenSource>(_inherited) { _cancel };
        }
        foreach (var source in cancel)
        {
            try
            {
                source.Cancel();
            }
            catch (AggregateException)
            {
            }
        }
    }

    private ScriptAwaits Owner()
    {
        var owner = this;
        while (owner._forward is { } next)
            owner = next;
        return owner;
    }
}
