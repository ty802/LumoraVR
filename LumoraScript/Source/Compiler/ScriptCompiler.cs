// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumora.Wasm;

namespace Lumora.Script;

// graph.json to module.wasm. Every node resolves against the registry, every wire is type checked,
// flow cycles must pass through a loop node, and the module comes out with one export per event, a
// source map from code offset to node, and a manifest. The result is decoded by the engine's own
// validator before it is handed back: the compiler is trusted no more than any other module source.
//
// MODULE LAYOUT
//   import lumora:rt.str_utf8 (ptr, len) -> handle   makes the constant strings at start
//   import lumora.<overload>                         one per node overload that calls into C#
//   import lumora:rt.delay (timer, seconds)          asks for a timer, when the graph has a Delay
//   an await node's Start is a node import that takes the await's number ahead of its inputs
//   import lumora:rt.now () -> seconds               the world's clock, when the graph has a Cooldown
//   import lumora:rt.each_count/each_item            what a For Each walks, when the graph has one
//   import lumora:rt.each_query (source, floats) -> handle   what a searching For Each found
//   memory: [0, 1024) continuation stack, then the constant string bytes
//   globals: one handle per constant string, then the lanes of every stored variable, Display and Pulse
//   start: creates the constant strings
//   exports: on.<node id> per event, after.<node id> per Delay, await.<node id>.<exit> per exit an await's
//            answer can run, taking the payload lanes, var.<node id>.<lane> per lane of a
//            stored variable, Display or Pulse, show.<node id> per Display that needs no flow, each on
//            its own so one that fails leaves the rest showing, trace.<node id>.<exit> per wired flow
//            exit, counting the flow that leaves by it, and mem.<node id> per Once, Toggle and Cooldown,
//            what it remembers, for a live swap to carry over -xlinka
public sealed class ScriptCompiler
{
    // What a script's module is read and checked against. The engine's own caps are for modules from
    // somewhere else; a script's module is only ever one this compiler just wrote, as big as its graph.
    public static readonly WasmLimits ModuleLimits = new()
    {
        MaxModuleBytes = int.MaxValue,
        MaxTypes = int.MaxValue,
        MaxFunctions = int.MaxValue,
        MaxImports = int.MaxValue,
        MaxExports = int.MaxValue,
        MaxGlobals = int.MaxValue,
        MaxElementSegments = int.MaxValue,
        MaxElementItems = int.MaxValue,
        MaxDataSegments = int.MaxValue,
        MaxTypeArity = int.MaxValue,
        MaxLocalsPerFunction = int.MaxValue,
        MaxFunctionBodyBytes = int.MaxValue,
        MaxBlockDepth = int.MaxValue,
        MaxBranchTableEntries = int.MaxValue,
        MaxOperandStack = int.MaxValue,
    };

    public const string NodeModule = "lumora";
    public const string RuntimeModule = "lumora:rt";
    public const string StringImport = "str_utf8";
    public const string TimerImport = "delay";
    public const string NowImport = "now";
    public const string EachCountImport = "each_count";
    public const string EachItemImport = "each_item";
    public const string EachQueryImport = "each_query";
    public const float MinIntervalSeconds = 0.05f;
    public const string ManifestSection = "lumora.manifest";
    public const string EventExportPrefix = "on.";
    public const string VariableExportPrefix = "var.";
    public const string TimerExportPrefix = "after.";
    public const string AwaitExportPrefix = "await.";
    public const string DisplayExportPrefix = "show.";
    public const string TraceExportPrefix = "trace.";
    public const string MemoryExportPrefix = "mem.";

    internal const int StackBytes = 1024;

    // A search's source, then every float it can carry, unused ones zero.
    internal static readonly WasmFuncType EachQueryType = new(
        new[] { WasmValueType.I32 }.Concat(Enumerable.Repeat(WasmValueType.F32, FlowBuilder.MaxQueryLanes)).ToArray(),
        new[] { WasmValueType.I32 });

    private readonly ScriptGraph _graph;
    private readonly NodeRegistry _registry;
    private readonly List<CompileDiagnostic> _diagnostics = new();
    private readonly List<ResolvedNode> _nodes = new();
    private readonly Dictionary<string, ResolvedNode> _byId = new(StringComparer.Ordinal);
    private readonly List<ScriptBindingEntry> _bindings = new();

    private ScriptCompiler(ScriptGraph graph, NodeRegistry registry)
    {
        _graph = graph;
        _registry = registry;
    }

    // Never throws: a compiler fault comes back as a failed compile that says so. Safe to run on any
    // thread, several at once: it reads the graph and the registry and writes only its own state.
    public static CompileResult Compile(ScriptGraph graph, NodeRegistry registry)
    {
        var compiler = new ScriptCompiler(graph, registry);
        try
        {
            return compiler.Run();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            compiler.Error(null, "internal: the compiler failed: " + ex.Message);
            string hash;
            try
            {
                hash = graph.ComputeHash();
            }
            catch (Exception)
            {
                hash = string.Empty;
            }
            return CompileResult.Failed(hash, compiler._diagnostics);
        }
    }

    private CompileResult Run()
    {
        var hash = _graph.ComputeHash();

        ResolveBindings();
        ResolveNodes();
        if (HasErrors)
            return CompileResult.Failed(hash, _diagnostics);

        ResolveWires();
        ResolveConstants();
        ResolveSteps();
        if (HasErrors)
            return CompileResult.Failed(hash, _diagnostics);

        CheckFlowCycles();
        CheckDataCycles();
        if (HasErrors)
            return CompileResult.Failed(hash, _diagnostics);

        return Emit(hash);
    }

    private bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    // A node shared by several flows is lowered once per flow, so the same complaint can come up twice.
    private void Error(string? node, string message)
    {
        var diagnostic = new CompileDiagnostic(DiagnosticSeverity.Error, node, message);
        if (!_diagnostics.Contains(diagnostic))
            _diagnostics.Add(diagnostic);
    }

    // A node this build lacks or has only older: the graph is fine, this machine is behind.
    private void Behind(string node, string message)
        => _diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Error, node, message) { NeedsNewerBuild = true });

    private void Warn(string? node, string message) => _diagnostics.Add(new CompileDiagnostic(DiagnosticSeverity.Warning, node, message));

    private void ResolveBindings()
    {
        foreach (var binding in _graph.Bindings)
        {
            if (!ScriptTypes.TryGet(binding.Type, out var type))
            {
                Error(null, $"binding '{binding.Name}' has unknown type '{binding.Type}'");
                continue;
            }
            if (!type.IsHandle || type == ScriptTypes.String)
            {
                Error(null, $"binding '{binding.Name}' is a {type.Name}; a binding holds a world object");
                continue;
            }
            _bindings.Add(new ScriptBindingEntry(binding.Name, type));
        }
    }

    private void ResolveNodes()
    {
        for (int i = 0; i < _graph.Nodes.Count; i++)
        {
            var node = _graph.Nodes[i];
            if (!_registry.TryGetDefinition(node.Type, out var definition))
            {
                Behind(node.Id, $"unknown node type '{node.Type}'");
                continue;
            }
            if (node.Version > definition.Version)
            {
                Behind(node.Id, $"was saved by a newer {definition.Name} (v{node.Version}, this build has v{definition.Version})");
                continue;
            }
            if (node.Version < definition.Version)
            {
                Error(node.Id, $"is {definition.Name} v{node.Version} and needs migrating to v{definition.Version}");
                continue;
            }
            if (!_registry.TryResolve(definition, node.Signature, out var overload, out var error))
            {
                Error(node.Id, error);
                continue;
            }

            var resolved = new ResolvedNode(node, i, overload);
            if (overload.Shape?.Kind == FlowShapeKind.ListEach && !ResolveListHelpers(resolved))
                continue;
            if (overload.Shape?.Kind == FlowShapeKind.Scope && !ResolveScopeHelpers(resolved))
                continue;
            if (overload.Shape?.Kind == FlowShapeKind.Change && !ResolveChangeHelper(resolved))
                continue;
            _nodes.Add(resolved);
            _byId.Add(node.Id, resolved);
        }
    }

    // A list For Each counts and reads its list through the Count and Get At nodes for its item type.
    private bool ResolveListHelpers(ResolvedNode node)
    {
        var item = node.Overload.TypeArguments.Count == 1 ? node.Overload.TypeArguments[0].Name : null;
        var helpers = new NodeOverload[2];
        string[] names = { "ListCount", "ListGetAt" };
        for (int i = 0; i < names.Length; i++)
        {
            if (item == null || !_registry.TryGetDefinition(names[i], out var definition)
                || !_registry.TryResolve(definition, item, out helpers[i], out _) || !helpers[i].IsHostCall)
            {
                Error(node.Id, $"cannot walk a list of {item ?? "nothing"} on this build");
                return false;
            }
        }
        node.Helpers = helpers;
        return true;
    }

    // A scope opens and closes through two action nodes: enter takes the scope's own inputs in order, leave
    // takes none, and neither gives anything back.
    private bool ResolveScopeHelpers(ResolvedNode node)
    {
        var shape = node.Overload.Shape!;
        string[] names = { shape.Enter ?? string.Empty, shape.Leave ?? string.Empty };
        var helpers = new NodeOverload[2];
        for (int i = 0; i < names.Length; i++)
        {
            if (!_registry.TryGetDefinition(names[i], out var definition) || definition.Kind != NodeKind.Action
                || !_registry.TryResolve(definition, null, out helpers[i], out _) || !helpers[i].IsHostCall || helpers[i].Outputs.Count > 0)
            {
                Error(node.Id, $"cannot open or close {node.Overload.Definition.Name} on this build");
                return false;
            }
        }
        var inputs = node.Overload.Inputs;
        var takes = helpers[0].Inputs;
        bool fits = takes.Count == inputs.Count && helpers[1].Inputs.Count == 0;
        for (int i = 0; fits && i < inputs.Count; i++)
            fits = takes[i].Type == inputs[i].Type;
        if (!fits)
        {
            Error(node.Id, $"{names[0]} does not take what {node.Overload.Definition.Name} is given");
            return false;
        }
        node.Helpers = helpers;
        return true;
    }

    // A Change works its difference out through a value node that takes the input type twice and gives it
    // back: a host call, or one instruction a lane.
    private bool ResolveChangeHelper(ResolvedNode node)
    {
        var shape = node.Overload.Shape!;
        var type = node.Overload.Inputs[shape.Input].Type;
        if (!_registry.TryGetDefinition(shape.Helper ?? string.Empty, out var definition) || definition.Kind != NodeKind.Value
            || !_registry.TryResolve(definition, $"{type.Name},{type.Name}", out var helper, out _)
            || helper.Outputs.Count != 1 || helper.Outputs[0].Type != type || helper.Method == null)
        {
            Error(node.Id, $"cannot work out a change of {type.Name} on this build");
            return false;
        }
        node.Helpers = new[] { helper };
        return true;
    }

    private void ResolveWires()
    {
        foreach (var wire in _graph.Flow)
        {
            if (!Ends(wire, out var from, out var to))
                continue;
            int exit = IndexOf(from.Overload.FlowOutputs, wire.FromPort);
            if (exit < 0)
            {
                Error(from.Id, $"has no flow output '{wire.FromPort}'");
                continue;
            }
            if (IndexOf(to.Overload.FlowInputs, wire.ToPort) < 0)
            {
                Error(to.Id, $"has no flow input '{wire.ToPort}'");
                continue;
            }
            if (from.Exits[exit] != null)
            {
                Error(from.Id, $"flow output '{wire.FromPort}' is wired twice; a Sequence runs several chains");
                continue;
            }
            from.Exits[exit] = to;
        }

        foreach (var wire in _graph.Data)
        {
            if (!Ends(wire, out var from, out var to))
                continue;
            int output = from.Overload.OutputIndex(wire.FromPort);
            if (output < 0)
            {
                Error(from.Id, $"has no output '{wire.FromPort}'");
                continue;
            }
            int input = to.Overload.InputIndex(wire.ToPort);
            if (input < 0)
            {
                Error(to.Id, $"has no input '{wire.ToPort}'");
                continue;
            }
            var produced = from.Overload.Outputs[output].Type;
            var wanted = to.Overload.Inputs[input].Type;
            if (!ScriptTypes.Accepts(wanted, produced))
            {
                Error(to.Id, $"input '{wire.ToPort}' takes {wanted.Name}, {from.Id}.{wire.FromPort} gives {produced.Name}");
                continue;
            }
            if (to.Inputs[input] != null)
            {
                Error(to.Id, $"input '{wire.ToPort}' is wired twice");
                continue;
            }
            to.Inputs[input] = DataSource.Wired(from, output);
        }
    }

    private bool Ends(GraphWire wire, out ResolvedNode from, out ResolvedNode to)
    {
        to = null!;
        if (!_byId.TryGetValue(wire.FromNode, out from!))
        {
            Error(wire.FromNode, $"wire {wire} starts at a node that does not exist");
            return false;
        }
        if (!_byId.TryGetValue(wire.ToNode, out to!))
        {
            Error(wire.ToNode, $"wire {wire} ends at a node that does not exist");
            return false;
        }
        return true;
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == name)
                return i;
        }
        return -1;
    }

    private void ResolveConstants()
    {
        foreach (var node in _nodes)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var inputs = node.Overload.Inputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                var port = inputs[i];
                bool given = node.Source.Values.TryGetValue(port.Name, out var value);
                if (given)
                    used.Add(port.Name);

                if (node.Inputs[i] != null)
                {
                    if (given)
                        Warn(node.Id, $"input '{port.Name}' is wired, so its value is ignored");
                    continue;
                }

                if (given)
                {
                    if (TryConstant(node, port.Name, value!, port.Type, out var constant))
                        node.Inputs[i] = DataSource.Of(constant);
                }
                else if (port.HasDefault)
                {
                    node.Inputs[i] = DataSource.Of(ConstantValue.OfBoxed(port.Type, port.Default));
                }
                else
                {
                    node.Inputs[i] = DataSource.Of(ConstantValue.Default(port.Type));
                }
            }

            switch (node.Kind)
            {
                case NodeKind.StoredVariable:
                    ResolveStored(node, used);
                    break;
                case NodeKind.WriteVariable:
                    ResolveWrite(node, used);
                    break;
                case NodeKind.Event when node.Overload.Definition.TakesSource:
                    ResolveSource(node, used);
                    break;
                case NodeKind.Event when node.Overload.Definition.TakesInterval:
                    ResolveInterval(node, used);
                    break;
            }

            foreach (var key in node.Source.Values.Keys)
            {
                if (!used.Contains(key))
                    Error(node.Id, $"has no input or setting called '{key}'");
            }
        }
    }

    private bool TryConstant(ResolvedNode node, string port, GraphValue value, ScriptType type, out ConstantValue constant)
    {
        if (!ConstantValue.TryConvert(value, type, out constant, out var error))
        {
            Error(node.Id, $"'{port}': {error}");
            return false;
        }
        if (constant.Binding >= 0)
        {
            if (constant.Binding >= _bindings.Count)
            {
                Error(node.Id, $"'{port}' uses binding {constant.Binding}, the graph declares {_bindings.Count}");
                return false;
            }
            if (_bindings[constant.Binding].Type != type)
            {
                Error(node.Id, $"'{port}' takes {type.Name}, binding {constant.Binding} holds {_bindings[constant.Binding].Type.Name}");
                return false;
            }
        }
        return true;
    }

    private void ResolveStored(ResolvedNode node, HashSet<string> used)
    {
        var type = node.Overload.Outputs[0].Type;
        node.VariableName = node.Id;
        if (node.Source.Values.TryGetValue("name", out var name))
        {
            used.Add("name");
            if (name.Kind != GraphValueKind.String || !NodePort.IsValidName(name.String))
                Error(node.Id, "'name' is a plain identifier");
            else
                node.VariableName = name.String;
        }

        node.Initial = ConstantValue.Default(type);
        if (node.Source.Values.TryGetValue("initial", out var initial))
        {
            used.Add("initial");
            if (TryConstant(node, "initial", initial, type, out var constant))
                node.Initial = constant;
        }
    }

    private void ResolveWrite(ResolvedNode node, HashSet<string> used)
    {
        if (!node.Source.Values.TryGetValue("var", out var target) || target.Kind != GraphValueKind.String)
        {
            Error(node.Id, "'var' names the Stored Value node to write");
            return;
        }
        used.Add("var");
        if (!_byId.TryGetValue(target.String!, out var variable) || variable.Kind != NodeKind.StoredVariable)
        {
            Error(node.Id, $"'var' is '{target.String}', which is not a Stored Value node");
            return;
        }
        if (variable.Overload.Outputs[0].Type != node.Overload.Inputs[0].Type)
        {
            Error(node.Id, $"writes {node.Overload.Inputs[0].Type.Name} into {variable.Id}, which holds {variable.Overload.Outputs[0].Type.Name}");
            return;
        }
        node.Variable = variable;
    }

    // Increment and Decrement Value write their stored value plus or minus "by": each is given an Add or
    // Subtract node of its own, fed by the stored value and the step, and writes what that gives. It reads the
    // variable as the write runs, so steps in a loop build on each other.
    private void ResolveSteps()
    {
        foreach (var node in _nodes.ToArray())
        {
            if (node.Kind != NodeKind.WriteVariable || !NodeRegistry.IsStep(node.Overload.Definition) || node.Variable == null)
                continue;
            bool up = node.Overload.Definition.ClrType == typeof(IncrementValue<>);
            var type = node.Overload.Inputs[0].Type;
            if (!_registry.TryGetDefinition(up ? "Add" : "Subtract", out var math)
                || !_registry.TryResolve(math, type.Name + "," + type.Name, out var step, out _) || step.Outputs.Count != 1 || step.Outputs[0].Type != type)
            {
                Error(node.Id, $"cannot step a {type.Name} on this build");
                continue;
            }
            string id = (node.Id.Length > GraphNode.MaxIdLength - 5 ? node.Id[..(GraphNode.MaxIdLength - 5)] : node.Id) + "-step";
            var stepped = new ResolvedNode(new GraphNode(id, math.Name), node.Tag, step);
            stepped.Inputs[0] = DataSource.Wired(node.Variable, 0);
            stepped.Inputs[1] = node.Inputs[0];
            node.Inputs[0] = DataSource.Wired(stepped, 0);
            _nodes.Add(stepped);
        }
    }

    // A sourced event's "source": a bound object, never a constant, never compiled into anything.
    private void ResolveSource(ResolvedNode node, HashSet<string> used)
    {
        if (!node.Source.Values.TryGetValue("source", out var source))
        {
            if (node.Overload.Definition.SourceType != "slot")
                Error(node.Id, $"'source' names the {node.Overload.Definition.SourceType} it listens to");
            return;
        }
        used.Add("source");
        if (source.Kind != GraphValueKind.Binding)
        {
            Error(node.Id, "'source' is an object bound to the script");
            return;
        }
        if (source.Binding >= _bindings.Count)
        {
            Error(node.Id, $"'source' uses binding {source.Binding}, the graph declares {_bindings.Count}");
            return;
        }
        node.SourceBinding = source.Binding;
    }

    // A timed event's "seconds": a number, one a second when unset, never faster than MinIntervalSeconds.
    private void ResolveInterval(ResolvedNode node, HashSet<string> used)
    {
        node.IntervalSeconds = 1f;
        if (!node.Source.Values.TryGetValue("seconds", out var seconds))
            return;
        used.Add("seconds");
        if (seconds.Kind != GraphValueKind.Number || !double.IsFinite(seconds.Number) || seconds.Number <= 0d)
        {
            Error(node.Id, "'seconds' is a number of seconds above zero");
            return;
        }
        node.IntervalSeconds = (float)Math.Max(seconds.Number, MinIntervalSeconds);
    }

    // A flow cycle has to pass through a loop node. Anything else is a chain that never ends, found
    // here rather than by the fuel meter at run time. A Delay's exit is not an edge at all: what follows
    // it runs in a later call, so a cycle through a Delay is a timer that re-arms itself. An await's answer
    // exits are the same.
    private void CheckFlowCycles()
    {
        var index = new Dictionary<ResolvedNode, int>();
        var low = new Dictionary<ResolvedNode, int>();
        var onStack = new HashSet<ResolvedNode>();
        var stack = new Stack<ResolvedNode>();
        int counter = 0;

        foreach (var root in _nodes)
        {
            if (index.ContainsKey(root))
                continue;

            var work = new Stack<(ResolvedNode Node, int Exit)>();
            work.Push((root, 0));
            index[root] = low[root] = counter++;
            stack.Push(root);
            onStack.Add(root);

            while (work.Count > 0)
            {
                var (node, exit) = work.Pop();
                if (exit < node.Exits.Length)
                {
                    work.Push((node, exit + 1));
                    var next = node.Exits[exit];
                    if (next == null || !node.RunsNow(exit))
                        continue;
                    if (!index.ContainsKey(next))
                    {
                        index[next] = low[next] = counter++;
                        stack.Push(next);
                        onStack.Add(next);
                        work.Push((next, 0));
                    }
                    else if (onStack.Contains(next))
                    {
                        low[node] = Math.Min(low[node], index[next]);
                    }
                    continue;
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }

                if (low[node] != index[node])
                    continue;

                var component = new List<ResolvedNode>();
                ResolvedNode member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                } while (member != node);

                bool cyclic = component.Count > 1 || node.Exits.Where((_, exit) => node.RunsNow(exit)).Contains(node);
                if (cyclic && !component.Any(n => n.IsLoop))
                {
                    var ids = string.Join(", ", component.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal));
                    foreach (var n in component)
                        Error(n.Id, $"is on a flow cycle ({ids}) that passes through no loop node");
                }
            }
        }
    }

    private void CheckDataCycles()
    {
        var state = new Dictionary<ResolvedNode, int>();
        foreach (var node in _nodes)
        {
            if (IsComputed(node))
                Visit(node);
        }

        void Visit(ResolvedNode node)
        {
            var path = new Stack<(ResolvedNode Node, int Input)>();
            if (state.GetValueOrDefault(node) != 0)
                return;
            state[node] = 1;
            path.Push((node, 0));
            while (path.Count > 0)
            {
                var (current, input) = path.Pop();
                if (input < current.Inputs.Length)
                {
                    path.Push((current, input + 1));
                    var source = current.Inputs[input]?.Node;
                    if (source == null || !IsComputed(source))
                        continue;
                    int seen = state.GetValueOrDefault(source);
                    if (seen == 1)
                        Error(source.Id, $"is part of a data cycle through {current.Id}");
                    else if (seen == 0)
                    {
                        state[source] = 1;
                        path.Push((source, 0));
                    }
                    continue;
                }
                state[current] = 2;
            }
        }

        static bool IsComputed(ResolvedNode node) => node.Kind is NodeKind.Value or NodeKind.Read or NodeKind.Constant;
    }

    private CompileResult Emit(string hash)
    {
        var module = new WasmModuleBuilder();

        int stringImport = module.ImportFunction(RuntimeModule, StringImport,
            new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.I32 }, new[] { WasmValueType.I32 }));

        var imports = new List<NodeOverload>();
        var importIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        int Import(NodeOverload overload)
        {
            var linked = overload.Folds ?? overload;
            if (!importIndex.TryGetValue(linked.ImportName, out int function))
            {
                function = module.ImportFunction(NodeModule, linked.ImportName, linked.ImportType!);
                importIndex.Add(linked.ImportName, function);
                imports.Add(linked);
            }
            return function;
        }
        foreach (var node in _nodes)
        {
            if (node.Overload.IsHostCall)
                node.ImportIndex = Import(node.Overload);
            if (node.Helpers.Length > 0)
                node.HelperImports = node.Helpers.Select(h => h.IsHostCall ? Import(h) : -1).ToArray();
        }

        int timerImport = -1;
        var delays = _nodes.Where(n => n.IsDelay).ToList();
        for (int i = 0; i < delays.Count; i++)
            delays[i].TimerIndex = i;
        if (delays.Count > 0)
        {
            timerImport = module.ImportFunction(RuntimeModule, TimerImport,
                new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.F32 }, Array.Empty<WasmValueType>()));
        }

        var awaits = _nodes.Where(n => n.IsAwait).ToList();
        for (int i = 0; i < awaits.Count; i++)
            awaits[i].AwaitIndex = i;

        int nowImport = -1;
        if (_nodes.Any(n => n.Overload.Shape?.Kind == FlowShapeKind.Cooldown))
            nowImport = module.ImportFunction(RuntimeModule, NowImport, new WasmFuncType(Array.Empty<WasmValueType>(), new[] { WasmValueType.F64 }));
        int eachCount = -1, eachItem = -1;
        if (_nodes.Any(n => n.Overload.Shape?.Kind == FlowShapeKind.Each))
        {
            eachCount = module.ImportFunction(RuntimeModule, EachCountImport,
                new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.I32 }, new[] { WasmValueType.I32 }));
            eachItem = module.ImportFunction(RuntimeModule, EachItemImport,
                new WasmFuncType(new[] { WasmValueType.I32, WasmValueType.I32, WasmValueType.I32 }, new[] { WasmValueType.I32 }));
        }
        int eachQuery = -1;
        if (_nodes.Any(n => n.Overload.Shape is { Kind: FlowShapeKind.Each, Query.Length: > 0 }))
            eachQuery = module.ImportFunction(RuntimeModule, EachQueryImport, EachQueryType);
        var runtime = new RuntimeImports(timerImport, nowImport, eachCount, eachItem, eachQuery);

        // Every constant string, deduplicated, laid out after the continuation stack.
        var strings = new Dictionary<string, (int Offset, int Length, int Global)>(StringComparer.Ordinal);
        var data = new List<byte>();
        foreach (var constant in AllConstants())
        {
            if (constant.Value is not string text || constant.Type.Kind != ScriptTypeKind.Handle || strings.ContainsKey(text))
                continue;
            var utf8 = Encoding.UTF8.GetBytes(text);
            int global = module.AddGlobal(new WasmGlobalType(WasmValueType.I32, true), WasmConstInit.I32(0));
            strings.Add(text, (StackBytes + data.Count, utf8.Length, global));
            data.AddRange(utf8);
        }

        uint pages = (uint)((StackBytes + data.Count + 65535) / 65536);
        module.AddMemory(pages, pages);
        if (data.Count > 0)
            module.AddActiveData(WasmConstInit.I32(StackBytes), data.ToArray());

        var variables = new List<ScriptVariableEntry>();
        var stringVariables = new List<(int Global, string Text)>();
        foreach (var node in _nodes.Where(n => n.Kind == NodeKind.StoredVariable))
        {
            var type = node.Overload.Outputs[0].Type;
            var initial = node.Initial ?? ConstantValue.Default(type);
            var raw = new ulong[type.LaneCount];
            if (initial.Binding >= 0)
                raw[0] = LaneCodec.FromInt(initial.Binding + 1);
            else if (initial.Value is not string)
                LaneCodec.Write(type, initial.Value, raw, null);

            node.Globals = new int[type.LaneCount];
            var exports = new string[type.LaneCount];
            for (int lane = 0; lane < type.LaneCount; lane++)
            {
                node.Globals[lane] = module.AddGlobal(new WasmGlobalType(type.Lanes[lane], true), ConstOf(type.Lanes[lane], raw[lane]));
                exports[lane] = $"{VariableExportPrefix}{node.Id}.{lane}";
                module.Export(exports[lane], WasmExternKind.Global, node.Globals[lane]);
            }
            if (initial.Value is string text)
                stringVariables.Add((node.Globals[0], text));
            variables.Add(new ScriptVariableEntry(node.Id, node.VariableName ?? node.Id, type, exports));
        }

        // A Display's value and a Pulse's count are variables too: the host reads them the same way, and a
        // handle on show stays a root for as long as it is shown. A Display that can be worked out at any
        // time is refreshed by the show export; one fed straight from a flow's own data is taken as that
        // flow's call ends; any other would have to compute from data that is gone, so it shows nothing.
        // What a Once, a Toggle or a Cooldown remembers between runs: fresh with every instance, and carried
        // to the same node in a live graph swap.
        var memory = new List<ScriptMemoryEntry>();
        foreach (var node in _nodes)
        {
            var kind = node.Overload.Shape?.Kind;
            switch (kind)
            {
                case FlowShapeKind.Once:
                case FlowShapeKind.Toggle:
                    node.Globals = new[] { module.AddGlobal(new WasmGlobalType(WasmValueType.I32, true), WasmConstInit.I32(0)) };
                    break;
                case FlowShapeKind.Cooldown:
                    node.Globals = new[] { module.AddGlobal(new WasmGlobalType(WasmValueType.F64, true), WasmConstInit.F64(-1e300)) };
                    break;
                case FlowShapeKind.Change:
                {
                    // Whether it has run, then the lanes of what came in last. Not carried in a swap: an
                    // edited graph's Change starts over.
                    var lanes = node.Overload.Inputs[node.Overload.Shape!.Input].Type.Lanes;
                    node.Globals = new int[lanes.Length + 1];
                    node.Globals[0] = module.AddGlobal(new WasmGlobalType(WasmValueType.I32, true), WasmConstInit.I32(0));
                    for (int lane = 0; lane < lanes.Length; lane++)
                        node.Globals[lane + 1] = module.AddGlobal(new WasmGlobalType(lanes[lane], true), ConstOf(lanes[lane], 0ul));
                    continue;
                }
                default:
                    continue;
            }
            string export = MemoryExportPrefix + node.Id;
            module.Export(export, WasmExternKind.Global, node.Globals[0]);
            memory.Add(new ScriptMemoryEntry(node.Id, kind!.Value, export));
        }

        // A counter on every wired flow exit, for the editor's live view: how many times flow has left that
        // node that way. Four instructions a step, whichever way the flow goes.
        var traces = new List<ScriptTraceEntry>();
        foreach (var node in _nodes)
        {
            if (node.Exits.Length == 0)
                continue;
            node.TraceGlobals = new int[node.Exits.Length];
            for (int exit = 0; exit < node.Exits.Length; exit++)
            {
                if (node.Exits[exit] == null)
                {
                    node.TraceGlobals[exit] = -1;
                    continue;
                }
                int global = module.AddGlobal(new WasmGlobalType(WasmValueType.I32, true), WasmConstInit.I32(0));
                string export = $"{TraceExportPrefix}{node.Id}.{exit}";
                module.Export(export, WasmExternKind.Global, global);
                node.TraceGlobals[exit] = global;
                traces.Add(new ScriptTraceEntry(node.Id, node.Overload.FlowOutputs[exit], export));
            }
        }

        var displays = new List<ScriptDisplayEntry>();
        var free = new List<ResolvedNode>();
        var direct = new List<ResolvedNode>();
        var shown = new Dictionary<ResolvedNode, int>();
        foreach (var node in _nodes.Where(n => n.Kind is NodeKind.Display or NodeKind.Pulse))
        {
            bool counts = node.Kind == NodeKind.Pulse;
            var type = counts ? ScriptTypes.Int : node.Overload.Inputs[0].Type;
            node.Globals = new int[type.LaneCount];
            var exports = new string[type.LaneCount];
            for (int lane = 0; lane < type.LaneCount; lane++)
            {
                node.Globals[lane] = module.AddGlobal(new WasmGlobalType(type.Lanes[lane], true), ConstOf(type.Lanes[lane], 0));
                exports[lane] = $"{VariableExportPrefix}{node.Id}.{lane}";
                module.Export(exports[lane], WasmExternKind.Global, node.Globals[lane]);
            }
            variables.Add(new ScriptVariableEntry(node.Id, (counts ? "pulse." : "display.") + node.Id, type, exports));
            bool refreshed = !counts && NeedsNoFlow(node, new HashSet<ResolvedNode>());
            bool captured = !counts && !refreshed && FedByFlow(node);
            shown[node] = displays.Count;
            displays.Add(new ScriptDisplayEntry(node.Id, counts, refreshed ? DisplayExportPrefix + node.Id : null, captured));
            if (refreshed)
                free.Add(node);
            else if (captured)
                direct.Add(node);
            else if (!counts)
                Warn(node.Id, "shows a value worked out from a flow's data after that flow is done; wire it straight to the output to show");
        }

        if (strings.Count > 0)
        {
            var start = module.AddFunction(WasmFuncType.Empty);
            foreach (var (_, (offset, length, global)) in strings)
                start.I32Const(offset).I32Const(length).Call(stringImport).GlobalSet(global);
            foreach (var (global, text) in stringVariables)
                start.GlobalGet(strings[text].Global).GlobalSet(global);
            module.SetStart(start.Index);
        }

        var events = new List<ScriptEventEntry>();
        foreach (var node in _nodes.Where(n => n.Kind == NodeKind.Event))
        {
            var parameters = node.Overload.Outputs.SelectMany(o => o.Type.Lanes).ToArray();
            var function = module.AddFunction(new WasmFuncType(parameters, Array.Empty<WasmValueType>()));
            new EventFunctionEmitter(function, node, text => strings[text].Global, runtime, Error, direct).Emit();
            var export = EventExportPrefix + node.Id;
            module.Export(export, WasmExternKind.Function, function.Index);
            events.Add(new ScriptEventEntry(node.Id, node.Overload.Definition.Name, export, node.Overload.PayloadType!, node.Overload,
                node.SourceBinding, node.IntervalSeconds));
        }

        var timers = new List<ScriptTimerEntry>();
        foreach (var node in delays)
        {
            var function = module.AddFunction(WasmFuncType.Empty);
            new EventFunctionEmitter(function, node, text => strings[text].Global, runtime, Error, direct).Emit();
            var export = TimerExportPrefix + node.Id;
            module.Export(export, WasmExternKind.Function, function.Index);
            timers.Add(new ScriptTimerEntry(node.Id, export));
        }

        // Each exit an await's answer can run is a function of its own, with the payload as its parameters,
        // the way an event's is.
        var answered = new List<ScriptAwaitEntry>();
        foreach (var node in awaits)
        {
            var parameters = node.Overload.Outputs.SelectMany(o => o.Type.Lanes).ToArray();
            var exits = new List<string>();
            var exports = new List<string>();
            for (int exit = 1; exit < node.Exits.Length; exit++)
            {
                var function = module.AddFunction(new WasmFuncType(parameters, Array.Empty<WasmValueType>()));
                new EventFunctionEmitter(function, node, text => strings[text].Global, runtime, Error, direct, exit).Emit();
                var name = node.Overload.FlowOutputs[exit];
                var export = $"{AwaitExportPrefix}{node.Id}.{name}";
                module.Export(export, WasmExternKind.Function, function.Index);
                exits.Add(name);
                exports.Add(export);
            }
            answered.Add(new ScriptAwaitEntry(node.Id, exits, exports, node.Overload.PayloadType!, node.Overload));
        }

        foreach (var node in free)
        {
            var function = module.AddFunction(WasmFuncType.Empty);
            EventFunctionEmitter.EmitDisplays(function, new[] { node }, text => strings[text].Global, Error);
            module.Export(displays[shown[node]].Export!, WasmExternKind.Function, function.Index);
        }

        if (HasErrors)
            return CompileResult.Failed(hash, _diagnostics);

        var manifest = ManifestWriter.Write(hash, events, timers, answered, imports, _bindings, variables, displays);
        module.AddCustomSection(ManifestSection, manifest);
        var bytes = module.ToBytes();

        WasmModule decoded;
        try
        {
            decoded = WasmModule.Decode(bytes, ModuleLimits);
        }
        catch (WasmException ex)
        {
            Error(null, $"internal: the compiler produced a module the engine rejects ({ex.Message})");
            return CompileResult.Failed(hash, _diagnostics);
        }

        var tags = _graph.Nodes.Select(n => n.Id).ToArray();
        return new CompileResult(hash, bytes, manifest, _diagnostics, imports, events, timers, variables, _bindings, tags,
            displays) { Decoded = decoded, Traces = traces, FlowMemory = memory, Awaits = answered };
    }

    // Whether a value can be worked out at any time: from constants, stored values, and pure and read
    // nodes over them, with no event's data and no action's output anywhere in it.
    private static bool NeedsNoFlow(ResolvedNode node, HashSet<ResolvedNode> seen)
    {
        foreach (var source in node.Inputs)
        {
            if (source?.Node is not { } from || !seen.Add(from))
                continue;
            switch (from.Kind)
            {
                case NodeKind.Event:
                case NodeKind.Action:
                case NodeKind.Flow:
                case NodeKind.Await:
                    return false;
                case NodeKind.Value:
                case NodeKind.Read:
                case NodeKind.Constant:
                    if (!NeedsNoFlow(from, seen))
                        return false;
                    break;
            }
        }
        return true;
    }

    // Whether a Display reads an event's payload or an action's output directly, through any Constants.
    private static bool FedByFlow(ResolvedNode display)
    {
        var source = display.Inputs[0];
        while (source?.Node is { Kind: NodeKind.Constant } pass)
            source = pass.Inputs[0];
        return source?.Node?.Kind is NodeKind.Event or NodeKind.Action or NodeKind.Flow or NodeKind.Await;
    }

    private IEnumerable<ConstantValue> AllConstants()
    {
        foreach (var node in _nodes)
        {
            foreach (var input in node.Inputs)
            {
                if (input?.Constant != null)
                    yield return input.Constant;
            }
            if (node.Initial != null)
                yield return node.Initial;
        }
    }

    private static WasmConstInit ConstOf(WasmValueType lane, ulong raw) => lane switch
    {
        WasmValueType.I64 => WasmConstInit.I64(LaneCodec.ToLong(raw)),
        WasmValueType.F32 => WasmConstInit.F32(LaneCodec.ToFloat(raw)),
        WasmValueType.F64 => WasmConstInit.F64(LaneCodec.ToDouble(raw)),
        _ => WasmConstInit.I32(LaneCodec.ToInt(raw)),
    };
}
