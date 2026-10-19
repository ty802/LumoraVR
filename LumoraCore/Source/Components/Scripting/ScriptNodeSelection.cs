// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// The nodes selected in a world by every Loom tool in it, as one set the cards read. Each machine keeps its
// own from the tools' synced lists, so everyone sees a selection, and rebuilds it only when a list changes:
// asking costs a card one lookup a frame. A hand holding secondary on a node is about to select or deselect
// that node's set, and those cards ramp toward what they will be, on the holder's machine only. -xlinka
public sealed class ScriptNodeSelection
{
    // What a selected card's ground is pulled toward, and how far.
    public static readonly color Tint = new color(0.18f, 0.86f, 0.96f, 1f).ToLinear();
    public const float Pull = 0.42f;

    // While a list names a node this machine has not got yet, how often it looks again, in seconds.
    private const double RetrySeconds = 0.5;

    private static readonly ConditionalWeakTable<World, ScriptNodeSelection> Worlds = new();

    private readonly World _world;
    private readonly List<SyncRefList<ScriptGraphNode>> _lists = new();
    private readonly HashSet<ScriptGraphNode> _selected = new();
    private readonly Dictionary<object, (HashSet<ScriptGraphNode> Nodes, bool Selecting, float Amount)> _holds = new();
    private readonly Action<SyncRefList<ScriptGraphNode>> _changed;
    private bool _dirty;
    private double _retryAt = double.PositiveInfinity;

    private ScriptNodeSelection(World world)
    {
        _world = world;
        _changed = _ => _dirty = true;
    }

    // This world's, made on first use.
    public static ScriptNodeSelection For(World world) => Worlds.GetValue(world, static w => new ScriptNodeSelection(w));

    // This world's, or null while no tool in it has asked for one.
    public static ScriptNodeSelection? Of(World? world) => world != null && Worlds.TryGetValue(world, out var selection) ? selection : null;

    public void Watch(SyncRefList<ScriptGraphNode> list)
    {
        if (_lists.Contains(list))
            return;
        _lists.Add(list);
        list.OnChanged += _changed;
        _dirty = true;
    }

    public void Forget(SyncRefList<ScriptGraphNode> list)
    {
        if (!_lists.Remove(list))
            return;
        list.OnChanged -= _changed;
        _dirty = true;
    }

    public bool Contains(ScriptGraphNode node)
    {
        Refresh();
        return _selected.Contains(node);
    }

    // How far a node's card is pulled toward the tint: all the way while it is selected, partway while a hold
    // ramps it toward being selected or not.
    public float Highlight(ScriptGraphNode node)
    {
        Refresh();
        float amount = _selected.Contains(node) ? 1f : 0f;
        foreach (var hold in _holds.Values)
        {
            if (hold.Nodes.Contains(node))
                amount = hold.Selecting ? MathF.Max(amount, hold.Amount) : MathF.Min(amount, 1f - hold.Amount);
        }
        return amount;
    }

    // A hold on a set by its owner, from nothing to all the way when it toggles. No nodes ends it.
    public void SetHold(object owner, HashSet<ScriptGraphNode>? nodes, bool selecting, float amount)
    {
        if (nodes == null)
            _holds.Remove(owner);
        else
            _holds[owner] = (nodes, selecting, System.Math.Clamp(amount, 0f, 1f));
    }

    private void Refresh()
    {
        if (!_dirty && _world.Time.TotalTime < _retryAt)
            return;
        _dirty = false;
        _retryAt = double.PositiveInfinity;
        _selected.Clear();
        foreach (var list in _lists)
        {
            foreach (var reference in list.Elements)
            {
                if (reference.Target is { IsDestroyed: false } node)
                    _selected.Add(node);
                else if (!reference.Value.IsNull)
                    _retryAt = _world.Time.TotalTime + RetrySeconds;
            }
        }
    }
}
