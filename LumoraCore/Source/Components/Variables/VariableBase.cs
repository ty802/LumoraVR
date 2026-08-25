// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Variables;

// Shared lifecycle for everything that takes part in a named variable: the path member, the scope
// binding, and the rules for when the binding is rebuilt.
//
// Registration is driven entirely by events. It happens on start, on enable, whenever a member of
// the component changes, whenever a scope above is renamed or destroyed, and whenever the slot
// chain above moves. Nothing here runs per frame, and nothing polls for a scope. -xlinka
public abstract class VariableBase<T> : Component, IVariableParticipant<T>, ICustomInspectorUI
{
    // "Scope/Name", or just "Name" to bind to the nearest scope that accepts it. Empty binds nothing.
    public readonly Sync<string> VariableName;

    protected VariableBinding<T>? binding;

    protected VariableBase()
    {
        VariableName = new Sync<string>(this, "");
    }

    // Drivers are write-only: they consume the value and never supply one.
    public abstract bool IsWriteOnly { get; }

    // Whether this participant replaces an existing value when it binds, rather than adopting it.
    public abstract bool OverridesOnBind { get; }

    public virtual bool AcceptsWrites => !IsWriteOnly;

    // False for drivers and for field bindings with no target yet.
    protected abstract bool HasLocalValue { get; }

    // The setter is what the manager's broadcast lands in.
    protected abstract T LocalValue { get; set; }

    string? IVariableParticipant.VariablePath => VariableName.Value;

    public T VariableValue
    {
        get => HasLocalValue ? LocalValue : (binding != null ? binding.LastValue : default!);
        set
        {
            LocalValue = value;
            if (binding != null)
                binding.LastValue = value;
        }
    }

    // For tooling and for subclasses that report it.
    public VariableScope? BoundScope => binding?.Scope;

    public bool HasVariable => binding?.HasVariable ?? false;

    public override void OnAwake()
    {
        base.OnAwake();
        binding = new VariableBinding<T>(Slot, this, MarkChangeDirty, () => LocalValue);
    }

    public override void OnStart()
    {
        base.OnStart();
        RefreshBinding();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        RefreshBinding();
    }

    public override void OnDuplicate()
    {
        base.OnDuplicate();
        RefreshBinding();
    }

    public override void OnEnabled()
    {
        base.OnEnabled();
        MarkScopeDirty();
        RefreshBinding();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        binding?.Unbind();
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);

        // Binds after scopes have settled their names and before resets fire. See VariableLoadOrder.
        control.OnLoaded(() => RefreshBinding(), VariableLoadOrder.Binding);
    }

    public override void OnDestroy()
    {
        binding?.Dispose();
        binding = null;
        base.OnDestroy();
    }

    public void MarkScopeDirty() => binding?.MarkScopeDirty();

    public virtual bool RefreshBinding()
    {
        if (binding == null)
            return false;

        // A disabled variable leaves the scope entirely rather than sitting in it as a silent
        // participant, so disabling one really does take its value out of the identity.
        if (IsDestroyed || !Enabled.Value)
        {
            binding.Unbind();
            return false;
        }

        return binding.Refresh(VariableName.Value, HasLocalValue);
    }

    public virtual void BuildInspectorBody(UIBuilder ui)
    {
        string path = VariableName.Value ?? "";
        InspectorStats.AddRow(ui, "Path", string.IsNullOrWhiteSpace(path) ? "<unbound>" : path);

        Variables.ParsePath(path, out var parsedScope, out var parsedName);
        if (!string.IsNullOrWhiteSpace(path) && parsedName == null)
            InspectorStats.AddRow(ui, "Path", "REJECTED (illegal characters)");
        else if (path.Contains('/') && parsedScope == null)
            InspectorStats.AddRow(ui, "Scope name", "REJECTED (illegal characters)");

        var scope = binding?.Scope;
        InspectorStats.AddRow(ui, "Scope", scope == null
            ? "none found"
            : string.IsNullOrEmpty(scope.ActiveName) ? "<unnamed>" : scope.ActiveName!);

        InspectorStats.AddRow(ui, "Identity", $"{typeof(T).Name} \"{binding?.BoundVariableName ?? ""}\"");
        InspectorStats.AddRow(ui, "Role", IsWriteOnly ? "driver (write only)" : "provides value");

        var manager = binding?.Manager;
        InspectorStats.AddRow(ui, "Participants", manager == null
            ? "not registered"
            : $"{manager.ReadableCount} readable / {manager.ParticipantCount} total");
        InspectorStats.AddRow(ui, "Current value", manager == null ? "-" : manager.ValueToString());

        BuildExtraInspectorRows(ui);
    }

    protected virtual void BuildExtraInspectorRows(UIBuilder ui)
    {
    }
}
