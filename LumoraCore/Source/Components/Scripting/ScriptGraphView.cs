// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Scripting;

namespace Lumora.Core.Components.Scripting;

// What a graph looks like on this machine: a card per node and one mesh for every wire, all on local
// slots, kept up with the shared node data a frame at a time. A card is rebuilt only when its node, that
// node's problems or its fault mark change; the wires are rebuilt when any end of any wire moves, or a cut
// marks or lets go of one.
//
// Flow shows as glowing segments running along the wires it takes, from the running script's counts of how often it
// left each node by each exit: what the host publishes for a Shared script, this machine's own runtime for
// a Local one. A shader graph's cards show instead what each node makes, in a preview compiled here after
// every check. -xlinka
public sealed class ScriptGraphView
{
    public const double SignalSeconds = 0.65;
    private const double SignalGap = 0.09;
    private const int MaxBurst = 3;
    private const int MaxSignals = 256;

    private readonly ScriptGraphSpace _space;
    private readonly Dictionary<ScriptGraphNode, ScriptNodeCard> _cards = new();
    private readonly Dictionary<ScriptGraphNode, ScriptNodePreview> _previews = new();
    private readonly Dictionary<ScriptGraphNode, string> _documentIds = new();
    private int _previewCheck = -1;
    private readonly List<ScriptGraphNode> _nodes = new();
    private readonly List<ScriptGraphNode> _gone = new();
    private readonly List<ScriptWireCurve> _curves = new();
    private readonly List<ScriptWireKey> _keys = new();
    private readonly Dictionary<ScriptWireKey, int> _flowWires = new();
    private readonly HashSet<ScriptWireKey> _cutMarks = new();
    private readonly Dictionary<ScriptWireKey, int> _seenCounts = new();
    private readonly List<ScriptWireKey> _goneTraceKeys = new();
    private readonly List<(ScriptWireKey Key, double Start)> _signals = new();
    private readonly List<ScriptWireCurve> _streaks = new();
    private readonly Dictionary<ScriptGraphNode, (string Text, Dictionary<string, int> Counts)> _published = new();
    private readonly Dictionary<(string Node, string Exit), int> _localTraces = new();
    private object? _localTracesFor;
    private Slot? _wireSlot;
    private ScriptWireMesh? _wireMesh;
    private Slot? _signalSlot;
    private ScriptWireMesh? _signalMesh;
    private int _wireStamp;
    private bool _hasSampledTraces;

    public ScriptGraphView(ScriptGraphSpace space)
    {
        _space = space;
    }

    public int CardCount => _cards.Count;

    public int WireCount => _wireMesh?.WireCount ?? 0;

    // Execution pulses on their way along wires right now.
    public int SignalCount => _signals.Count;

    public ScriptNodeCard? CardOf(ScriptGraphNode node) => _cards.TryGetValue(node, out var card) ? card : null;

    // A shader node's preview on this machine, once its graph has been checked.
    public ScriptNodePreview? PreviewOf(ScriptGraphNode node) => _previews.TryGetValue(node, out var preview) ? preview : null;

    public int PreviewCount => _previews.Count;

    public void Update()
    {
        double now = _space.World?.Time.TotalTime ?? 0.0;
        _nodes.Clear();
        foreach (var node in _space.Nodes)
            _nodes.Add(node);

        foreach (var node in _nodes)
        {
            string? fault = FaultOf(node, now);
            int stamp = Stamp(node, fault);
            if (_cards.TryGetValue(node, out var card))
            {
                if (card.Stamp == stamp || card.IsEditing)
                {
                    card.Refresh();
                    continue;
                }
                card.Destroy();
            }
            _cards[node] = ScriptNodeCard.Build(node, _space.ProblemsOf(node), stamp, fault, card?.Layout.OptionsOpen == true,
                _space.NotesOf(node));
        }

        _gone.Clear();
        foreach (var node in _cards.Keys)
        {
            if (node.IsDestroyed || !_nodes.Contains(node))
                _gone.Add(node);
        }
        foreach (var node in _gone)
        {
            _cards[node].Destroy();
            _cards.Remove(node);
            _published.Remove(node);
            if (_previews.Remove(node, out var preview))
                preview.Destroy();
        }

        UpdateWires();
        UpdateSignals(now);
        UpdatePreviews();
    }

    // Each shader node's output on its own, compiled here from what the graph's last check read, again after
    // every check; a card rebuilt in between keeps its preview. Nothing for a script.
    private void UpdatePreviews()
    {
        if (_space.Kind.Value != GraphKind.Shader)
            return;
        var build = _space.LastBuild;
        bool fresh = _space.CheckCount != _previewCheck;
        if (fresh && build != null)
        {
            _documentIds.Clear();
            foreach (var (id, node) in build.Nodes)
                _documentIds[node] = id;
        }
        var textures = _space.ShaderSource.Target is { IsDestroyed: false } source ? source : null;
        foreach (var node in _nodes)
        {
            if (!_cards.TryGetValue(node, out var card) || !card.Layout.Preview)
            {
                if (_previews.Remove(node, out var stale))
                    stale.Destroy();
                continue;
            }
            bool made = false;
            if (!_previews.TryGetValue(node, out var preview))
            {
                preview = ScriptNodePreview.Create(node);
                _previews[node] = preview;
                made = true;
            }
            preview.Place(card.Layout);
            if ((fresh || made) && build != null && _documentIds.TryGetValue(node, out var id))
                preview.Show(Lumora.Shading.ShaderGraphCompiler.CompilePreview(build.Graph, GraphLanguage.Shader.Registry, id), textures);
        }
        if (build != null)
            _previewCheck = _space.CheckCount;
    }

    public void Dispose()
    {
        foreach (var card in _cards.Values)
            card.Destroy();
        _cards.Clear();
        foreach (var preview in _previews.Values)
            preview.Destroy();
        _previews.Clear();
        foreach (var slot in new[] { _wireSlot, _signalSlot })
        {
            if (slot != null && !slot.IsDestroyed)
                slot.Destroy();
        }
        _wireSlot = null;
        _wireMesh = null;
        _signalSlot = null;
        _signalMesh = null;
    }

    // Where a port of a node meets its wires, in world space.
    public bool TryPortPoint(ScriptGraphNode node, ScriptCardHandleKind kind, string port, out float3 point)
    {
        point = default;
        if (!_cards.TryGetValue(node, out var card) || !card.Layout.TryAnchor(kind, port, out var anchor))
            return false;
        point = node.Slot.LocalPointToGlobal(ScriptEditorStyle.CanvasToLocal(anchor));
        return true;
    }

    // The wires a cut would take, drawn red until it lets go. Null or empty lets go of them all.
    public void SetCutMarks(IReadOnlyCollection<ScriptWireKey>? keys)
    {
        _cutMarks.Clear();
        if (keys == null)
            return;
        foreach (var key in keys)
            _cutMarks.Add(key);
    }

    // Every wire the straight line from a to b crosses, both ends in the graph's own space, judged flat on
    // the graph's plane against the curve the wire is drawn along.
    public int WiresCrossing(float3 a, float3 b, List<ScriptWireKey> into)
    {
        into.Clear();
        for (int i = 0; i < _curves.Count; i++)
        {
            var curve = _curves[i];
            var previous = ScriptWireMesh.PointOn(curve, 0f);
            for (int step = 1; step < ScriptWireMesh.Rings; step++)
            {
                var next = ScriptWireMesh.PointOn(curve, step / (float)(ScriptWireMesh.Rings - 1));
                if (Crosses(a, b, previous, next))
                {
                    into.Add(_keys[i]);
                    break;
                }
                previous = next;
            }
        }
        return into.Count;
    }

    private static bool Crosses(in float3 a, in float3 b, in float3 c, in float3 d)
    {
        float d1 = Side(c, d, a);
        float d2 = Side(c, d, b);
        float d3 = Side(a, b, c);
        float d4 = Side(a, b, d);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Side(in float3 from, in float3 to, in float3 point)
        => (to.x - from.x) * (point.y - from.y) - (to.y - from.y) * (point.x - from.x);

    private int Stamp(ScriptGraphNode node, string? fault)
    {
        var hash = new HashCode();
        hash.Add(node.ContentHash());
        foreach (var problem in _space.ProblemsOf(node))
            hash.Add(problem);
        foreach (var note in _space.NotesOf(node))
            hash.Add(note);
        hash.Add(fault);
        return hash.ToHashCode();
    }

    // Why the running script lately faulted in this node: as the host published it for a Shared script,
    // from this machine's own runtime for a Local one.
    private string? FaultOf(ScriptGraphNode node, double now)
    {
        var host = _space.Host.Target;
        if (host?.Mode.Value == ScriptMode.Local)
            return host.LocalRuntime is { } runtime && runtime.TryGetFaultMark(node.NodeId.Value ?? string.Empty, now, out var message) ? message : null;
        var fault = node.Fault.Value;
        return string.IsNullOrEmpty(fault) ? null : fault;
    }

    private void UpdateWires()
    {
        _curves.Clear();
        _keys.Clear();
        _flowWires.Clear();
        var hash = new HashCode();
        foreach (var node in _nodes)
        {
            foreach (var wire in node.Inputs)
            {
                if (wire.Node.Target is not { IsDestroyed: false } from || !_cards.ContainsKey(from))
                    continue;
                string port = wire.Port.Value ?? string.Empty;
                if (!TryPortPoint(from, ScriptCardHandleKind.DataOutput, wire.NodePort.Value ?? string.Empty, out var start)
                    || !TryPortPoint(node, ScriptCardHandleKind.DataInput, port, out var end))
                    continue;
                var key = new ScriptWireKey(node, false, port);
                var tint = _cutMarks.Contains(key) ? ScriptEditorStyle.Cut : ScriptEditorStyle.Type(TypeOf(node, port));
                Add(key, start, end, tint, tint, ref hash);
            }
            foreach (var wire in node.Exits)
            {
                if (wire.Node.Target is not { IsDestroyed: false } to || !_cards.ContainsKey(to))
                    continue;
                string exit = wire.Port.Value ?? string.Empty;
                if (!TryPortPoint(node, ScriptCardHandleKind.FlowOutput, exit, out var start)
                    || !TryPortPoint(to, ScriptCardHandleKind.FlowInput, wire.NodePort.Value ?? string.Empty, out var end))
                    continue;
                var key = new ScriptWireKey(node, true, exit);
                _flowWires[key] = _curves.Count;
                var tint = _cutMarks.Contains(key) ? ScriptEditorStyle.Cut : ScriptEditorStyle.Flow;
                Add(key, start, end, tint, tint, ref hash);
            }
        }

        int stamp = hash.ToHashCode();
        if (stamp == _wireStamp && _wireMesh != null)
            return;
        _wireStamp = stamp;
        EnsureWireMesh();
        _wireMesh!.SetWires(_curves);
    }

    private void Add(in ScriptWireKey key, float3 start, float3 end, color from, color to, ref HashCode hash)
    {
        var slot = _space.Slot;
        var a = slot.GlobalPointToLocal(start);
        var b = slot.GlobalPointToLocal(end);
        _curves.Add(new ScriptWireCurve(a, b, from, to));
        _keys.Add(key);
        hash.Add(Quantize(a));
        hash.Add(Quantize(b));
        hash.Add(from.GetHashCode());
    }

    private static (int, int, int) Quantize(float3 p) => ((int)MathF.Round(p.x * 5000f), (int)MathF.Round(p.y * 5000f), (int)MathF.Round(p.z * 5000f));

    private static Lumora.Script.ScriptType? TypeOf(ScriptGraphNode node, string input)
    {
        var overload = node.Overload;
        int index = overload?.InputIndex(input) ?? -1;
        return index >= 0 ? overload!.Inputs[index].Type : null;
    }

    // Counts are sampled from a running instance here, or from the host's publication on a guest.
    // Establish zero even before the first publication, otherwise the first actual execution is lost.
    private void UpdateSignals(double now)
    {
        var host = _space.Host.Target;
        var local = host?.Mode.Value == ScriptMode.Local ? host.LocalRuntime : host?.SharedRuntime;
        if (!ReferenceEquals(local, _localTracesFor))
        {
            _localTracesFor = local;
            _localTraces.Clear();
            _seenCounts.Clear();
            _signals.Clear();
            if (local != null)
            {
                var traces = local.Runner.Compiled.Traces;
                for (int i = 0; i < traces.Count; i++)
                    _localTraces[(traces[i].NodeId, traces[i].Exit)] = i;
                // This view was already watching when the new runtime started. Its first execution
                // belongs to this session, unlike historic counts when a graph is first opened.
                if (_hasSampledTraces)
                    foreach (var key in _flowWires.Keys)
                        _seenCounts[key] = 0;
            }
        }

        foreach (var (key, _) in _flowWires)
        {
            int count = local != null ? LocalCount(local, key) : host?.Mode.Value == ScriptMode.Local ? -1 : PublishedCount(key);
            if (count < 0)
            {
                _seenCounts.TryAdd(key, 0);
                continue;
            }
            if (!_seenCounts.TryGetValue(key, out int seen))
            {
                // Opening a graph with existing history is not a new execution.
                _seenCounts[key] = count;
                continue;
            }
            int fresh = System.Math.Min(count < seen ? count : count - seen, MaxBurst);
            _seenCounts[key] = count;
            for (int i = 0; i < fresh && _signals.Count < MaxSignals; i++)
                _signals.Add((key, now + i * SignalGap));
        }

        _hasSampledTraces = true;
        _goneTraceKeys.Clear();
        foreach (var key in _seenCounts.Keys)
        {
            if (!_flowWires.ContainsKey(key))
                _goneTraceKeys.Add(key);
        }
        foreach (var key in _goneTraceKeys)
            _seenCounts.Remove(key);

        _streaks.Clear();
        for (int i = _signals.Count - 1; i >= 0; i--)
        {
            var (key, start) = _signals[i];
            double t = (now - start) / SignalSeconds;
            if (t > 1.0 || !_flowWires.TryGetValue(key, out int wire))
            {
                _signals.RemoveAt(i);
                continue;
            }
            if (t < 0.0)
                continue;
            float head = (float)t * 1.28f;
            _streaks.Add(_curves[wire] with
            {
                Start = head - 0.28f, End = head, Pulse = true,
                FromColor = ScriptEditorStyle.Signal, ToColor = ScriptEditorStyle.Signal,
            });
        }

        if (_streaks.Count == 0)
        {
            if (_signalMesh?.WireCount > 0)
                _signalMesh.SetWires(_streaks);
            return;
        }
        EnsureSignalMesh();
        _signalMesh!.SetWires(_streaks);
    }

    private int LocalCount(ScriptHostRuntime runtime, in ScriptWireKey key)
        => _localTraces.TryGetValue((key.Owner.NodeId.Value ?? string.Empty, key.Port), out int index) ? runtime.ReadTrace(index) : -1;

    // What the host last published for a node's exits, read again only when the text changed.
    private int PublishedCount(in ScriptWireKey key)
    {
        var node = key.Owner;
        string text = node.Traced.Value ?? string.Empty;
        if (!_published.TryGetValue(node, out var parsed) || !ReferenceEquals(parsed.Text, text))
        {
            var counts = parsed.Counts ?? new Dictionary<string, int>(StringComparer.Ordinal);
            counts.Clear();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.LastIndexOf(':');
                if (colon > 0 && int.TryParse(part[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    counts[part[..colon]] = value;
            }
            parsed = (text, counts);
            _published[node] = parsed;
        }
        return parsed.Counts.TryGetValue(key.Port, out int count) ? count : -1;
    }

    private void EnsureWireMesh()
    {
        if (_wireSlot != null && !_wireSlot.IsDestroyed && _wireMesh != null)
            return;
        _wireSlot = _space.Slot.AddLocalSlot("Wires");
        _wireMesh = _wireSlot.AttachComponent<ScriptWireMesh>();
        DrawPipe(_wireSlot, _wireMesh, false);
    }

    private void EnsureSignalMesh()
    {
        if (_signalSlot != null && !_signalSlot.IsDestroyed && _signalMesh != null)
            return;
        _signalSlot = _space.Slot.AddLocalSlot("Signals");
        _signalMesh = _signalSlot.AttachComponent<ScriptWireMesh>();
        DrawPipe(_signalSlot, _signalMesh, true);
    }

    private static void DrawPipe(Slot slot, Component mesh, bool pulse)
    {
        var material = slot.AttachComponent<FresnelMaterial>();
        material.UseVertexColor.Value = true;
        material.Exponent.Value = pulse ? 1.4f : 2f;
        material.NearColor.Value = pulse ? new colorHDR(1.8f, 1.8f, 1.8f, 1f) : new colorHDR(0.55f, 0.55f, 0.55f, 0.12f);
        material.FarColor.Value = pulse ? new colorHDR(1.3f, 1.3f, 1.3f, 0.8f) : new colorHDR(1.2f, 1.2f, 1.2f, 0.85f);
        material.BlendMode.Value = pulse ? BlendMode.Additive : BlendMode.Alpha;
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    // Unlit, coloured by vertex, no shadow: how every mesh the editor draws on its own looks.
    public static void Draw(Slot slot, Component mesh)
    {
        var material = slot.AttachComponent<UnlitMaterial>();
        material.UseVertexColor.Value = true;
        material.TintColor.Value = colorHDR.White;
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }
}
