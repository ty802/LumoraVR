// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

// Reversible structural change to a sync collection: add, remove, or reorder, one step each.
//
// An element is a sync member, not a value, so putting a removed one back means re-creating it and
// replaying its serialized state - the same save/load path the world loader uses, sharing one
// translator with this record so references the element held resolve back to their live targets.
// The rebuilt element gets a FRESH RefID; references pointing AT the old element from elsewhere are
// not restored, which is the same deal the rest of the destroy-undo path offers.
//
// Reorder has two implementations and picks the cheaper one. Swapping the VALUES of two neighbours
// leaves both elements (and their RefIDs) in place, so nothing pointing at them notices - that is
// the good path, and it covers reference and field lists, which is most of what the inspector
// shows. Element types with no assignable value (a list of sync objects) fall back to
// serialize-remove-insert-replay, which does mint a new RefID. -xlinka
public sealed class ListElementUndoBatch : IUndoBatch
{
    private enum Op { Add, Remove, Move }

    private readonly ISyncList _list;
    private readonly ReferenceTranslator _translator = new();
    private readonly Op _op;
    private readonly int _index;
    private readonly int _target;
    private readonly Action? _onChanged;

    private DataTreeNode? _saved;
    private readonly List<RefID> _dead = new();

    public string Description { get; }

    private ListElementUndoBatch(ISyncList list, Op op, int index, int target, Action? onChanged)
    {
        _list = list;
        _op = op;
        _index = index;
        _target = target;
        _onChanged = onChanged;
        Description = op switch
        {
            Op.Add => "Add List Element",
            Op.Remove => "Remove List Element",
            _ => "Reorder List",
        };
    }

    // the new element comes back through added so the caller can seed it
    public static ListElementUndoBatch? Add(ISyncList? list, out ISyncMember? added, Action? onChanged = null)
    {
        added = null;
        if (!IsUsable(list))
            return null;
        try
        {
            added = list!.AddElement();
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"ListElementUndo: add failed: {ex.Message}");
            return null;
        }
        return added == null ? null : new ListElementUndoBatch(list!, Op.Add, list!.Count - 1, -1, onChanged);
    }

    // null means nothing was removed
    public static ListElementUndoBatch? Remove(ISyncList? list, int index, Action? onChanged = null)
    {
        if (!IsUsable(list) || index < 0 || index >= list!.Count)
            return null;

        var batch = new ListElementUndoBatch(list, Op.Remove, index, -1, onChanged);
        if (!batch.CaptureAt(index))
            return null;
        return batch.RemoveAt(index) ? batch : null;
    }

    // null means nothing moved
    public static ListElementUndoBatch? Move(ISyncList? list, int from, int to, Action? onChanged = null)
    {
        if (!IsUsable(list) || from == to)
            return null;
        if (from < 0 || to < 0 || from >= list!.Count || to >= list.Count)
            return null;

        var batch = new ListElementUndoBatch(list, Op.Move, from, to, onChanged);
        return batch.MoveElement(from, to) ? batch : null;
    }

    public bool Undo()
    {
        bool applied = _op switch
        {
            Op.Add => CaptureAt(_index) && RemoveAt(_index),
            Op.Remove => Restore(_index),
            _ => MoveElement(_target, _index),
        };
        if (applied)
            _onChanged?.Invoke();
        return applied;
    }

    public bool Redo()
    {
        bool applied = _op switch
        {
            Op.Add => Restore(_index),
            Op.Remove => CaptureAt(_index) && RemoveAt(_index),
            _ => MoveElement(_index, _target),
        };
        if (applied)
            _onChanged?.Invoke();
        return applied;
    }

    public void OnEvicted() { }

    private static bool IsUsable(ISyncList? list)
        => list is SyncElement { IsDestroyed: false };

    // Serialize the element in place and remember the identities it is about to lose, so their GUIDs
    // stop resolving to the dead element before anything asks for them again.
    private bool CaptureAt(int index)
    {
        if (!IsUsable(_list) || index < 0 || index >= _list.Count)
            return false;
        var element = _list.GetElement(index);
        if (element == null)
            return false;

        _saved = UndoSerialization.SaveMember(element, _list as IWorldElement, _translator);
        if (_saved == null)
            return false;

        _dead.Clear();
        UndoSerialization.CollectIdentities(element, _dead);
        return true;
    }

    private bool RemoveAt(int index)
    {
        if (!IsUsable(_list) || index < 0 || index >= _list.Count)
            return false;
        try
        {
            _list.RemoveElement(index);
            return true;
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"ListElementUndo: remove at {index} failed: {ex.Message}");
            return false;
        }
    }

    private bool Restore(int index)
    {
        if (!IsUsable(_list) || _saved == null)
            return false;
        var world = (_list as SyncElement)?.World;
        if (world == null)
            return false;

        index = global::System.Math.Clamp(index, 0, _list.Count);
        try
        {
            UndoSerialization.Forget(_translator, _dead);
            var element = index == _list.Count ? _list.AddElement() : _list.InsertElement(index);
            if (element == null)
                return false;
            var control = new LoadControl(world, _translator);
            element.Load(_saved, control);
            control.FinishLoad();

            // This record can run again (redo, then undo), and next time the element it snapshots is
            // the one that exists NOW, so track its identities instead of the dead ones.
            _dead.Clear();
            UndoSerialization.CollectIdentities(element, _dead);
            return true;
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"ListElementUndo: restore at {index} failed: {ex.Message}");
            return false;
        }
    }

    private bool MoveElement(int from, int to)
    {
        if (!IsUsable(_list) || from == to)
            return false;
        if (from < 0 || to < 0 || from >= _list.Count || to >= _list.Count)
            return false;

        if (global::System.Math.Abs(from - to) == 1 && TrySwapValues(_list.GetElement(from), _list.GetElement(to)))
            return true;

        // No assignable value to swap: rebuild the element at the destination instead.
        if (!CaptureAt(from) || !RemoveAt(from))
            return false;
        return Restore(to);
    }

    internal static bool TrySwapValues(ISyncMember? a, ISyncMember? b)
    {
        if (a == null || b == null)
            return false;

        if (a is ISyncRef refA && b is ISyncRef refB)
        {
            // The raw RefID, never the resolved target: Target reads null for a destroyed or
            // not-yet-resolved reference, and writing that null back would ERASE the reference on
            // what the user asked for as a mere reorder. The RefID swap is lossless in every state.
            var value = refA.Value;
            refA.Value = refB.Value;
            refB.Value = value;
            return true;
        }

        if (a is IField fieldA && b is IField fieldB && fieldA.ValueType == fieldB.ValueType)
        {
            object? value = fieldA.BoxedValue;
            fieldA.BoxedValue = fieldB.BoxedValue!;
            fieldB.BoxedValue = value!;
            return true;
        }

        return false;
    }
}
