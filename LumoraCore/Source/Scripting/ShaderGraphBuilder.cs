// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// A shader graph written from code, on a material orb's graph: nodes under names the caller picks, each
// input a wire or a value, then laid out the way a person lays one out, in columns running into the
// Material Output with every node beside what reads it.
//
// An input given as text is a wire: "noise" from that node's first output, "noise.cells" from another.
// Text going into a name or a choice is the value itself. Wires go in before values, because a wire can
// widen a node, and a number then lands in the width the port ended at. -xlinka
public sealed class ShaderGraphBuilder
{
    // Daylight between columns and between cards in one, in card units so it keeps up with the card size.
    public const float ColumnGap = 100f * ScriptEditorStyle.CardScale;
    public const float RowGap = 60f * ScriptEditorStyle.CardScale;

    // Where the Material Output of a new orb sits, so a graph laid out here opens over its orb the way a
    // fresh one does.
    private static readonly float3 OutputAt = new(0.02f, 0f, 0f);

    private readonly ScriptGraphSpace _space;
    private readonly ScriptGraphNode _output;
    private readonly Dictionary<string, ScriptGraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<string> _problems = new();

    public ShaderGraphBuilder(ShaderGraphSource source)
    {
        _space = source.Graph.Target ?? throw new ArgumentException("the material has no graph", nameof(source));
        _output = _space.Nodes.FirstOrDefault(n => n.NodeType.Value == "MaterialOutput")
            ?? throw new ArgumentException("the graph has no Material Output", nameof(source));
        _nodes["output"] = _output;
    }

    public ScriptGraphSpace Space => _space;

    public int NodeCount => _nodes.Count;

    // Wires the graph refused, as "from -> to.port: why".
    public IReadOnlyList<string> Problems => _problems;

    public readonly struct In
    {
        public readonly string? Text;
        public readonly double[]? Numbers;

        private In(string? text, double[]? numbers)
        {
            Text = text;
            Numbers = numbers;
        }

        public static implicit operator In(string text) => new(text, null);

        public static implicit operator In(double value) => new(null, new[] { value });

        public static In Of(params double[] values) => new(null, values);
    }

    public static In V2(double x, double y) => In.Of(x, y);

    public static In V3(double x, double y, double z) => In.Of(x, y, z);

    // A picked color, sRGB like every color a person picks.
    public static In Rgb(double r, double g, double b) => In.Of(r, g, b, 1.0);

    public string Node(string id, string type, string? signature, params (string Port, In Value)[] inputs)
    {
        if (_nodes.ContainsKey(id))
            throw new ArgumentException($"'{id}' is already in the graph", nameof(id));
        var node = ScriptGraphEditing.MakeNode(_space, type, signature, float3.Zero, out var error)
            ?? throw new InvalidOperationException($"{id} ({type}{(signature != null ? " " + signature : "")}): {error}");
        _nodes[id] = node;
        Into(id, inputs);
        return id;
    }

    public string Node(string id, string type, params (string Port, In Value)[] inputs) => Node(id, type, null, inputs);

    // More inputs for a node already made; the Material Output is "output".
    public void Into(string id, params (string Port, In Value)[] inputs)
    {
        var node = _nodes[id];
        foreach (var (port, value) in inputs)
        {
            if (value.Text != null && !IsSetting(node, port))
                Wire(id, node, port, value.Text);
        }
        foreach (var (port, value) in inputs)
        {
            if (value.Text != null && IsSetting(node, port))
                node.SetValue(port, GraphValue.Of(value.Text));
            else if (value.Numbers != null)
                node.SetValue(port, Fit(id, node, port, value.Numbers));
        }
    }

    // The common ones, each a node of its own name.
    public string Add(string id, In a, In b) => Node(id, "Add", ("a", a), ("b", b));

    public string Sub(string id, In a, In b) => Node(id, "Subtract", ("a", a), ("b", b));

    public string Mul(string id, In a, In b) => Node(id, "Multiply", ("a", a), ("b", b));

    public string Div(string id, In a, In b) => Node(id, "Divide", ("a", a), ("b", b));

    public string Max(string id, In a, In b) => Node(id, "Maximum", ("a", a), ("b", b));

    public string Min(string id, In a, In b) => Node(id, "Minimum", ("a", a), ("b", b));

    public string Pow(string id, In value, In exponent) => Node(id, "Power", ("base", value), ("exponent", exponent));

    public string Mix(string id, In a, In b, In factor) => Node(id, "Mix", ("a", a), ("b", b), ("factor", factor));

    // Mix for two colors, typed up front so a number given for either end is a gray.
    public string MixColors(string id, In a, In b, In factor) => Node(id, "Mix", "color,color,float", ("a", a), ("b", b), ("factor", factor));

    public string Smooth(string id, In value, In edge0, In edge1) => Node(id, "Smoothstep", ("value", value), ("edge0", edge0), ("edge1", edge1));

    public string Step(string id, In edge, In value) => Node(id, "Step", ("edge", edge), ("value", value));

    public string Map(string id, In value, In fromMin, In fromMax, In toMin, In toMax)
        => Node(id, "MapRange", ("value", value), ("fromMin", fromMin), ("fromMax", fromMax), ("toMin", toMin), ("toMax", toMax));

    public string Unary(string id, string type, In value) => Node(id, type, ("value", value));

    public string Color(string id, double r, double g, double b) => Node(id, "RGB", ("value", Rgb(r, g, b)));

    public string Float(string id, string name, double value, double min, double max)
        => Node(id, "FloatProperty", ("name", name), ("value", value), ("min", min), ("max", max));

    public string ColorProperty(string id, string name, double r, double g, double b)
        => Node(id, "ColorProperty", ("name", name), ("value", Rgb(r, g, b)));

    // A plane of two parts of a vector, for the 2D nodes.
    public string Pair(string id, In x, In y) => Node(id, "CombineXYZ", "float,float", ("x", x), ("y", y));

    private void Wire(string id, ScriptGraphNode node, string port, string from)
    {
        int dot = from.IndexOf('.');
        string fromId = dot >= 0 ? from[..dot] : from;
        if (!_nodes.TryGetValue(fromId, out var source))
            throw new ArgumentException($"{id}.{port} reads '{fromId}', which is not in the graph");
        string output = dot >= 0 ? from[(dot + 1)..] : source.Overload?.Outputs.FirstOrDefault()?.Name ?? "result";
        if (!ScriptGraphEditing.Wire(node, false, port, source, output, out var error))
            _problems.Add($"{from} -> {id}.{port}: {error}");
    }

    private static bool IsSetting(ScriptGraphNode node, string port)
        => PortType(node, port) is { } type && (type == ScriptTypes.String || type.ClrType.IsEnum);

    private static ScriptType? PortType(ScriptGraphNode node, string port)
    {
        if (node.Overload is not { } overload)
            return null;
        int index = overload.InputIndex(port);
        return index >= 0 ? overload.Inputs[index].Type : null;
    }

    // A number into a vector or color port fills every part, a color's alpha 1; three parts into a color
    // get an alpha of 1.
    private GraphValue Fit(string id, ScriptGraphNode node, string port, double[] numbers)
    {
        var type = PortType(node, port);
        if (type == null)
            throw new ArgumentException($"{id} ({node.NodeType.Value}) has no input '{port}'");
        bool color = type.Name is "color" or "colorHDR";
        int width = type.Name switch
        {
            "float2" => 2,
            "float3" => 3,
            "float4" or "color" or "colorHDR" => 4,
            _ => 1,
        };
        if (numbers.Length == width)
            return width == 1 ? GraphValue.Of(numbers[0]) : GraphValue.Of(numbers);
        if (numbers.Length == 1)
        {
            var filled = Enumerable.Repeat(numbers[0], width).ToArray();
            if (color)
                filled[3] = 1.0;
            return GraphValue.Of(filled);
        }
        if (color && numbers.Length == 3)
            return GraphValue.Of(new[] { numbers[0], numbers[1], numbers[2], 1.0 });
        throw new ArgumentException($"{id}.{port} is {type.Name}; {numbers.Length} numbers do not fit it");
    }

    public readonly record struct Bounds(float Left, float Right, float Top, float Bottom)
    {
        public float Width => Right - Left;

        public float Height => Top - Bottom;
    }

    // Columns as late as each node can go, so it stands just left of the first thing reading it; then rows
    // from the output leftward, each column's nodes in the order of their readers and centred on them. The
    // whole graph is then moved so the Material Output sits where a new orb's does. The bounds are in the
    // graph's own space.
    public Bounds Layout()
    {
        var nodes = _space.Nodes.ToList();
        var readers = nodes.ToDictionary(n => n, _ => new List<ScriptGraphNode>());
        foreach (var node in nodes)
        {
            foreach (var wire in node.Inputs)
            {
                if (wire.Node.Target is ScriptGraphNode from && readers.ContainsKey(from))
                    readers[from].Add(node);
            }
        }

        var depth = new Dictionary<ScriptGraphNode, int>();
        int Depth(ScriptGraphNode node, int guard)
        {
            if (depth.TryGetValue(node, out var known))
                return known;
            int best = 0;
            foreach (var wire in node.Inputs)
            {
                if (wire.Node.Target is ScriptGraphNode from && readers.ContainsKey(from) && guard < 512)
                    best = System.Math.Max(best, Depth(from, guard + 1) + 1);
            }
            depth[node] = best;
            return best;
        }
        foreach (var node in nodes)
            Depth(node, 0);

        var columns = new Dictionary<ScriptGraphNode, int>();
        int Column(ScriptGraphNode node, int guard)
        {
            if (columns.TryGetValue(node, out var known))
                return known;
            int column = ReferenceEquals(node, _output) || readers[node].Count == 0 || guard > 512
                ? depth[node]
                : readers[node].Min(r => Column(r, guard + 1)) - 1;
            columns[node] = column;
            return column;
        }
        foreach (var node in nodes)
            Column(node, 0);

        // Each column is as wide as its widest card: a fixed step let wide cards reach into the next column.
        var columnX = new Dictionary<int, float>();
        float x = 0f;
        foreach (var column in nodes.GroupBy(n => columns[n]).OrderBy(g => g.Key))
        {
            columnX[column.Key] = x;
            x += column.Max(Width) + ColumnGap;
        }

        var centres = new Dictionary<ScriptGraphNode, float>();
        foreach (var group in nodes.GroupBy(n => columns[n]).OrderByDescending(g => g.Key))
        {
            var keyed = group.Select((node, index) =>
            {
                var ys = readers[node].Where(centres.ContainsKey).Select(r => centres[r]).ToList();
                return (Node: node, Key: ys.Count > 0 ? ys.Average() : -index * 0.2f);
            }).OrderByDescending(k => k.Key).ToList();
            var tops = new List<float>();
            float cursor = float.MaxValue;
            foreach (var (node, key) in keyed)
            {
                float h = Height(node);
                float top = MathF.Min(key + h * 0.5f, cursor);
                tops.Add(top);
                cursor = top - h - RowGap;
            }
            float wanted = keyed.Average(k => k.Key);
            float got = keyed.Select((k, i) => tops[i] - Height(k.Node) * 0.5f).Average();
            float shift = wanted - got;
            for (int i = 0; i < keyed.Count; i++)
            {
                var node = keyed[i].Node;
                float top = tops[i] + shift;
                node.Slot.LocalPosition.Value = new float3(columnX[group.Key], top, 0f);
                centres[node] = top - Height(node) * 0.5f;
            }
        }

        var move = OutputAt - _output.Slot.LocalPosition.Value;
        foreach (var node in nodes)
            node.Slot.LocalPosition.Value += move;

        return new Bounds(
            nodes.Min(n => n.Slot.LocalPosition.Value.x),
            nodes.Max(n => n.Slot.LocalPosition.Value.x + Width(n)),
            nodes.Max(n => n.Slot.LocalPosition.Value.y),
            nodes.Min(n => n.Slot.LocalPosition.Value.y - Height(n)));
    }

    public static float Height(ScriptGraphNode node) => ScriptCardLayout.For(node, 0).Height * ScriptEditorStyle.CardScale;

    public static float Width(ScriptGraphNode node) => ScriptCardLayout.For(node, 0).Width * ScriptEditorStyle.CardScale;
}
