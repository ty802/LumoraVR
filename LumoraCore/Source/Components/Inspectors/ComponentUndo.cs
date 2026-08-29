// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

// a destroyed component is serialized to a data tree first so undo can re-attach the same type and
// load its state back; an attached/duplicated component undoes by snapshotting-then-destroying, so
// redo can bring it back with any edits it had.
//
// reference handling rides the persistence layer: the batch keeps ONE ReferenceTranslator across
// save and restore, so outbound references to still-live world elements resolve straight back
// (their GUID->RefID pairs were recorded at save time), and the restored component re-binds its own
// saved GUIDs to its new RefIDs. what does NOT come back: INBOUND references other components held
// to the destroyed one (cleared at destroy, unknowable here) and non-persistent members (the save
// path skips them). restore attaches with runOnAttachBehavior:false - same as the world loader - so
// OnAttach defaults don't stomp the loaded state.
public sealed class ComponentExistenceUndoBatch : IUndoBatch
{
    private readonly World _world;
    private readonly Slot _slot;
    private readonly Type _componentType;
    private readonly ReferenceTranslator _translator;
    private readonly bool _isDestroy; // true: undo restores; false (attach): undo destroys

    private DataTreeNode? _saved;
    private Component? _live;

    public string Description { get; }

    private ComponentExistenceUndoBatch(World world, Slot slot, Type componentType,
        ReferenceTranslator translator, bool isDestroy, DataTreeNode? saved, Component? live)
    {
        _world = world;
        _slot = slot;
        _componentType = componentType;
        _translator = translator;
        _isDestroy = isDestroy;
        _saved = saved;
        _live = live;
        Description = $"{(isDestroy ? "Destroy" : "Attach")} {componentType.Name}";
    }

    // snapshots the component, destroys it, and hands back the batch (null if it can't snapshot)
    internal static ComponentExistenceUndoBatch? CreateDestroy(Component target)
    {
        if (target == null || target.IsDestroyed || target.Slot == null || target.World == null)
            return null;

        var translator = new ReferenceTranslator();
        var batch = new ComponentExistenceUndoBatch(target.World, target.Slot, target.GetType(),
            translator, isDestroy: true, saved: null, live: target);
        if (!batch.Snapshot())
            return null;
        batch.DestroyLive();
        return batch;
    }

    // tracks a freshly attached/duplicated component so undo can remove it again
    internal static ComponentExistenceUndoBatch? CreateAttach(Component attached)
    {
        if (attached == null || attached.IsDestroyed || attached.Slot == null || attached.World == null)
            return null;
        return new ComponentExistenceUndoBatch(attached.World, attached.Slot, attached.GetType(),
            new ReferenceTranslator(), isDestroy: false, saved: null, live: attached);
    }

    public bool Undo() => _isDestroy ? Restore() : SnapshotAndDestroy();

    public bool Redo() => _isDestroy ? SnapshotAndDestroy() : Restore();

    public void OnEvicted() { }

    // serializes the live component's current state (keeps edits made since the batch was made)
    private bool Snapshot()
    {
        if (_live == null || _live.IsDestroyed)
            return false;
        try
        {
            // SaveNonPersistent: undo must capture components on non-persistent slots (spawned items) too.
            var control = new SaveControl(_slot, _translator) { SaveNonPersistent = true };
            // Claim identities for what this component points at first, so a ref to one of its own
            // members resolves on restore regardless of which member the writer reached first.
            control.ReserveWorkerIdentities(_live);
            _saved = _live.Save(control);
            return _saved != null;
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"ComponentUndo: snapshot of {_componentType.Name} failed: {ex.Message}");
            return false;
        }
    }

    private bool SnapshotAndDestroy()
    {
        if (!Snapshot())
            return false;
        DestroyLive();
        return true;
    }

    private void DestroyLive()
    {
        if (_live != null && !_live.IsDestroyed)
            _live.Destroy();
        _live = null;
    }

    private bool Restore()
    {
        if (_slot == null || _slot.IsDestroyed || _saved == null)
            return false;
        try
        {
            var component = _slot.AttachComponent(_componentType, runOnAttachBehavior: false);
            if (component == null)
                return false;
            var loadControl = new LoadControl(_world, _translator);
            component.Load(_saved, loadControl);
            loadControl.FinishLoad();
            _live = component;
            return true;
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"ComponentUndo: restore of {_componentType.Name} failed: {ex.Message}");
            return false;
        }
    }
}

// record helpers mirroring InspectorUndo.RecordEdit ergonomics
public static class ComponentUndo
{
    // destroys a component undoably (serialize-then-destroy). falls back to a plain destroy when
    // there's no undo manager, so the button always works.
    public static void RecordDestroy(Worker context, Component target)
    {
        var manager = FindManager(context);
        if (manager == null)
        {
            target?.Destroy();
            return;
        }
        var batch = ComponentExistenceUndoBatch.CreateDestroy(target!);
        if (batch != null)
            manager.Record(batch);
        else
            target?.Destroy(); // couldn't snapshot: still honor the destroy, just without undo
    }

    // makes a just-attached (or duplicated) component undoable
    public static void RecordAttach(Worker context, Component attached)
    {
        var manager = FindManager(context);
        var batch = manager != null ? ComponentExistenceUndoBatch.CreateAttach(attached) : null;
        if (batch != null)
            manager!.Record(batch);
    }

    private static UndoManager? FindManager(Worker context)
        => context?.World?.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>();
}
