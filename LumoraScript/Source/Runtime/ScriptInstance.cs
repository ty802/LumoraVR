// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Lumora.Wasm;

namespace Lumora.Script;

// One compiled graph, linked and running. Knows nothing about worlds: the context it is given decides
// whose authority node calls run under and which capabilities link at all. The module it runs is one
// this process's compiler made and the engine's validator decoded, once per graph however many
// instances run it.
//
// Must be driven from one thread at a time, the thread that owns whatever the nodes touch. -xlinka
public sealed class ScriptInstance
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _payloadProperties = new();

    private readonly WasmStore _store;
    private readonly WasmModule _module;
    private readonly WasmInstance _instance;
    private readonly ScriptInstanceOptions _options;
    private readonly Dictionary<string, (ScriptEventEntry Entry, WasmFunction Function)> _events = new(StringComparer.Ordinal);
    private readonly List<WasmGlobal> _handleRoots = new();
    private readonly Dictionary<string, WasmGlobal[]> _variables = new(StringComparer.Ordinal);
    private readonly WasmFunction[] _timerFunctions;
    private readonly Dictionary<(string NodeId, string Exit), (ScriptAwaitEntry Entry, WasmFunction Function)> _answers = new();
    private readonly Dictionary<string, WasmFunction> _showFunctions = new(StringComparer.Ordinal);
    private readonly WasmGlobal[] _traces;
    private readonly Dictionary<string, (FlowShapeKind Kind, WasmGlobal Global)> _memory = new(StringComparer.Ordinal);
    private readonly PriorityQueue<int, (double Due, long Order)> _timers;

    public CompileResult Compiled { get; }
    public ScriptContextBase Context { get; }
    public int Faults { get; private set; }
    public ScriptCallResult LastResult { get; private set; }

    private ScriptInstance(CompileResult compiled, ScriptContextBase context, ScriptInstanceOptions options,
        WasmStore store, WasmModule module, WasmInstance instance, PriorityQueue<int, (double Due, long Order)> timers)
    {
        _timers = timers;
        Compiled = compiled;
        Context = context;
        _options = options;
        _store = store;
        _module = module;
        _instance = instance;

        foreach (var entry in compiled.Events)
        {
            var function = instance.GetFunction(entry.Export)
                ?? throw new ScriptLinkException($"module has no export {entry.Export}");
            _events.Add(entry.NodeId, (entry, function));
        }

        foreach (var variable in compiled.Variables)
        {
            var globals = new WasmGlobal[variable.Exports.Count];
            for (int i = 0; i < globals.Length; i++)
            {
                globals[i] = instance.GetGlobal(variable.Exports[i])
                    ?? throw new ScriptLinkException($"module has no export {variable.Exports[i]}");
            }
            _variables.Add(variable.NodeId, globals);
            if (variable.Type.IsHandle)
                _handleRoots.Add(globals[0]);
        }

        foreach (var display in compiled.Displays)
        {
            if (display.Export == null)
                continue;
            _showFunctions.Add(display.NodeId, instance.GetFunction(display.Export)
                ?? throw new ScriptLinkException($"module has no export {display.Export}"));
        }

        _traces = new WasmGlobal[compiled.Traces.Count];
        for (int i = 0; i < _traces.Length; i++)
        {
            _traces[i] = instance.GetGlobal(compiled.Traces[i].Export)
                ?? throw new ScriptLinkException($"module has no export {compiled.Traces[i].Export}");
        }

        foreach (var entry in compiled.FlowMemory)
        {
            _memory[entry.NodeId] = (entry.Kind, instance.GetGlobal(entry.Export)
                ?? throw new ScriptLinkException($"module has no export {entry.Export}"));
        }

        _timerFunctions = new WasmFunction[compiled.Timers.Count];
        for (int i = 0; i < _timerFunctions.Length; i++)
        {
            _timerFunctions[i] = instance.GetFunction(compiled.Timers[i].Export)
                ?? throw new ScriptLinkException($"module has no export {compiled.Timers[i].Export}");
        }

        foreach (var entry in compiled.Awaits)
        {
            for (int i = 0; i < entry.Exits.Count; i++)
            {
                _answers[(entry.NodeId, entry.Exits[i])] = (entry, instance.GetFunction(entry.Exports[i])
                    ?? throw new ScriptLinkException($"module has no export {entry.Exports[i]}"));
            }
        }
    }

    public static ScriptInstance Create(CompileResult compiled, ScriptContextBase context, IReadOnlyList<object?> bindings,
        ScriptInstanceOptions? options = null)
    {
        options ??= ScriptInstanceOptions.Default;
        if (!compiled.Success || compiled.Module == null)
            throw new ScriptLinkException("the graph did not compile");
        if (bindings.Count != compiled.Bindings.Count)
            throw new ScriptLinkException($"the graph declares {compiled.Bindings.Count} binding(s), {bindings.Count} given");
        for (int i = 0; i < bindings.Count; i++)
        {
            var value = bindings[i];
            var type = compiled.Bindings[i].Type;
            if (value != null && !type.ClrType.IsInstanceOfType(value))
                throw new ScriptLinkException($"binding '{compiled.Bindings[i].Name}' takes {type.Name}, got {value.GetType().Name}");
        }

        context.PinBindings(bindings);
        context.Awaits = new ScriptAwaits(compiled.Awaits, options.WarnAwaits, context);

        var module = compiled.Decoded ?? WasmModule.Decode(compiled.Module, ScriptCompiler.ModuleLimits);
        var store = new WasmStore();
        var linker = new WasmLinker(store);

        linker.DefineFunction(ScriptCompiler.RuntimeModule, ScriptCompiler.StringImport,
            new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.I32 }, new[] { WasmValueType.I32 }),
            call => MakeString(call, context));

        // The queue exists before the instance does, because the timer import is bound at link time.
        var timers = new PriorityQueue<int, (double Due, long Order)>();
        long order = 0;
        int timerCount = compiled.Timers.Count;
        if (timerCount > 0)
        {
            linker.DefineFunction(ScriptCompiler.RuntimeModule, ScriptCompiler.TimerImport,
                new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.F32 }, Array.Empty<WasmValueType>()),
                call => ArmTimer(call, context, options, timers, timerCount, ref order));
        }

        linker.DefineFunction(ScriptCompiler.RuntimeModule, ScriptCompiler.NowImport,
            new WasmFuncType(Array.Empty<WasmValueType>(), new[] { WasmValueType.F64 }),
            call => call.SetF64(0, context.Now));
        linker.DefineFunction(ScriptCompiler.RuntimeModule, ScriptCompiler.EachCountImport,
            new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.I32 }, new[] { WasmValueType.I32 }),
            call => call.SetI32(0, Math.Max(0, context.EachCount((EachSource)call.GetI32(0), context.GetHandle(call.GetI32(1))))));
        linker.DefineFunction(ScriptCompiler.RuntimeModule, ScriptCompiler.EachItemImport,
            new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.I32, WasmValueType.I32 }, new[] { WasmValueType.I32 }),
            call => call.SetI32(0, context.AddHandle(context.EachItem((EachSource)call.GetI32(0), context.GetHandle(call.GetI32(1)), call.GetI32(2)))));
        linker.DefineFunction(ScriptCompiler.RuntimeModule, ScriptCompiler.EachQueryImport, ScriptCompiler.EachQueryType,
            call =>
            {
                Span<float> query = stackalloc float[FlowBuilder.MaxQueryLanes];
                for (int i = 0; i < query.Length; i++)
                    query[i] = call.GetF32(i + 1);
                call.SetI32(0, context.AddHandle(context.EachQuery((EachSource)call.GetI32(0), query)));
            });

        foreach (var overload in compiled.Imports)
        {
            if (!context.AllowsCapability(overload.Definition.Capability))
                throw new ScriptLinkException($"{overload.ImportName} needs {overload.Definition.Capability}, which this script may not use");
            linker.DefineFunction(ScriptCompiler.NodeModule, overload.ImportName, overload.ImportType!, overload.Invoker.Bind(context));
        }

        var instance = linker.Instantiate(module, new WasmInstanceLimits
        {
            MaxMemoryPages = options.MaxMemoryPages,
            MaxCallDepth = options.MaxCallDepth,
        });
        context.EndPinning();

        return new ScriptInstance(compiled, context, options, store, module, instance, timers);
    }

    // Working memory the instance holds, fixed when it starts: a graph's module never grows its memory.
    public long MemorySize => _instance.MemorySize;

    public IEnumerable<ScriptEventEntry> Events
    {
        get
        {
            foreach (var (entry, _) in _events.Values)
                yield return entry;
        }
    }

    // Invokes one event node with its payload already in lanes.
    public ScriptCallResult Raise(string nodeId, ReadOnlySpan<ulong> payload)
    {
        if (!_events.TryGetValue(nodeId, out var target))
            return LastResult = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, nodeId, "no such event node", 0);
        return LastResult = Invoke(target.Function, payload);
    }

    // Invokes one event node with a payload struct, turned into lanes in the payload's declared order.
    public ScriptCallResult Raise<T>(string nodeId, in T payload) where T : struct
    {
        if (!_events.TryGetValue(nodeId, out var target))
            return LastResult = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, nodeId, "no such event node", 0);
        if (target.Entry.PayloadType != typeof(T))
            return LastResult = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, nodeId, $"{nodeId} takes {target.Entry.PayloadType.Name}", 0);
        return LastResult = Invoke(target.Function, Encode(target.Entry, payload));
    }

    // Every event node of this payload type, in graph order. Returns how many ran without a fault.
    public int RaiseAll<T>(in T payload) where T : struct
    {
        int ok = 0;
        foreach (var (entry, function) in _events.Values)
        {
            if (entry.PayloadType != typeof(T))
                continue;
            LastResult = Invoke(function, Encode(entry, payload));
            if (LastResult.Ok)
                ok++;
        }
        return ok;
    }

    public int PendingTimers => _timers.Count;

    // Moves every timer due by now into the list, earliest first. Anything they arm when they run waits
    // for the next take, so a Delay of zero is one frame, never a loop inside one.
    public void TakeDueTimers(double now, List<int> due)
    {
        while (_timers.TryPeek(out _, out var when) && when.Due <= now)
            due.Add(_timers.Dequeue());
    }

    // Runs what follows the Delay a timer belongs to.
    public ScriptCallResult RunTimer(int timer)
    {
        if ((uint)timer >= (uint)_timerFunctions.Length)
            return LastResult = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, null, $"no timer {timer}", 0);
        return LastResult = Invoke(_timerFunctions[timer], ReadOnlySpan<ulong>.Empty);
    }

    // Answers to this instance's awaits that came back since the last take, in the order they came.
    public void TakeAnswers(List<ScriptAnswer> into) => Context.Awaits?.Take(into);

    // The oldest answer that came back, taken only when it is about to run, so one still waiting at a live
    // graph swap goes to the new instance with the rest.
    public bool TryTakeAnswer(out ScriptAnswer answer)
    {
        answer = default;
        return Context.Awaits?.TryTake(out answer) == true;
    }

    public int WaitingAnswers => Context.Awaits?.Waiting ?? 0;

    // Work the awaits have out and answers not yet taken.
    public int PendingAwaits => Context.Awaits is { } awaits ? awaits.Out + awaits.Waiting : 0;

    // Runs the exit an answer names, with its payload on the await's outputs. An answer for an await the
    // graph no longer has, or an exit it lacks, runs nothing.
    public ScriptCallResult RunAnswer(in ScriptAnswer answer)
    {
        if (!_answers.TryGetValue((answer.NodeId, answer.Exit), out var target))
            return LastResult = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, answer.NodeId, $"no answer {answer.Exit} on {answer.NodeId}", 0);
        if (answer.Payload.GetType() != target.Entry.PayloadType)
            return LastResult = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, answer.NodeId, $"{answer.NodeId} answers with {target.Entry.PayloadType.Name}", 0);
        return LastResult = Invoke(target.Function, Encode(target.Entry.Overload, target.Entry.PayloadType, answer.Payload));
    }

    // The script is stopping for good: whatever the awaits have out is cancelled and what came back dropped.
    public void StopAwaits() => Context.Awaits?.Stop();

    // Works out one Display that needs no flow, into its globals.
    public ScriptCallResult RunDisplay(string nodeId)
    {
        if (!_showFunctions.TryGetValue(nodeId, out var function))
            return LastResult = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, nodeId, $"no display {nodeId}", 0);
        return LastResult = Invoke(function, ReadOnlySpan<ulong>.Empty);
    }

    // How many times flow has left by the wired exit Compiled.Traces[index] names, since this instance began.
    public int ReadTrace(int index) => (uint)index < (uint)_traces.Length ? LaneCodec.ToInt(_traces[index].RawValue) : 0;

    // A stored variable's current value, read straight from its globals.
    public object? ReadVariable(string nodeId)
    {
        if (!_variables.TryGetValue(nodeId, out var globals))
            return null;
        var type = Compiled.Variables.First(v => v.NodeId == nodeId).Type;
        var lanes = new ulong[globals.Length];
        for (int i = 0; i < lanes.Length; i++)
            lanes[i] = globals[i].RawValue;
        return LaneCodec.Read(type, lanes, Context);
    }

    public bool WriteVariable(string nodeId, object? value)
    {
        if (!_variables.TryGetValue(nodeId, out var globals))
            return false;
        var type = Compiled.Variables.First(v => v.NodeId == nodeId).Type;
        var lanes = new ulong[globals.Length];
        LaneCodec.Write(type, value, lanes, Context);
        for (int i = 0; i < lanes.Length; i++)
            globals[i].SetRawValue(lanes[i]);
        return true;
    }

    // A live graph swap: every stored variable whose name and type both still match takes the old
    // instance's value. Handles are carried as the objects behind them, since each instance numbers
    // its own. Answers how many carried over.
    public int AdoptStoredValues(ScriptInstance previous)
    {
        int adopted = 0;
        foreach (var variable in Compiled.Variables)
        {
            ScriptVariableEntry? match = null;
            foreach (var old in previous.Compiled.Variables)
            {
                if (old.Name == variable.Name && old.Type == variable.Type)
                {
                    match = old;
                    break;
                }
            }
            if (match != null && WriteVariable(variable.NodeId, previous.ReadVariable(match.NodeId)))
                adopted++;
        }
        return adopted;
    }

    // A live graph swap, past stored values: what each Once, Toggle and Cooldown remembers goes to the node
    // of the same id and kind, every Delay still waiting whose node is still there waits on, due when it
    // was, and the awaits' work out answers here instead. Answers how many carried over.
    public int AdoptFlowState(ScriptInstance previous)
    {
        int adopted = 0;
        if (Context.Awaits is { } awaits && previous.Context.Awaits is { } before)
        {
            adopted += before.Out + before.Waiting;
            awaits.Absorb(before);
        }
        foreach (var (nodeId, (kind, global)) in _memory)
        {
            if (previous._memory.TryGetValue(nodeId, out var old) && old.Kind == kind)
            {
                global.SetRawValue(old.Global.RawValue);
                adopted++;
            }
        }

        if (previous._timers.Count == 0 || Compiled.Timers.Count == 0)
            return adopted;
        var byNode = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Compiled.Timers.Count; i++)
            byNode[Compiled.Timers[i].NodeId] = i;
        var waiting = new List<(int Timer, double Due, long Order)>();
        foreach (var (timer, when) in previous._timers.UnorderedItems)
        {
            if ((uint)timer < (uint)previous.Compiled.Timers.Count && byNode.TryGetValue(previous.Compiled.Timers[timer].NodeId, out int mine))
                waiting.Add((mine, when.Due, when.Order));
        }
        waiting.Sort((a, b) => a.Due != b.Due ? a.Due.CompareTo(b.Due) : a.Order.CompareTo(b.Order));
        long order = long.MinValue;
        foreach (var (timer, due, _) in waiting)
        {
            _timers.Enqueue(timer, (due, order++));
            adopted++;
        }
        return adopted;
    }

    // Whether any event node takes this payload.
    public bool Handles(Type payloadType)
    {
        foreach (var (entry, _) in _events.Values)
        {
            if (entry.PayloadType == payloadType)
                return true;
        }
        return false;
    }

    private ScriptCallResult Invoke(WasmFunction function, ReadOnlySpan<ulong> payload)
    {
        long fuel = _options.FuelPerCall;
        try
        {
            long deadline = Environment.TickCount64 + _options.MaxCallMilliseconds;
            long left = _store.Invoke(function, payload, Span<ulong>.Empty, fuel, _options.MaxCallDepth, deadline);
            return new ScriptCallResult(ScriptCallStatus.Ok, null, null, fuel - left);
        }
        catch (WasmTrapException trap)
        {
            Faults++;
            string? node = null;
            if (trap.ByteOffset >= 0 && WasmSourceMap.TryFindTag(_module, trap.ByteOffset, out int tag)
                && tag >= 0 && tag < Compiled.TagNodes.Count)
                node = Compiled.TagNodes[tag];
            long used = fuel - trap.FuelLeft;
            if (trap.Code == WasmTrapCode.OutOfTime)
                return new ScriptCallResult(ScriptCallStatus.OutOfTime, node, $"stopped after {_options.MaxCallMilliseconds / 1000.0:0.#} s in one call", used);
            var status = trap.Code == WasmTrapCode.OutOfFuel ? ScriptCallStatus.OutOfFuel : ScriptCallStatus.Trap;
            return new ScriptCallResult(status, node, trap.Message, used);
        }
        finally
        {
            CollectHandles();
        }
    }

    private void CollectHandles()
    {
        Span<int> roots = _handleRoots.Count <= 64 ? stackalloc int[_handleRoots.Count] : new int[_handleRoots.Count];
        for (int i = 0; i < _handleRoots.Count; i++)
            roots[i] = LaneCodec.ToInt(_handleRoots[i].RawValue);
        Context.Collect(roots);
    }

    private ulong[] Encode<T>(ScriptEventEntry entry, in T payload) where T : struct
        => Encode(entry.Overload, typeof(T), payload);

    private ulong[] Encode(NodeOverload overload, Type payloadType, object boxed)
    {
        var properties = _payloadProperties.GetOrAdd(payloadType, NodeRegistry.PayloadProperties);
        var outputs = overload.Outputs;
        int laneCount = 0;
        foreach (var port in outputs)
            laneCount += port.Type.LaneCount;

        var lanes = new ulong[laneCount];
        int lane = 0;
        for (int i = 0; i < outputs.Count; i++)
        {
            var type = outputs[i].Type;
            LaneCodec.Write(type, properties[i].GetValue(boxed), lanes.AsSpan(lane, type.LaneCount), Context);
            lane += type.LaneCount;
        }
        return lanes;
    }

    private static void ArmTimer(WasmHostCall call, ScriptContextBase context, ScriptInstanceOptions options,
        PriorityQueue<int, (double Due, long Order)> timers, int timerCount, ref long order)
    {
        int timer = call.GetI32(0);
        float seconds = call.GetF32(1);
        if ((uint)timer >= (uint)timerCount)
            throw new ScriptLimitException($"no timer {timer}");
        if (timers.Count == options.WarnDelays)
            context.Warn(ScriptWarning.Delays, $"more than {options.WarnDelays:N0} delays waiting");
        if (!(seconds > 0f))
            seconds = 0f;
        timers.Enqueue(timer, (context.Now + seconds, order++));
    }

    private static void MakeString(WasmHostCall call, ScriptContextBase context)
    {
        int pointer = call.GetI32(0);
        int length = call.GetI32(1);
        var memory = call.Memory;
        if (pointer < 0 || length < 0 || (long)pointer + length > memory.Length)
            throw new ScriptLimitException("string out of bounds");
        call.SetI32(0, context.AddHandle(Encoding.UTF8.GetString(memory.Slice(pointer, length))));
    }
}
