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
// good cancels whatever is still out and drops what came back.
//
// CALLS of this script's waiting Functions, the ones whose bodies hold a Delay, an await or a call of one
// that does, are kept here too, each by the word every flow of its body carries: the ticket its caller waits
// on, the arguments, and how many waits its body has under way. A Return answers the ticket. A body whose
// flows have all run out with nothing under way and no Return reached answers it with defaults. A wait in a
// call that is gone, a script stopped or swapped since, comes back to nothing and runs nothing. -xlinka
public sealed class ScriptAwaits
{
    private readonly object _lock = new();
    private readonly Queue<ScriptAnswer> _answers = new();

    // The queue's count, set under the lock at every change and read without it: the script phase asks
    // every script every frame whether anything came back, and a lock each was most of what asking cost.
    private int _waiting;
    private readonly List<CancellationTokenSource> _inherited = new();
    private readonly IReadOnlyList<ScriptAwaitEntry> _entries;
    private readonly ConstructorInfo?[] _tickets;
    private readonly int _warnAt;
    private readonly ScriptContextBase _context;
    private CancellationTokenSource _cancel = new();
    private ScriptAwaits? _forward;
    private int _out;
    private bool _stopped;

    private sealed class UnderWay
    {
        public required CallTicket Ticket;
        public required FunctionSignature Signature;
        public required string Function;
        public object?[]? Arguments;
        public int Waits;
        public bool Answered;
    }

    private readonly Dictionary<int, UnderWay> _running = new();

    // Words are unique across every instance, so a wait from a script swapped out never finds a call of the
    // one swapped in.
    private static int _lastWord;

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
    public int Waiting => Volatile.Read(ref _waiting);

    // Calls of this script's waiting Functions still under way.
    public int CallsUnderWay
    {
        get
        {
            lock (_lock)
                return _running.Count;
        }
    }

    internal AwaitTicket Open(int awaitIndex, int word = 0)
    {
        if ((uint)awaitIndex >= (uint)_entries.Count)
            throw new ScriptLimitException($"no await {awaitIndex}");
        var entry = _entries[awaitIndex];
        // A call answers with values for the node's own ports, not with a payload struct.
        ConstructorInfo? make = null;
        if (entry.PayloadType != typeof(CallAnswer))
            make = _tickets[awaitIndex] ??= typeof(AwaitTicket<>).MakeGenericType(entry.PayloadType).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(ScriptAwaits), typeof(string), typeof(CancellationToken), typeof(int) })!;
        CancellationToken token;
        bool many;
        lock (_lock)
        {
            if (_stopped)
                throw new ScriptLimitException("the script is stopping");
            many = ++_out == _warnAt + 1;
            token = _cancel.Token;
            if (word != 0 && _running.TryGetValue(word, out var running))
                running.Waits++;
        }
        if (many)
            _context.Warn(ScriptWarning.Awaits, $"more than {_warnAt:N0} awaits out at once");
        if (make == null)
            return new CallTicket(this, entry.NodeId, token, entry.Overload, word);
        return (AwaitTicket)make.Invoke(new object[] { this, entry.NodeId, token, word });
    }

    internal bool Post(string nodeId, string exit, object payload, int word = 0)
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
                owner._answers.Enqueue(new ScriptAnswer(nodeId, exit, payload, word));
                Volatile.Write(ref owner._waiting, owner._answers.Count);
                return true;
            }
        }
    }

    internal void Release(int word = 0)
    {
        ScriptAwaits owner;
        while (true)
        {
            owner = Owner();
            lock (owner._lock)
            {
                if (owner._forward != null)
                    continue;
                owner._out = Math.Max(0, owner._out - 1);
                break;
            }
        }
        // A wait that will never come back is no longer under way in its call.
        if (word != 0 && owner.Resume(word))
            owner.Settle(word);
    }

    // A call of one of this script's waiting Functions begins: the ticket its caller waits on, answered when
    // the call returns. The word its body's flows carry from here on.
    internal int Begin(CallTicket ticket, FunctionSignature signature, string function)
    {
        int word;
        do
            word = Interlocked.Increment(ref _lastWord) & int.MaxValue;
        while (word == 0);
        lock (_lock)
            _running[word] = new UnderWay { Ticket = ticket, Signature = signature, Function = function };
        return word;
    }

    // A Delay armed in a call: one more wait under way there.
    internal void Wait(int word)
    {
        if (word == 0)
            return;
        lock (_lock)
        {
            if (_running.TryGetValue(word, out var running))
                running.Waits++;
        }
    }

    // A wait in a call coming back, about to run on. False when the call is gone, its caller in another script
    // stopped included, and then nothing runs.
    internal bool Resume(int word)
    {
        if (word == 0)
            return true;
        lock (_lock)
        {
            if (!_running.TryGetValue(word, out var running))
                return false;
            if (running.Ticket.Cancelled.IsCancellationRequested)
            {
                _running.Remove(word);
                return false;
            }
            running.Waits = Math.Max(0, running.Waits - 1);
            return true;
        }
    }

    internal void Keep(int word, object?[] arguments)
    {
        lock (_lock)
        {
            if (_running.TryGetValue(word, out var running))
                running.Arguments = arguments;
        }
    }

    // One of a call's arguments, for a flow of its body that runs after a wait, and the type its port carries.
    internal object? Argument(int word, int port, out ScriptType? type)
    {
        type = null;
        lock (_lock)
        {
            if (!_running.TryGetValue(word, out var running) || (uint)port >= (uint)running.Signature.Inputs.Count)
                return null;
            type = running.Signature.Inputs[port].Type;
            return running.Arguments != null && port < running.Arguments.Length ? running.Arguments[port] : null;
        }
    }

    // A Return reached: the caller hears what it gave back. The first one only; anything still under way in the
    // call runs on to its end and is heard by nobody.
    internal void Finish(int word, IReadOnlyList<object?> outputs)
    {
        CallTicket ticket;
        lock (_lock)
        {
            if (!_running.TryGetValue(word, out var running) || running.Answered)
                return;
            running.Answered = true;
            ticket = running.Ticket;
            if (running.Waits == 0)
                _running.Remove(word);
        }
        ticket.Return(outputs);
    }

    // A flow of the call ran out. With nothing under way any more, the call is over: one that reached no Return
    // gives every output its default.
    internal void Settle(int word)
    {
        if (word == 0)
            return;
        CallTicket? ticket = null;
        lock (_lock)
        {
            if (!_running.TryGetValue(word, out var running) || running.Waits > 0)
                return;
            _running.Remove(word);
            if (!running.Answered)
                ticket = running.Ticket;
        }
        ticket?.Return(Array.Empty<object?>());
    }

    // A flow of the call trapped: the call is over, and its caller hears why where it can.
    internal void Fault(int word, string reason, bool onlyIfIdle = false)
    {
        if (word == 0)
            return;
        CallTicket? ticket = null;
        string function;
        lock (_lock)
        {
            if (!_running.TryGetValue(word, out var running) || (onlyIfIdle && running.Waits > 0))
                return;
            _running.Remove(word);
            function = running.Function;
            if (!running.Answered)
                ticket = running.Ticket;
        }
        ticket?.Fail($"{function} failed: {reason}");
    }

    internal bool TryTake(out ScriptAnswer answer)
    {
        lock (_lock)
        {
            bool took = _answers.TryDequeue(out answer);
            Volatile.Write(ref _waiting, _answers.Count);
            return took;
        }
    }

    internal void Take(List<ScriptAnswer> into)
    {
        lock (_lock)
        {
            while (_answers.Count > 0)
                into.Add(_answers.Dequeue());
            Volatile.Write(ref _waiting, 0);
        }
    }

    // A live swap: what the previous instance had out now answers here, and is cancelled only when this
    // one stops. The calls its waiting Functions had under way end there: their callers in other scripts hear
    // so, and what they were waiting on comes back to nothing.
    internal void Absorb(ScriptAwaits previous)
    {
        if (ReferenceEquals(previous, this))
            return;
        List<UnderWay> ended;
        lock (previous._lock)
        {
            ended = new List<UnderWay>(previous._running.Values);
            previous._running.Clear();
            lock (_lock)
            {
                while (previous._answers.Count > 0)
                    _answers.Enqueue(previous._answers.Dequeue());
                Volatile.Write(ref previous._waiting, 0);
                Volatile.Write(ref _waiting, _answers.Count);
                _out += previous._out;
                previous._out = 0;
                _inherited.Add(previous._cancel);
                _inherited.AddRange(previous._inherited);
                previous._inherited.Clear();
                previous._cancel = new CancellationTokenSource();
                previous._forward = this;
            }
        }
        foreach (var running in ended)
        {
            if (!running.Answered)
                running.Ticket.Fail($"its script changed before {running.Function} returned");
        }
    }

    internal void Stop()
    {
        List<CancellationTokenSource> cancel;
        List<UnderWay> ended;
        lock (_lock)
        {
            if (_stopped)
                return;
            _stopped = true;
            _answers.Clear();
            Volatile.Write(ref _waiting, 0);
            _out = 0;
            cancel = new List<CancellationTokenSource>(_inherited) { _cancel };
            ended = new List<UnderWay>(_running.Values);
            _running.Clear();
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
        foreach (var running in ended)
        {
            if (!running.Answered)
                running.Ticket.Fail($"its script stopped before {running.Function} returned");
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
