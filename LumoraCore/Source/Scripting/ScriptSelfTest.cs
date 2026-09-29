// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumora.Script;
using Lumora.Wasm;

namespace Lumora.Core.Scripting;

public readonly record struct ScriptSelfTestResult(string Name, bool Passed, string Detail, double Milliseconds);

// A quick check of Loom on this machine, for anyone who opens the diagnostics panel: the WebAssembly
// engine decodes, runs and stops what it should, how fast it goes here, and a graph compiles, loads and
// runs from end to end. Every step runs on the world thread and is held to tens of milliseconds, so a
// panel can run one a frame without a hitch anyone notices. Nothing here touches a world. -xlinka
public static class ScriptSelfTest
{
    private const double SpeedTargetMs = 20.0;

    public static IReadOnlyList<(string Name, Func<ScriptSelfTestResult> Run)> Steps { get; } = new (string, Func<ScriptSelfTestResult>)[]
    {
        ("Engine", EngineRoundTrip),
        ("Interpreter speed", InterpreterSpeed),
        ("Host calls", HostCalls),
        ("Memory access", MemoryAccess),
        ("Runaway stop", RunawayStop),
        ("Trap recovery", TrapRecovery),
        ("Deep recursion", DeepRecursion),
        ("Memory ceiling", MemoryCeiling),
        ("Node registry", CheckRegistry),
        ("Graph end to end", GraphEndToEnd),
    };

    // A graph compiled on its own, past the cache, and what it took.
    public static CompileResult CompileFresh(ScriptGraph graph, out double milliseconds)
    {
        var registry = ScriptEnvironment.Registry;
        long start = Stopwatch.GetTimestamp();
        var result = ScriptCompiler.Compile(graph, registry);
        milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return result;
    }

    private static ScriptInstanceOptions Options => ScriptInstanceOptions.Default;

    private static byte[]? _module;

    // One small module holds every probe. The host call it imports counts how often it was called.
    private static byte[] Module => _module ??= BuildModule();

    private static byte[] BuildModule()
    {
        var i32 = WasmValueType.I32;
        var none = Array.Empty<WasmValueType>();
        var module = new WasmModuleBuilder();
        int tick = module.ImportFunction("diag", "tick", WasmFuncType.Empty);
        module.AddMemory(1);

        var add = module.AddFunction(new WasmFuncType(new[] { i32, i32 }, new[] { i32 }));
        add.LocalGet(0).LocalGet(1).Op(WasmOp.I32Add);
        module.Export("add", WasmExternKind.Function, add.Index);

        // Adds every number below n, one loop turn each.
        var spin = module.AddFunction(new WasmFuncType(new[] { i32 }, new[] { i32 }));
        int i = spin.AddLocal(i32);
        int acc = spin.AddLocal(i32);
        spin.Block().Loop()
            .LocalGet(i).LocalGet(0).Op(WasmOp.I32GeS).BrIf(1)
            .LocalGet(acc).LocalGet(i).Op(WasmOp.I32Add).LocalSet(acc)
            .LocalGet(i).I32Const(1).Op(WasmOp.I32Add).LocalSet(i)
            .Br(0)
            .End().End()
            .LocalGet(acc);
        module.Export("spin", WasmExternKind.Function, spin.Index);

        var ticks = module.AddFunction(new WasmFuncType(new[] { i32 }, none));
        int t = ticks.AddLocal(i32);
        ticks.Block().Loop()
            .LocalGet(t).LocalGet(0).Op(WasmOp.I32GeS).BrIf(1)
            .Call(tick)
            .LocalGet(t).I32Const(1).Op(WasmOp.I32Add).LocalSet(t)
            .Br(0)
            .End().End();
        module.Export("ticks", WasmExternKind.Function, ticks.Index);

        // Writes every word of the first page with its own address, reads them all back and sums them,
        // rounds times over.
        var sweep = module.AddFunction(new WasmFuncType(new[] { i32 }, new[] { i32 }));
        int round = sweep.AddLocal(i32);
        int at = sweep.AddLocal(i32);
        int sum = sweep.AddLocal(i32);
        sweep.Block().Loop()
            .LocalGet(round).LocalGet(0).Op(WasmOp.I32GeS).BrIf(1)
            .I32Const(0).LocalSet(at)
            .Block().Loop()
                .LocalGet(at).I32Const(65536).Op(WasmOp.I32GeS).BrIf(1)
                .LocalGet(at).LocalGet(at).Memory(WasmOp.I32Store, 2, 0)
                .LocalGet(at).I32Const(4).Op(WasmOp.I32Add).LocalSet(at)
                .Br(0)
            .End().End()
            .I32Const(0).LocalSet(at)
            .Block().Loop()
                .LocalGet(at).I32Const(65536).Op(WasmOp.I32GeS).BrIf(1)
                .LocalGet(sum).LocalGet(at).Memory(WasmOp.I32Load, 2, 0).Op(WasmOp.I32Add).LocalSet(sum)
                .LocalGet(at).I32Const(4).Op(WasmOp.I32Add).LocalSet(at)
                .Br(0)
            .End().End()
            .LocalGet(round).I32Const(1).Op(WasmOp.I32Add).LocalSet(round)
            .Br(0)
            .End().End()
            .LocalGet(sum);
        module.Export("sweep", WasmExternKind.Function, sweep.Index);

        var div = module.AddFunction(new WasmFuncType(new[] { i32, i32 }, new[] { i32 }));
        div.LocalGet(0).LocalGet(1).Op(WasmOp.I32DivS);
        module.Export("div", WasmExternKind.Function, div.Index);

        var forever = module.AddFunction(WasmFuncType.Empty);
        forever.Loop().Br(0).End();
        module.Export("forever", WasmExternKind.Function, forever.Index);

        var deep = module.AddFunction(WasmFuncType.Empty);
        deep.Call(deep.Index);
        module.Export("deep", WasmExternKind.Function, deep.Index);

        var grow = module.AddFunction(new WasmFuncType(new[] { i32 }, new[] { i32 }));
        grow.LocalGet(0).MemoryGrow();
        module.Export("grow", WasmExternKind.Function, grow.Index);

        return module.ToBytes();
    }

    private sealed class Probe
    {
        public readonly WasmStore Store = new();
        public WasmInstance Instance = null!;
        public long Ticks;
    }

    // Decoded, validated and linked the way a script's module is, under a script's limits.
    private static Probe Load(out double decodeMs, out double linkMs)
    {
        long start = Stopwatch.GetTimestamp();
        var module = WasmModule.Decode(Module);
        decodeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        start = Stopwatch.GetTimestamp();
        var probe = new Probe();
        var linker = new WasmLinker(probe.Store);
        linker.DefineFunction("diag", "tick", WasmFuncType.Empty, _ => probe.Ticks++);
        probe.Instance = linker.Instantiate(module, new WasmInstanceLimits
        {
            MaxMemoryPages = Options.MaxMemoryPages,
            MaxCallDepth = Options.MaxCallDepth,
        });
        linkMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return probe;
    }

    // Returns the fuel the call used.
    private static long Call(Probe probe, string name, ulong[] arguments, Span<ulong> results, long fuel = long.MaxValue,
        long deadline = long.MaxValue)
    {
        var function = probe.Instance.GetFunction(name) ?? throw new InvalidOperationException("no export " + name);
        long left = probe.Store.Invoke(function, arguments, results, fuel, Options.MaxCallDepth, deadline);
        return fuel - left;
    }

    private static ulong[] Args(params int[] values)
    {
        var lanes = new ulong[values.Length];
        for (int i = 0; i < values.Length; i++)
            lanes[i] = (uint)values[i];
        return lanes;
    }

    private static ScriptSelfTestResult Pass(string name, string detail, double ms) => new(name, true, detail, ms);

    private static ScriptSelfTestResult Fail(string name, string detail, double ms) => new(name, false, detail, ms);

    private static string Rate(double perSecond) => perSecond >= 1e6 ? $"{perSecond / 1e6:0.0} million" : $"{perSecond / 1e3:0.0} thousand";

    private static ScriptSelfTestResult EngineRoundTrip()
    {
        const string name = "Engine";
        long start = Stopwatch.GetTimestamp();
        var probe = Load(out double decodeMs, out double linkMs);
        Span<ulong> result = stackalloc ulong[1];
        Call(probe, "add", Args(40, 2), result);
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (int)result[0] == 42
            ? Pass(name, $"decode and validate {decodeMs:0.###} ms, link {linkMs:0.###} ms, 40 + 2 = 42", ms)
            : Fail(name, $"40 + 2 came back {(int)result[0]}", ms);
    }

    // Loop turns doubled until one run takes long enough to time well.
    private static ScriptSelfTestResult InterpreterSpeed()
    {
        const string name = "Interpreter speed";
        var probe = Load(out _, out _);
        Span<ulong> result = stackalloc ulong[1];
        Call(probe, "spin", Args(10_000), result);

        int turns = 100_000;
        double ms;
        long fuel;
        while (true)
        {
            long start = Stopwatch.GetTimestamp();
            fuel = Call(probe, "spin", Args(turns), result);
            ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (ms >= SpeedTargetMs || turns >= 1 << 27)
                break;
            turns *= 2;
        }
        int expected = unchecked((int)((long)turns * (turns - 1) / 2));
        if ((int)result[0] != expected)
            return Fail(name, $"the sum below {turns:N0} came back wrong", ms);
        double seconds = ms / 1000.0;
        return Pass(name, $"{Rate(turns / seconds)} loop turns/s, {Rate(fuel / seconds)} fuel/s ({turns:N0} turns in {ms:0.0} ms)", ms);
    }

    private static ScriptSelfTestResult HostCalls()
    {
        const string name = "Host calls";
        var probe = Load(out _, out _);
        Span<ulong> none = Span<ulong>.Empty;
        Call(probe, "ticks", Args(1_000), none);

        int calls = 20_000;
        double ms;
        while (true)
        {
            probe.Ticks = 0;
            long start = Stopwatch.GetTimestamp();
            Call(probe, "ticks", Args(calls), none);
            ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (ms >= SpeedTargetMs * 0.5 || calls >= 1 << 24)
                break;
            calls *= 2;
        }
        if (probe.Ticks != calls)
            return Fail(name, $"{calls:N0} calls made, {probe.Ticks:N0} arrived", ms);
        return Pass(name, $"{Rate(calls / (ms / 1000.0))} calls into the engine/s ({calls:N0} in {ms:0.0} ms)", ms);
    }

    private static ScriptSelfTestResult MemoryAccess()
    {
        const string name = "Memory access";
        var probe = Load(out _, out _);
        Span<ulong> result = stackalloc ulong[1];
        Call(probe, "sweep", Args(1), result);

        int rounds = 4;
        double ms;
        while (true)
        {
            long start = Stopwatch.GetTimestamp();
            Call(probe, "sweep", Args(rounds), result);
            ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (ms >= SpeedTargetMs * 0.5 || rounds >= 1 << 16)
                break;
            rounds *= 2;
        }
        // Each round adds 4 * (0 + 1 + ... + 16383), wrapping the way i32 does.
        int expected = unchecked((int)(rounds * 536_838_144L));
        if ((int)result[0] != expected)
            return Fail(name, "words read back did not match what was written", ms);
        double bytes = rounds * 65536.0 * 2.0;
        return Pass(name, $"{bytes / (ms / 1000.0) / (1024 * 1024):0} MiB/s written and read back ({rounds:N0} passes over 64 KiB)", ms);
    }

    private static ScriptSelfTestResult RunawayStop()
    {
        const string name = "Runaway stop";
        var probe = Load(out _, out _);
        const int clock = 50;
        long start = Stopwatch.GetTimestamp();
        try
        {
            Call(probe, "forever", Array.Empty<ulong>(), Span<ulong>.Empty, long.MaxValue, Environment.TickCount64 + clock);
        }
        catch (WasmTrapException trap) when (trap.Code == WasmTrapCode.OutOfTime)
        {
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return Pass(name, $"an endless loop given {clock} ms on the clock stops after {ms:0.0} ms, {long.MaxValue - trap.FuelLeft:N0} fuel in "
                + $"(an event call gets {Options.MaxCallMilliseconds / 1000.0:0.#} s)", ms);
        }
        catch (WasmTrapException trap)
        {
            return Fail(name, "stopped by " + trap.Code + " instead of the clock", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return Fail(name, "an endless loop came back", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private static ScriptSelfTestResult TrapRecovery()
    {
        const string name = "Trap recovery";
        var probe = Load(out _, out _);
        Span<ulong> result = stackalloc ulong[1];
        long start = Stopwatch.GetTimestamp();
        WasmTrapCode? code = null;
        try
        {
            Call(probe, "div", Args(1, 0), result);
        }
        catch (WasmTrapException trap)
        {
            code = trap.Code;
        }
        if (code != WasmTrapCode.IntegerDivideByZero)
            return Fail(name, code == null ? "dividing by zero did not trap" : "dividing by zero trapped as " + code, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        Call(probe, "add", Args(2, 3), result);
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (int)result[0] == 5
            ? Pass(name, "a divide by zero traps and the next call runs normally", ms)
            : Fail(name, "the call after a trap came back wrong", ms);
    }

    private static ScriptSelfTestResult DeepRecursion()
    {
        const string name = "Deep recursion";
        var probe = Load(out _, out _);
        long start = Stopwatch.GetTimestamp();
        try
        {
            Call(probe, "deep", Array.Empty<ulong>(), Span<ulong>.Empty);
        }
        catch (WasmTrapException trap) when (trap.Code == WasmTrapCode.CallStackExhausted)
        {
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return Pass(name, $"endless recursion traps at {Options.MaxCallDepth} calls deep, the process never sees it", ms);
        }
        catch (WasmTrapException trap)
        {
            return Fail(name, "trapped as " + trap.Code, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return Fail(name, "endless recursion came back", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private static ScriptSelfTestResult MemoryCeiling()
    {
        const string name = "Memory ceiling";
        var probe = Load(out _, out _);
        Span<ulong> result = stackalloc ulong[1];
        long start = Stopwatch.GetTimestamp();
        Call(probe, "grow", Args(1), result);
        int first = (int)result[0];
        Call(probe, "grow", Args((int)Options.MaxMemoryPages), result);
        int past = (int)result[0];
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long ceiling = Options.MaxMemoryPages * 64L * 1024;
        return first == 1 && past == -1
            ? Pass(name, $"memory grows up to {ceiling >> 20} MiB and a grow past it fails cleanly", ms)
            : Fail(name, $"grow returned {first} and {past}, expected 1 and -1", ms);
    }

    private static ScriptSelfTestResult CheckRegistry()
    {
        const string name = "Node registry";
        long start = Stopwatch.GetTimestamp();
        var registry = ScriptEnvironment.Registry;
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double scan = ScriptEnvironment.CompileStats.RegistryScanMs;
        return registry.Problems.Count == 0
            ? Pass(name, $"{registry.Definitions.Count} nodes, scanned in {scan:0.0} ms on first use", ms)
            : Fail(name, $"{registry.Problems.Count} left out, first: {registry.Problems[0]}", ms);
    }

    // Sums 0..999 in a For, then formats the sum into text: flow, data, a loop, strings and stored values.
    private const string SampleGraph = """
        {"format":"loom.graph/1","nodes":[
          {"id":"start","type":"OnStart"},
          {"id":"seq","type":"Sequence"},
          {"id":"sum","type":"StoredValue","sig":"int"},
          {"id":"for","type":"For","values":{"count":1000}},
          {"id":"add","type":"Add","sig":"int,int"},
          {"id":"w","type":"WriteValue","sig":"int","values":{"var":"sum"}},
          {"id":"text","type":"StoredValue","sig":"string"},
          {"id":"fmt","type":"Format","sig":"string,int","values":{"format":"sum {0}"}},
          {"id":"wt","type":"WriteValue","sig":"string","values":{"var":"text"}}],
         "flow":[["start.next","seq.in"],["seq.out0","for.in"],["for.body","w.in"],["seq.out1","wt.in"]],
         "data":[["sum.value","add.a"],["for.index","add.b"],["add.result","w.value"],["sum.value","fmt.arg0"],["fmt.result","wt.value"]]}
        """;

    private sealed class SelfTestContext : ScriptContextBase
    {
        protected override bool AllowsCapability(Capability capability) => capability is Capability.Core or Capability.Events;

        protected override void WriteLog(string text)
        {
        }
    }

    private static ScriptSelfTestResult GraphEndToEnd()
    {
        const string name = "Graph end to end";
        long start = Stopwatch.GetTimestamp();
        var graph = ScriptGraph.Parse(SampleGraph);
        var compiled = CompileFresh(graph, out double compileMs);
        if (!compiled.Success)
            return Fail(name, "the sample graph does not compile: " + string.Join("; ", compiled.Errors), Stopwatch.GetElapsedTime(start).TotalMilliseconds);

        long loading = Stopwatch.GetTimestamp();
        var instance = ScriptInstance.Create(compiled, new SelfTestContext(), Array.Empty<object?>());
        double loadMs = Stopwatch.GetElapsedTime(loading).TotalMilliseconds;

        long running = Stopwatch.GetTimestamp();
        var call = instance.Raise("start", new ScriptStarted());
        double runMs = Stopwatch.GetElapsedTime(running).TotalMilliseconds;
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (!call.Ok)
            return Fail(name, "the sample graph faulted: " + call, ms);
        if (!Equals(instance.ReadVariable("sum"), 499_500) || instance.ReadVariable("text") as string != "sum 499500")
            return Fail(name, $"the sample graph worked out {instance.ReadVariable("sum")} and '{instance.ReadVariable("text")}'", ms);
        return Pass(name, $"{graph.Nodes.Count} nodes: compile {compileMs:0.0} ms, load {loadMs:0.00} ms, run {runMs:0.00} ms on {call.FuelUsed:N0} fuel", ms);
    }
}
