// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

// Replace a component with one of another type on the same slot, carrying over every member the two
// types share and re-aiming the references that pointed at the old one. Reversible as one step.
//
// A swap cannot keep the component's RefID - a component is created by its type and the ID comes
// with it - so identity is preserved the only way that matters to a user: everything that POINTED at
// the old component is walked and re-aimed at the new one. A reference whose declared target type
// will not accept the new component cannot be re-aimed and is cleared instead, because leaving it
// on a destroyed element is worse than an empty slot the user can see; those are counted and named
// in the log so the change is never silent.
//
// Members transfer by NAME with a matching value type, which is the only rule that holds across
// unrelated types. Anything the destination declares differently keeps whatever its own attach
// behaviour gave it, so a swap lands on sensible defaults rather than half-copied garbage. -xlinka
public sealed class ComponentTypeSwapUndoBatch : IUndoBatch
{
    private readonly World _world;
    private readonly Slot _slot;
    private readonly Type _fromType;
    private readonly Type _toType;
    private readonly ReferenceTranslator _translator = new();

    // The side that is currently absent, kept as data. Re-taken on every transition so edits made
    // between an undo and a redo survive.
    private DataTreeNode? _saved;
    private List<RefID> _dead = new();

    // Inbound references, captured once at the first swap. Repointed ones follow the live component;
    // cleared ones only come back on the undo, since the replacement never accepted them.
    private readonly List<ISyncRef> _repointed = new();
    private readonly List<ISyncRef> _cleared = new();

    private Component? _live;
    private bool _swapped;

    public string Description { get; }

    private ComponentTypeSwapUndoBatch(Component source, Type toType)
    {
        _world = source.World;
        _slot = source.Slot;
        _fromType = source.GetType();
        _toType = toType;
        _live = source;
        Description = $"Swap {_fromType.Name} to {toType.Name}";
    }

    // null when the swap could not run at all, in which case nothing was changed
    public static ComponentTypeSwapUndoBatch? Swap(Component? source, Type? toType)
    {
        if (source == null || source.IsDestroyed || source.Slot == null || source.World == null)
            return null;
        if (toType == null || toType == source.GetType() || !IsAttachable(toType))
            return null;

        var batch = new ComponentTypeSwapUndoBatch(source, toType);
        return batch.Apply(source, toType) ? batch : null;
    }

    public bool Undo() => _swapped && Transition(_fromType, forward: false);

    public bool Redo() => !_swapped && Transition(_toType, forward: true);

    public void OnEvicted() { }

    public static bool IsAttachable(Type type)
        => typeof(Component).IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters;

    // COMPATIBILITY
    // "Any component" is the wrong menu for a swap: offering to turn a MeshRenderer into an AudioClip
    // is noise. Two types are interchangeable when they answer to the same base - a Collider for a
    // Collider, a material provider for a material provider - because that base is what every
    // reference pointing at the old component was declared against. Types that hang straight off the
    // framework bases have no such family, so they are only interchangeable with themselves. -xlinka

    // null when it has none, which means the type has no swap candidates at all
    public static Type? SwapFamily(Type type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (IsFrameworkBase(current))
                return null;
            if (current.IsAbstract)
                return current;
        }
        return null;
    }

    public static bool IsCompatible(Type source, Type candidate)
    {
        if (candidate == source || !IsAttachable(candidate))
            return false;
        var family = SwapFamily(source);
        return family != null && family.IsAssignableFrom(candidate);
    }

    // Bases every component shares sooner or later. Reaching one means the walk left the type's own
    // family and found only plumbing.
    private static bool IsFrameworkBase(Type type)
    {
        if (type == typeof(Component) || type == typeof(Worker) || type == typeof(ImplementableComponent))
            return true;
        if (!type.IsGenericType)
            return false;
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(ComponentBase<>)
            || definition == typeof(ImplementableComponent<>)
            || definition == typeof(Core.Assets.AssetProvider<>);
    }

    private bool Transition(Type toType, bool forward)
    {
        var live = _live;
        if (live == null || live.IsDestroyed || _slot == null || _slot.IsDestroyed || _saved == null)
            return false;

        // Take the payload for the side being restored BEFORE the live side overwrites it.
        var restoreNode = _saved;
        var restoreDead = _dead;

        if (!Snapshot(live))
            return false;
        live.Destroy();
        _live = null;

        UndoSerialization.Forget(_translator, restoreDead);
        Component? restored;
        try
        {
            restored = _slot.AttachComponent(toType, runOnAttachBehavior: false);
            if (restored == null)
                return false;
            var control = new LoadControl(_world, _translator);
            restored.Load(restoreNode, control);
            control.FinishLoad();
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"TypeSwapUndo: restoring {toType.Name} failed: {ex.Message}");
            return false;
        }

        _live = restored;
        _swapped = forward;
        AimInbound(restored, clearUnaccepted: forward);
        return true;
    }

    private bool Apply(Component source, Type toType)
    {
        if (!Snapshot(source))
            return false;

        Component replacement;
        try
        {
            replacement = _slot.AttachComponent(toType);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"TypeSwap: attaching {toType.Name} failed: {ex.Message}");
            return false;
        }

        var skipped = replacement.CopyProperties(source);

        CaptureInbound(source, replacement);

        source.Destroy();
        _live = replacement;
        _swapped = true;
        AimInbound(replacement, clearUnaccepted: true);
        Report(skipped);
        return true;
    }

    private bool Snapshot(Component component)
    {
        _saved = UndoSerialization.SaveComponent(component, _translator);
        if (_saved == null)
            return false;
        _dead = new List<RefID>();
        UndoSerialization.CollectIdentities(component, _dead);
        return true;
    }

    // Every reference in the world aimed at the old component, split by whether the replacement is an
    // acceptable target for it. References living inside the component being replaced are skipped -
    // they die with it and come back from the snapshot.
    private void CaptureInbound(Component source, Component replacement)
    {
        var sourceId = source.ReferenceID;
        var replacementType = replacement.GetType();

        var found = new List<ISyncRef>();
        foreach (var entry in _world.ReferenceController.AllObjects)
        {
            if (entry.Value is ISyncRef syncRef && syncRef.Value == sourceId && !OwnedBy(syncRef, source))
                found.Add(syncRef);
        }

        foreach (var syncRef in found)
        {
            if (syncRef.TargetType.IsAssignableFrom(replacementType))
                _repointed.Add(syncRef);
            else
                _cleared.Add(syncRef);
        }
    }

    private static bool OwnedBy(ISyncRef syncRef, Component component)
    {
        var element = syncRef as IWorldElement;
        while (element != null)
        {
            if (ReferenceEquals(element, component))
                return true;
            element = (element as SyncElement)?.Parent;
        }
        return false;
    }

    private void AimInbound(Component target, bool clearUnaccepted)
    {
        foreach (var syncRef in _repointed)
        {
            if (syncRef is SyncElement { IsDestroyed: false })
                syncRef.Value = target.ReferenceID;
        }
        foreach (var syncRef in _cleared)
        {
            if (syncRef is not SyncElement { IsDestroyed: false })
                continue;
            if (clearUnaccepted)
                syncRef.Clear();
            else
                syncRef.Value = target.ReferenceID;
        }
    }

    private void Report(List<string> skipped)
    {
        var message = new StringBuilder();
        message.Append($"TypeSwap: {_fromType.Name} -> {_toType.Name} on '{_slot.Name.Value}'; ");
        message.Append($"{_repointed.Count} reference(s) re-aimed");
        if (_cleared.Count > 0)
        {
            message.Append($", {_cleared.Count} cleared (");
            for (int i = 0; i < _cleared.Count; i++)
            {
                if (i > 0)
                    message.Append(", ");
                message.Append((_cleared[i] as ISyncMember)?.Name ?? "?");
            }
            message.Append(')');
        }
        if (skipped.Count > 0)
            message.Append($"; {skipped.Count} member(s) not carried over: {string.Join(", ", skipped)}");
        Logging.Logger.Log(message.ToString());
    }
}
