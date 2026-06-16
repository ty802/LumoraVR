// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components;

/// <summary>
/// One reversible user action. Undo/Redo return false when the batch can no
/// longer apply (targets destroyed); the manager then drops it. OnEvicted is
/// the permanent-cleanup hook for batches that park resources (graveyard slots).
/// </summary>
public interface IUndoBatch
{
    string Description { get; }
    bool Undo();
    bool Redo();
    void OnEvicted();
}

/// <summary>
/// Local-user undo/redo history, one per user. Holds plain local state (no sync) -
/// undo history is a per-client concern.
/// </summary>
[ComponentCategory("Users")]
public class UndoManager : Component
{
    private const int MaxHistory = 64;

    private readonly List<IUndoBatch> _undo = new();
    private readonly List<IUndoBatch> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoDescription => CanUndo ? _undo[^1].Description : null;
    public string? NextRedoDescription => CanRedo ? _redo[^1].Description : null;

    public void Record(IUndoBatch batch)
    {
        if (batch == null)
            return;

        _undo.Add(batch);

        // A new action invalidates the redo branch.
        foreach (var stale in _redo)
            SafeEvict(stale);
        _redo.Clear();

        while (_undo.Count > MaxHistory)
        {
            SafeEvict(_undo[0]);
            _undo.RemoveAt(0);
        }
    }

    public bool Undo()
    {
        // Skip over batches whose targets no longer exist.
        while (_undo.Count > 0)
        {
            var batch = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            if (batch.Undo())
            {
                _redo.Add(batch);
                return true;
            }
            SafeEvict(batch);
        }
        return false;
    }

    public bool Redo()
    {
        while (_redo.Count > 0)
        {
            var batch = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            if (batch.Redo())
            {
                _undo.Add(batch);
                return true;
            }
            SafeEvict(batch);
        }
        return false;
    }

    public override void OnDestroy()
    {
        foreach (var batch in _undo)
            SafeEvict(batch);
        foreach (var batch in _redo)
            SafeEvict(batch);
        _undo.Clear();
        _redo.Clear();
        base.OnDestroy();
    }

    private static void SafeEvict(IUndoBatch batch)
    {
        try
        {
            batch.OnEvicted();
        }
        catch (System.Exception ex)
        {
            Logging.Logger.Warn($"UndoManager: batch eviction failed: {ex.Message}");
        }
    }
}
