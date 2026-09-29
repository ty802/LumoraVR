// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumora.Script;

namespace Lumora.Core.Scripting;

public readonly record struct ScriptBenchmarkResult(string Group, string Name, bool Ok, long Ops, double Milliseconds, long Fuel, string Detail)
{
    public double MicrosecondsPerOp => Ops > 0 ? Milliseconds * 1000.0 / Ops : 0;

    public double OpsPerSecond => Milliseconds > 0 ? Ops / (Milliseconds / 1000.0) : 0;

    public double FuelPerOp => Ops > 0 ? (double)Fuel / Ops : 0;
}

// How fast Loom does the everyday things on this machine, for slots and for people: reading and moving a
// slot, making and removing one, walking children, finding one by name, casting rays, counting and
// reading people. Every case is a small graph run as a Local script, as the world, on a stage of local
// slots built for the run and removed after it, so nothing a case does leaves this machine or touches
// the world. A case is probed on a few turns, then timed on as many as take about TargetMs in one call,
// and reads per turn beside the empty loop turn, the floor under all of them. -xlinka
public sealed class ScriptBenchmark : IDisposable
{
    public const int CrowdSize = 100;
    private const double TargetMs = 12.0;
    private const int ProbeTurns = 32;

    public enum Per
    {
        Turn,
        Child,
        Person,
    }

    public readonly record struct Case(string Group, string Name, string Nodes, string Flow, string Data, int MaxTurns, Per Per = Per.Turn);

    // Bindings: 0 a lone slot, 1 a slot with CrowdSize children, 2 an empty slot to make things under.
    public static IReadOnlyList<Case> Cases { get; } = new Case[]
    {
        new("Loop", "Empty loop turn",
            """{"id":"v","type":"StoredValue","sig":"int"},{"id":"w","type":"WriteValue","sig":"int","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["for.index","w.value"]""", 1_000_000),

        new("Slots", "Read a position",
            """{"id":"pos","type":"SlotPosition","values":{"slot":{"binding":0}}},{"id":"v","type":"StoredValue","sig":"float3"},{"id":"w","type":"WriteValue","sig":"float3","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["pos.position","w.value"]""", 500_000),
        new("Slots", "Move a slot",
            """{"id":"tf","type":"ToFloat","sig":"int"},{"id":"mv","type":"MakeVector","sig":"float,float,float"},{"id":"set","type":"SetSlotLocalPosition","values":{"slot":{"binding":0}}}""",
            """["for.body","set.in"]""",
            """["for.index","tf.value"],["tf.result","mv.x"],["mv.vector","set.position"]""", 500_000),
        new("Slots", "Read a name",
            """{"id":"nm","type":"SlotName","values":{"slot":{"binding":0}}},{"id":"v","type":"StoredValue","sig":"string"},{"id":"w","type":"WriteValue","sig":"string","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["nm.name","w.value"]""", 500_000),
        new("Slots", "Find a child by name",
            """{"id":"fc","type":"FindChild","values":{"slot":{"binding":1},"name":"Child 50"}},{"id":"v","type":"StoredValue","sig":"slot"},{"id":"w","type":"WriteValue","sig":"slot","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["fc.child","w.value"]""", 200_000),
        new("Slots", "Walk children",
            """{"id":"each","type":"ForEachChild","values":{"slot":{"binding":1}}},{"id":"nm","type":"SlotName"},{"id":"v","type":"StoredValue","sig":"string"},{"id":"w","type":"WriteValue","sig":"string","values":{"var":"v"}}""",
            """["for.body","each.in"],["each.body","w.in"]""",
            """["each.child","nm.slot"],["nm.name","w.value"]""", 5_000, Per.Child),
        new("Slots", "Make and remove a slot",
            """{"id":"mk","type":"CreateChild","values":{"parent":{"binding":2},"name":"Bench"}},{"id":"rm","type":"DestroySlot"}""",
            """["for.body","mk.in"],["mk.next","rm.in"]""",
            """["mk.child","rm.slot"]""", 30),
        new("Slots", "Raycast",
            """{"id":"ray","type":"CastRay","values":{"origin":[0,2,0],"direction":[0,-1,0]}},{"id":"v","type":"StoredValue","sig":"bool"},{"id":"w","type":"WriteValue","sig":"bool","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["ray.hit","w.value"]""", 60),

        new("People", "Count people",
            """{"id":"uc","type":"UserCount"},{"id":"v","type":"StoredValue","sig":"int"},{"id":"w","type":"WriteValue","sig":"int","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["uc.count","w.value"]""", 500_000),
        new("People", "Read a name",
            """{"id":"me","type":"MachineUser"},{"id":"un","type":"UserName"},{"id":"v","type":"StoredValue","sig":"string"},{"id":"w","type":"WriteValue","sig":"string","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["me.user","un.user"],["un.name","w.value"]""", 500_000),
        new("People", "Read a head position",
            """{"id":"me","type":"MachineUser"},{"id":"hp","type":"HeadPosition"},{"id":"v","type":"StoredValue","sig":"float3"},{"id":"w","type":"WriteValue","sig":"float3","values":{"var":"v"}}""",
            """["for.body","w.in"]""",
            """["me.user","hp.user"],["hp.position","w.value"]""", 500_000),
        new("People", "Walk everyone",
            """{"id":"each","type":"ForEachUser"},{"id":"hp","type":"HeadPosition"},{"id":"v","type":"StoredValue","sig":"float3"},{"id":"w","type":"WriteValue","sig":"float3","values":{"var":"v"}}""",
            """["for.body","each.in"],["each.body","w.in"]""",
            """["each.user","hp.user"],["hp.position","w.value"]""", 100_000, Per.Person),
    };

    private readonly World _world;
    private readonly Slot _stage;
    private readonly object?[] _bindings;

    // On the world thread. The stage is local, so it exists on this machine only.
    public ScriptBenchmark(World world)
    {
        _world = world;
        _stage = world.RootSlot.AddLocalSlot("Loom Benchmark");
        _stage.Persistent.Value = false;
        var target = _stage.AddSlot("Target");
        var crowd = _stage.AddSlot("Crowd");
        for (int i = 0; i < CrowdSize; i++)
            crowd.AddSlot("Child " + i);
        var bench = _stage.AddSlot("Workbench");
        _bindings = new object?[] { target, crowd, bench };
    }

    public static string Json(Case bench)
        => "{\"format\":\"loom.graph/1\",\"bindings\":[{\"name\":\"target\",\"type\":\"slot\"},{\"name\":\"crowd\",\"type\":\"slot\"},"
            + "{\"name\":\"bench\",\"type\":\"slot\"}],\"nodes\":[{\"id\":\"go\",\"type\":\"OnStart\"},"
            + "{\"id\":\"n\",\"type\":\"StoredValue\",\"sig\":\"int\"},{\"id\":\"for\",\"type\":\"For\"}," + bench.Nodes + "],"
            + "\"flow\":[[\"go.next\",\"for.in\"]," + bench.Flow + "],"
            + "\"data\":[[\"n.value\",\"for.count\"]," + bench.Data + "]}";

    public ScriptBenchmarkResult Run(Case bench)
    {
        ScriptRunner runner;
        try
        {
            runner = ScriptRunner.Start(_world, ScriptGraph.Parse(Json(bench)), ScriptPrincipal.WorldContent, _bindings, self: _stage, local: true);
        }
        catch (Exception ex)
        {
            return Failed(bench, ex.Message);
        }

        try
        {
            int turns = System.Math.Min(bench.MaxTurns, ProbeTurns);
            var probe = Time(runner, turns);
            if (!probe.Call.Ok)
                return Failed(bench, probe.Call.ToString());

            long fuelPerTurn = System.Math.Max(1, probe.Call.FuelUsed / turns);
            double msPerTurn = probe.Ms / turns;
            long byFuel = ScriptInstanceOptions.Default.FuelPerCall / 2 / fuelPerTurn;
            long byTime = msPerTurn > 0 ? (long)(TargetMs / msPerTurn) : bench.MaxTurns;
            turns = (int)System.Math.Clamp(System.Math.Min(byFuel, byTime), turns, bench.MaxTurns);

            var timed = Time(runner, turns);
            if (!timed.Call.Ok)
                return Failed(bench, timed.Call.ToString());
            long ops = (long)turns * Inner(bench, runner);
            return new ScriptBenchmarkResult(bench.Group, bench.Name, true, ops, timed.Ms, timed.Call.FuelUsed,
                bench.Per == Per.Turn ? $"{turns:N0} turns" : $"{turns:N0} turns of {Inner(bench, runner)}");
        }
        finally
        {
            runner.Stop();
        }
    }

    private int Inner(Case bench, ScriptRunner runner) => bench.Per switch
    {
        Per.Child => CrowdSize,
        Per.Person => System.Math.Max(1, ScriptReads.Users(runner.Context).Count),
        _ => 1,
    };

    private static (ScriptCallResult Call, double Ms) Time(ScriptRunner runner, int turns)
    {
        runner.Instance.WriteVariable("n", turns);
        long start = Stopwatch.GetTimestamp();
        var call = runner.Raise("go", new ScriptStarted());
        return (call, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private static ScriptBenchmarkResult Failed(Case bench, string why) => new(bench.Group, bench.Name, false, 0, 0, 0, why);

    public void Dispose()
    {
        if (!_stage.IsDestroyed)
            _stage.Destroy();
    }
}
