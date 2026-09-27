// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Lumora.Core.Components.Assets;
using Lumora.Core.Scripting;
using Lumora.Script;
using Lumora.Shading;

namespace Lumora.Core.Components.Scripting;

// The root of a graph being edited: its node slots are its direct children. A script's Host is the
// ScriptHost the graph packs into; a shader graph's ShaderSource is what compiles it into a material's
// shader, and a loose group of shader nodes has neither yet.
//
// Every peer checks the graph for itself a moment after the last edit, with the same compiler the host
// runs, so every peer paints the same nodes red without a word on the wire. Nothing here writes to the
// datamodel while checking. The graph is read here and compiled on a worker through the compile cache,
// so the check that says a graph is clean has also compiled what the host then runs.
//
// A graph parked for an undo step, hidden or packed away is drawn by no one; a graph joined into another
// is parked like that, and whatever a hand held from it now belongs to the other. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptGraphSpace : Component
{
    public const double CheckDelaySeconds = 0.25;

    public readonly SyncRef<ScriptHost> Host = new();

    public readonly Sync<GraphKind> Kind = new();

    public readonly SyncRef<ShaderGraphSource> ShaderSource = new();

    private static readonly IReadOnlyList<string> NoProblems = Array.Empty<string>();

    public GraphLanguage Language => GraphLanguage.For(Kind.Value);

    // Every graph in a world, so a tool can find the one under a swipe or a port to snap to without
    // walking the slot tree.
    private static readonly ConditionalWeakTable<World, List<ScriptGraphSpace>> Open = new();

    private readonly Dictionary<ScriptGraphNode, List<string>> _problems = new();
    private readonly Dictionary<ScriptGraphNode, List<string>> _notes = new();
    private readonly List<string> _graphProblems = new();
    private readonly List<ScriptGraphNode> _nodes = new();
    private readonly HashSet<ScriptGraphNode> _held = new();
    private int _revision = int.MinValue;
    private double _changedAt;
    private bool _dirty = true;
    private ScriptGraphView? _view;
    private Task<GraphCheck>? _checking;
    private ScriptGraphBuild? _checkingBuild;
    private int _checkingRevision;
    private Slot? _watched;
    private bool _activeChangeQueued;

    // This machine's cards and wires for the graph.
    public ScriptGraphView? View => _view;

    public event Action<ScriptGraphSpace>? Checked;

    // How many checks have run on this peer.
    public int CheckCount { get; private set; }

    public bool IsChecked => CheckCount > 0 && !_dirty;

    public bool HasProblems => _problems.Count > 0 || _graphProblems.Count > 0;

    public IReadOnlyList<string> GraphProblems => _graphProblems;

    // A script graph's last compile; null for a shader graph.
    public CompileResult? LastCompile { get; private set; }

    // A shader graph's last compile; null for a script.
    public ShaderProgram? LastShader { get; private set; }

    // The graph as it stood at the last check, with the objects its bindings stand for.
    public ScriptGraphBuild? LastBuild { get; private set; }

    public IReadOnlyList<string> ProblemsOf(ScriptGraphNode node)
        => _problems.TryGetValue(node, out var list) ? list : NoProblems;

    // What the last check warned about on a node without it being wrong: a card shows these, not in red.
    public IReadOnlyList<string> NotesOf(ScriptGraphNode node)
        => _notes.TryGetValue(node, out var list) ? list : NoProblems;

    // The node slots under this graph, and any node a hand is holding that was picked up from here.
    public IEnumerable<ScriptGraphNode> Nodes
    {
        get
        {
            var slot = Slot;
            if (slot == null)
                yield break;
            foreach (var child in slot.Children)
            {
                if (child.IsDestroyed)
                    continue;
                var node = child.GetComponent<ScriptGraphNode>();
                if (node != null && !node.IsDestroyed)
                    yield return node;
            }
            foreach (var node in _held)
            {
                if (IsHeldFromHere(node))
                    yield return node;
            }
        }
    }

    private bool IsHeldFromHere(ScriptGraphNode node)
        => !node.IsDestroyed && node.Slot?.Parent != Slot && node.Slot?.GetComponent<Grabbable>() is { IsGrabbed: true } grab
           && ReferenceEquals(grab.LastParentRef.Target, Slot);

    // A node a hand holds that is let go of into this graph, which it now belongs to.
    internal void NoteHeld(ScriptGraphNode node)
    {
        if (node != null && IsHeldFromHere(node))
            _held.Add(node);
    }

    // Every graph this machine knows in a world, open or parked.
    public static void GetAll(World? world, List<ScriptGraphSpace> into)
    {
        into.Clear();
        if (world == null || !Open.TryGetValue(world, out var spaces))
            return;
        lock (spaces)
        {
            foreach (var space in spaces)
            {
                if (!space.IsDestroyed)
                    into.Add(space);
            }
        }
    }

    public override void OnAwake()
    {
        base.OnAwake();
        if (World is { } world)
        {
            var spaces = Open.GetValue(world, static _ => new List<ScriptGraphSpace>());
            lock (spaces)
                spaces.Add(this);
        }
        _watched = Slot;
        if (_watched != null)
            _watched.ActiveChanged += OnActiveChanged;
    }

    // Put off until the change has landed whole: parking moves the slot and switches it off, and from
    // another machine those arrive one at a time.
    private void OnActiveChanged(Slot slot)
    {
        if (_activeChangeQueued || World == null)
            return;
        _activeChangeQueued = true;
        World.RunSynchronously(AfterActiveChanged);
    }

    private void AfterActiveChanged()
    {
        _activeChangeQueued = false;
        var world = World;
        if (IsDestroyed || world == null || Slot == null)
            return;
        if (!Slot.IsActive)
        {
            _view?.Dispose();
            _view = null;
            HandOverHeld();
        }
        // Its script runs this graph only while the graph is out, so it decides again either way, in the change
        // pass like any change to the host. A live swap drops what the old runtime still had queued, and
        // deciding before the script phase could swap away a press made this frame.
        if (Host.Target is { IsDestroyed: false } host && (world.IsAuthority || host.Mode.Value == ScriptMode.Local))
            host.MarkChangeDirty();
    }

    // What a hand held from here while this graph was joined into another now belongs to that one.
    private void HandOverHeld()
    {
        foreach (var node in _held)
        {
            if (!node.IsDestroyed && !IsHeldFromHere(node) && node.Space is { } now && !ReferenceEquals(now, this))
                now.NoteHeld(node);
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // A packed graph waits parked for its undo step; nobody is looking at it.
        if (Slot?.IsActive != true)
            return;

        int revision = Revision();
        double now = World?.Time.TotalTime ?? 0.0;
        if (revision != _revision)
        {
            _revision = revision;
            _changedAt = now;
            _dirty = true;
        }
        if (_checking is { IsCompleted: true })
            FinishCheck();
        if (_dirty && _checking == null && now - _changedAt >= CheckDelaySeconds)
            BeginCheck();

        _view ??= new ScriptGraphView(this);
        _view.Update();
    }

    public override void OnDestroy()
    {
        if (_watched != null)
            _watched.ActiveChanged -= OnActiveChanged;
        _watched = null;
        if (World is { } world && Open.TryGetValue(world, out var spaces))
        {
            lock (spaces)
                spaces.Remove(this);
        }
        _view?.Dispose();
        _view = null;
        if (World != null && Host.Target is { IsDestroyed: false } host && (World.IsAuthority || host.Mode.Value == ScriptMode.Local))
            World.RunSynchronously(host.Reconcile);
        base.OnDestroy();
    }

    // Reads the graph as it stands and hands it to a worker to check with its kind's compiler.
    private void BeginCheck()
    {
        var build = ScriptGraphPacking.Build(this, withPositions: false);
        _checkingBuild = build;
        _checkingRevision = _revision;
        _checking = Language.CheckAsync(build.Graph);
    }

    // What the worker found, unless the graph has been edited since it was read; the next check is then
    // already on its way.
    private void FinishCheck()
    {
        var result = _checking!.Result;
        var build = _checkingBuild!;
        _checking = null;
        _checkingBuild = null;
        if (_checkingRevision != _revision)
            return;

        _problems.Clear();
        _notes.Clear();
        _graphProblems.Clear();
        foreach (var (node, message) in build.Problems)
            Note(_problems, node, message);
        // A shader graph's warnings go on its cards as notes; a script's cards show its errors as they always have.
        foreach (var diagnostic in result.Diagnostics)
        {
            bool error = diagnostic.Severity == DiagnosticSeverity.Error;
            if (!error && Kind.Value != GraphKind.Shader)
                continue;
            var into = error ? _problems : _notes;
            if (diagnostic.NodeId != null && build.Nodes.TryGetValue(diagnostic.NodeId, out var node))
                Note(into, node, diagnostic.Message);
            else if (error)
                Note(into, null, diagnostic.Message);
        }

        LastCompile = result.Script;
        LastShader = result.Shader;
        LastBuild = build;
        _dirty = false;
        CheckCount++;
        Checked?.Invoke(this);

        // The authority runs an open graph as it stands, and so does every machine for a Local script, so
        // the host hears about every check. A shader graph's material follows it on every machine.
        if (World != null && Host.Target is { IsDestroyed: false } host && (World.IsAuthority || host.Mode.Value == ScriptMode.Local))
            host.Reconcile();
        if (ShaderSource.Target is { IsDestroyed: false } source && result.Shader != null)
            source.TakeCheck(this, result.Shader);
    }

    private void Note(Dictionary<ScriptGraphNode, List<string>> into, ScriptGraphNode? node, string message)
    {
        if (node == null)
        {
            if (!_graphProblems.Contains(message))
                _graphProblems.Add(message);
            return;
        }
        if (!into.TryGetValue(node, out var list))
            into[node] = list = new List<string>();
        if (!list.Contains(message))
            list.Add(message);
    }

    // Every node's content folded into one number, for whatever compiles a graph nobody has open.
    public int ContentRevision()
    {
        var hash = new HashCode();
        int count = 0;
        foreach (var node in Nodes)
        {
            hash.Add(node.ReferenceID.RawValue);
            hash.Add(node.ContentHash());
            count++;
        }
        hash.Add(count);
        return hash.ToHashCode();
    }

    // Cheap enough to take every frame for a graph people build by hand: moves nothing, allocates nothing
    // past the first call.
    private int Revision()
    {
        foreach (var node in _nodes)
        {
            if (IsHeldFromHere(node))
                _held.Add(node);
        }
        HandOverHeld();
        _held.RemoveWhere(node => !IsHeldFromHere(node));
        _nodes.Clear();
        foreach (var node in Nodes)
            _nodes.Add(node);

        var hash = new HashCode();
        hash.Add(_nodes.Count);
        foreach (var node in _nodes)
        {
            hash.Add(node.ReferenceID.RawValue);
            hash.Add(node.ContentHash());
        }
        return hash.ToHashCode();
    }
}
