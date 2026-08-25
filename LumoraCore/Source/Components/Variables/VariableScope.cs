// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Variables;

// Declares a named variable scope on this slot's subtree. Variables under it bind by NAME instead
// of by reference, which is what lets an item or an avatar expose a knob that anything inside it
// can read or drive without either side holding a ref to the other.
//
// The manager table is derived, per-peer, local state. Nothing in it replicates or saves: the
// variable components and their synced values are the truth, and every peer rebuilds the same table
// from them. That is also why the table is rebuilt lazily off a dirty flag rather than per frame -
// a scope with a hundred variables under it must cost nothing when nothing moved. -xlinka
[ComponentCategory("Data/Variables")]
public class VariableScope : Component, ICustomInspectorUI
{
    // Empty means the scope is only reachable as "the nearest unnamed-binding scope above me".
    public readonly Sync<string> ScopeName;

    // When set, this scope only serves variables that name it explicitly, and a variable with no
    // scope prefix looks straight past it to the next scope up.
    public readonly Sync<bool> DirectBindingOnly;

    private readonly Dictionary<VariableIdentity, VariableManager> _managers = new();
    private string? _activeName;
    private bool _appliedDirectOnly;
    private bool _stateApplied;

    public VariableScope()
    {
        ScopeName = new Sync<string>(this, "");
        DirectBindingOnly = new Sync<bool>(this, false);
    }

    // Null when ScopeName holds characters a variable path could never spell, so a bad name binds
    // nothing instead of binding something surprising.
    public string? ActiveName => _activeName;

    public int VariableCount => _managers.Count;

    public IEnumerable<VariableManager> LiveVariables => _managers.Values;

    public override void OnStart()
    {
        base.OnStart();
        RefreshScopeState();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        RefreshScopeState();
    }

    public override void OnDuplicate()
    {
        base.OnDuplicate();
        RefreshScopeState();

        // A clone's variables copied their paths verbatim but bound (if at all) against the SOURCE
        // tree's scopes. Every duplicated component's data is already in place by the time this runs,
        // so rebinding the whole subtree here lands them on the clone's own scope.
        Slot?.ForeachComponentInChildren<Component>(component =>
        {
            if (component is IVariableParticipant participant)
                participant.RefreshBinding();
        });
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);

        // Order matters across the whole system on load: scopes settle their names first (0),
        // variables bind second (5), resets write third (10). A reset that fired before its variable
        // had registered would silently write nothing. -xlinka
        control.OnLoaded(RefreshScopeState, VariableLoadOrder.Scope);
    }

    public override void OnDestroy()
    {
        foreach (var manager in _managers.Values)
            manager.DetachAll();
        _managers.Clear();
        base.OnDestroy();
    }

    // False when nothing readable is registered under that identity, which includes "only drivers
    // are listening".
    public bool TryRead<T>(string? name, out T value)
    {
        var manager = GetManager<T>(name, createIfMissing: false);
        if (manager == null || manager.ReadableCount == 0)
        {
            value = Networking.Sync.SyncCoder.GetDefault<T>();
            return false;
        }
        value = manager.Value;
        return true;
    }

    public VariableWriteResult TryWrite<T>(string? name, T value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return VariableWriteResult.Invalid;

        var manager = GetManager<T>(name, createIfMissing: false);
        if (manager == null || manager.ReadableCount == 0)
        {
            // Distinguish "nobody declared this" from "somebody declared it as another type", which
            // is the single most confusing way for a variable write to fail.
            return HasNameUnderAnyType(name!) ? VariableWriteResult.TypeMismatch : VariableWriteResult.NotFound;
        }

        if (!manager.IsValidValue(value))
            return VariableWriteResult.Invalid;

        if (!manager.HasWritableParticipant())
            return VariableWriteResult.NotWritable;

        manager.SetValue(value);
        return VariableWriteResult.Success;
    }

    // Creates the identity if it is new.
    internal VariableManager<T>? Register<T>(string name, IVariableParticipant<T> participant)
    {
        var manager = GetManager<T>(name, createIfMissing: true);
        manager?.Register(participant);
        return manager;
    }

    internal VariableManager<T>? GetManager<T>(string? name, bool createIfMissing)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var identity = new VariableIdentity(typeof(T), name!);
        if (_managers.TryGetValue(identity, out var existing))
            return (VariableManager<T>)existing;
        if (!createIfMissing)
            return null;

        var created = AllocateManager<T>(name!);
        _managers.Add(identity, created);
        return created;
    }

    internal void RemoveManager(VariableManager manager)
    {
        _managers.Remove(new VariableIdentity(manager.ValueType, manager.Name));
    }

    private VariableManager<T> AllocateManager<T>(string name)
    {
        if (typeof(T) == typeof(Type))
            return (VariableManager<T>)(object)new TypeVariableManager(name, this);
        return new VariableManager<T>(name, this);
    }

    private bool HasNameUnderAnyType(string name)
    {
        foreach (var manager in _managers.Values)
        {
            if (string.Equals(manager.Name, name, StringComparison.Ordinal) && manager.ReadableCount > 0)
                return true;
        }
        return false;
    }

    // Both of a scope's members change WHICH variables resolve to it, so either one moving re-homes
    // the whole subtree: the name decides who can address it by name, and the direct-binding flag
    // decides whether unprefixed variables see it at all. Guarded on an actual change, since
    // OnChanges fires for any member write including Enabled. -xlinka
    private void RefreshScopeState()
    {
        var processed = Variables.ProcessName(ScopeName.Value);
        bool directOnly = DirectBindingOnly.Value;

        if (_stateApplied && string.Equals(processed, _activeName, StringComparison.Ordinal) && directOnly == _appliedDirectOnly)
            return;

        _activeName = processed;
        _appliedDirectOnly = directOnly;
        _stateApplied = true;
        MarkSubtreeDirty();
    }

    private void MarkSubtreeDirty()
    {
        Slot?.ForeachComponentInChildren<Component>(component =>
        {
            if (component is IVariableParticipant participant)
                participant.MarkScopeDirty();
        });
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Scope name", string.IsNullOrEmpty(_activeName) ? "<unnamed>" : _activeName!);
        if (!Variables.IsValidName(ScopeName.Value))
            InspectorStats.AddRow(ui, "Name", "REJECTED (illegal characters)");
        InspectorStats.AddRow(ui, "Binding", DirectBindingOnly.Value ? "direct only" : "default + direct");
        InspectorStats.AddRow(ui, "Live variables", _managers.Count.ToString());

        int shown = 0;
        foreach (var manager in _managers.Values)
        {
            if (shown >= 12)
            {
                InspectorStats.AddRow(ui, "", $"+{_managers.Count - shown} more");
                break;
            }
            shown++;
            InspectorStats.AddRow(ui,
                $"{manager.ValueType.Name} {manager.Name}",
                $"{manager.ValueToString()}  ({manager.ReadableCount}/{manager.ParticipantCount} readable)");
        }
    }
}

// A variable is identified by its value TYPE as well as its name, so "Size" as a float and "Size"
// as a float3 are two variables and neither one silently receives the other's writes.
internal readonly struct VariableIdentity : IEquatable<VariableIdentity>
{
    public readonly Type Type;
    public readonly string Name;

    public VariableIdentity(Type type, string name)
    {
        Type = type;
        Name = name;
    }

    public bool Equals(VariableIdentity other)
        => Type == other.Type && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is VariableIdentity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Type, StringComparer.Ordinal.GetHashCode(Name));
}

// Post-load ordering for the variable system. Deferred load actions run lowest first, and these
// three steps only produce the right answer in this order.
internal static class VariableLoadOrder
{
    public const int Scope = 0;
    public const int Binding = 5;
    public const int Reset = 10;
}
