// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Scripting;

namespace Lumora.Core.Components.Scripting;

// Where nodes write, drawn in the world: a line from the port that names the target to the target, an arrowhead
// at its end and a bead on the target, one colour to a target so two nodes writing one thing read as one. A
// field's line ends on its inspector row while a panel shows it, else on the field's object; a Write Value's
// ends on its Stored Value's card.
//
// Local to whoever looks, and nothing is drawn or worked out while nothing is shown. What is shown is looked up
// again twice a second or when the nodes asked about change; the lines are built again only when an end has
// moved a millimetre. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptWriteMarks : Component
{
    private const double ScanSeconds = 0.5;
    private const float LineWidth = 0.6f;
    private const float ArrowLength = 0.028f;
    private const float ArrowSpread = 0.012f;
    private const float TargetBead = 0.012f;
    private const float PortBead = 0.005f;
    private const float Quantum = 0.001f;

    private readonly List<ScriptWriteTarget> _targets = new();
    private readonly List<ScriptGraphSpace> _spaces = new();
    private readonly List<ScriptGraphNode> _focus = new();
    private readonly Dictionary<IWorldElement, color> _colours = new();
    private readonly List<ScriptWireCurve> _curves = new();
    private readonly List<(float3 Center, float Radius, color Tint)> _beads = new();
    private readonly HashSet<IWorldElement> _beaded = new();
    private readonly Dictionary<IWorldElement, List<ReferenceProxySource>> _rows = new();
    private Slot? _lineSlot;
    private Slot? _beadSlot;
    private ScriptWireMesh? _lines;
    private ScriptPulseMesh? _beadMesh;
    private bool _all;
    private bool _shown;
    private double _nextScan;
    private int _stamp;

    // What is shown now, writer by writer.
    public IReadOnlyList<ScriptWriteTarget> Targets => _targets;

    public int LineCount => _shown ? _curves.Count : 0;

    // The lines drawn last, three to a target: the line, then its arrowhead's two strokes.
    public IReadOnlyList<ScriptWireCurve> Lines => _curves;

    public bool IsShown => _shown;

    public color ColourOf(IWorldElement target) => _colours.TryGetValue(target, out var tint) ? tint : DashTheme.Text;

    public static ScriptWriteMarks Make(World world)
        => world.RootSlot.AddLocalSlot("Script write marks").AttachComponent<ScriptWriteMarks>();

    // Shows where every writer in every open script writes, or only the given nodes.
    public void Show(bool all, IReadOnlyList<ScriptGraphNode> focus)
    {
        double now = World?.Time.TotalTime ?? 0.0;
        if (!_shown || all != _all || !SameFocus(focus) || now >= _nextScan)
        {
            _all = all;
            _focus.Clear();
            _focus.AddRange(focus);
            Scan();
            _nextScan = now + ScanSeconds;
        }
        _shown = true;
        Draw();
    }

    public void Hide()
    {
        if (!_shown)
            return;
        _shown = false;
        _targets.Clear();
        _focus.Clear();
        _colours.Clear();
        _rows.Clear();
        _curves.Clear();
        _stamp = 0;
        foreach (var slot in new[] { _lineSlot, _beadSlot })
        {
            if (slot is { IsDestroyed: false } && slot.ActiveSelf.Value)
                slot.ActiveSelf.Value = false;
        }
    }

    public override void OnDestroy()
    {
        _targets.Clear();
        _colours.Clear();
        base.OnDestroy();
    }

    private bool SameFocus(IReadOnlyList<ScriptGraphNode> focus)
    {
        if (focus.Count != _focus.Count)
            return false;
        for (int i = 0; i < focus.Count; i++)
        {
            if (!ReferenceEquals(focus[i], _focus[i]))
                return false;
        }
        return true;
    }

    private void Scan()
    {
        _targets.Clear();
        if (_all)
        {
            ScriptGraphSpace.GetAll(World, _spaces);
            foreach (var space in _spaces)
            {
                if (space.IsDestroyed || !space.IsShown || space.Kind.Value != GraphKind.Script)
                    continue;
                foreach (var node in space.Nodes)
                    ScriptWriteTargets.Collect(node, _targets);
            }
        }
        else
        {
            foreach (var node in _focus)
            {
                if (node is { IsDestroyed: false } && node.Space is { IsShown: true })
                    ScriptWriteTargets.Collect(node, _targets);
            }
        }
        // A target keeps its colour while it stays shown; new ones step round the wheel from the last. The rows
        // showing a field are found here, and only their points looked up each frame.
        _rows.Clear();
        foreach (var target in _targets)
        {
            if (!_colours.ContainsKey(target.Target))
                _colours[target.Target] = Hue(_colours.Count);
            if (target.Target is not ScriptGraphNode && !_rows.ContainsKey(target.Target))
            {
                var rows = new List<ReferenceProxySource>();
                ReferenceProxySource.RowsOf(World, target.Target, rows);
                _rows[target.Target] = rows;
            }
        }
    }

    // Hues a golden turn apart, so the first few are as far apart as they can be and no two neighbours match.
    private static color Hue(int index)
    {
        float h = (index * 0.618034f + 0.08f) % 1f * 6f;
        float x = 1f - MathF.Abs(h % 2f - 1f);
        const float low = 0.28f;
        float r, g, b;
        switch ((int)h)
        {
            case 0: (r, g, b) = (1f, x, 0f); break;
            case 1: (r, g, b) = (x, 1f, 0f); break;
            case 2: (r, g, b) = (0f, 1f, x); break;
            case 3: (r, g, b) = (0f, x, 1f); break;
            case 4: (r, g, b) = (x, 0f, 1f); break;
            default: (r, g, b) = (1f, 0f, x); break;
        }
        return new color(low + r * (1f - low), low + g * (1f - low), low + b * (1f - low), 1f);
    }

    private void Draw()
    {
        var world = World;
        if (world == null)
            return;
        _curves.Clear();
        _beads.Clear();
        _beaded.Clear();
        var hash = new HashCode();
        foreach (var target in _targets)
        {
            if (target.Writer.IsDestroyed || target.Target.IsDestroyed || !TryFrom(target, out var from) || !TryTo(target.Target, out var to))
                continue;
            var line = to - from;
            float length = line.Length;
            if (length < TargetBead * 2f)
                continue;
            var tint = ColourOf(target.Target);
            var direction = line / length;
            var tip = to - direction * TargetBead;
            var side = float3.Cross(direction, float3.Up);
            if (side.LengthSquared < 1e-4f)
                side = float3.Cross(direction, float3.Right);
            side = side.Normalized;
            var back = tip - direction * ArrowLength;
            _curves.Add(new ScriptWireCurve(from, tip, tint, tint, Straight: true, Width: LineWidth));
            _curves.Add(new ScriptWireCurve(back + side * ArrowSpread, tip, tint, tint, Straight: true, Width: LineWidth));
            _curves.Add(new ScriptWireCurve(back - side * ArrowSpread, tip, tint, tint, Straight: true, Width: LineWidth));
            _beads.Add((from, PortBead, tint));
            if (_beaded.Add(target.Target))
                _beads.Add((to, TargetBead, tint));
            Stamp(ref hash, from);
            Stamp(ref hash, to);
            hash.Add(tint);
        }
        hash.Add(_curves.Count);
        int stamp = hash.ToHashCode();
        if (stamp == _stamp && _lineSlot is { IsDestroyed: false, ActiveSelf.Value: true })
            return;
        _stamp = stamp;
        Ensure();
        _lines!.SetWires(_curves);
        _beadMesh!.SetBeads(_beads);
    }

    private static void Stamp(ref HashCode hash, float3 point)
    {
        hash.Add((int)MathF.Round(point.x / Quantum));
        hash.Add((int)MathF.Round(point.y / Quantum));
        hash.Add((int)MathF.Round(point.z / Quantum));
    }

    private void Ensure()
    {
        if (_lineSlot == null || _lineSlot.IsDestroyed)
        {
            _lineSlot = Slot.AddLocalSlot("Lines");
            _lines = _lineSlot.AttachComponent<ScriptWireMesh>();
            ScriptGraphView.Draw(_lineSlot, _lines);
        }
        if (_beadSlot == null || _beadSlot.IsDestroyed)
        {
            _beadSlot = Slot.AddLocalSlot("Beads");
            _beadMesh = _beadSlot.AttachComponent<ScriptPulseMesh>();
            ScriptGraphView.Draw(_beadSlot, _beadMesh);
        }
        if (!_lineSlot.ActiveSelf.Value)
            _lineSlot.ActiveSelf.Value = true;
        if (!_beadSlot.ActiveSelf.Value)
            _beadSlot.ActiveSelf.Value = true;
    }

    // The port on the writer's card that names the target, or the card's left edge by its header.
    private static bool TryFrom(in ScriptWriteTarget target, out float3 point)
    {
        var node = target.Writer;
        point = default;
        if (node.Space is not { IsShown: true } space)
            return false;
        if (space.View is { } view && view.TryPortPoint(node, target.PortKind, target.Port, out point))
            return true;
        point = node.Slot.LocalPointToGlobal(ScriptEditorStyle.CanvasToLocal(new float2(0f, -ScriptEditorStyle.HeaderHeight * 0.5f)));
        return true;
    }

    private bool TryTo(IWorldElement target, out float3 point)
    {
        point = default;
        if (target is ScriptGraphNode stored)
        {
            if (stored.Space is not { IsShown: true } space)
                return false;
            float width = (space.View?.CardOf(stored)?.Layout ?? ScriptCardLayout.For(stored, 0)).Width;
            point = stored.Slot.LocalPointToGlobal(ScriptEditorStyle.CanvasToLocal(new float2(width * 0.5f, -ScriptEditorStyle.HeaderHeight * 0.5f)));
            return true;
        }
        if (_rows.TryGetValue(target, out var rows))
        {
            foreach (var row in rows)
            {
                if (row.TryRowPoint(out point))
                    return true;
            }
        }
        if (ScriptWriteTargets.SlotOf(target) is not { IsRootSlot: false } slot)
            return false;
        point = slot.GlobalPosition;
        return true;
    }
}
