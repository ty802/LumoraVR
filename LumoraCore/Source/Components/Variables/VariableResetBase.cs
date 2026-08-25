// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Variables;

// Writes a fixed value into a named variable when the content it lives in arrives somewhere new.
// Anything an author wants to start from a known state - a toggle that should come back off, a
// counter that should come back at zero - rather than from whatever the last session left in it.
//
// Both triggers are load-shaped in this engine. A world load and a spawned or pasted object both
// run the same deferred post-load pass, so ResetOnLoad covers both, and duplication has its own
// pass. There is no third path to hang a flag off, so there isn't a third flag. -xlinka
public abstract class VariableResetBase<T> : Component, ICustomInspectorUI
{
    // "Scope/Name" of the variable to write.
    public readonly Sync<string> VariableName;

    // Write the reset value after a world load or after this object is spawned or pasted in.
    public readonly Sync<bool> ResetOnLoad;

    // Write the reset value into the copy after this object is duplicated.
    public readonly Sync<bool> ResetOnDuplicate;

    private VariableWriteResult _lastResult = VariableWriteResult.NotFound;
    private bool _everRan;

    protected VariableResetBase()
    {
        VariableName = new Sync<string>(this, "");
        ResetOnLoad = new Sync<bool>(this, true);
        ResetOnDuplicate = new Sync<bool>(this, true);
    }

    protected abstract T ResetVariableValue { get; }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);

        // Runs after scopes have named themselves and after variables have registered. Firing any
        // earlier would find an empty scope and write nothing at all. See VariableLoadOrder.
        if (ResetOnLoad.Value)
            control.OnLoaded(RunReset, VariableLoadOrder.Reset);
    }

    public override void OnDuplicate()
    {
        base.OnDuplicate();
        if (!ResetOnDuplicate.Value)
            return;

        // Deferred to the end of the update: duplication calls OnDuplicate on every copied component
        // in attach order, and the variable this writes may not have rebound to the clone's own scope
        // yet when this one's turn comes.
        World?.RunSynchronously(RunReset);
    }

    // Whatever the triggers say.
    public void RunReset()
    {
        if (IsDestroyed || Slot == null || Slot.IsDestroyed)
            return;

        _lastResult = Slot.WriteVariable(VariableName.Value, ResetVariableValue);
        _everRan = true;
        MarkChangeDirty();
    }

    public virtual void BuildInspectorBody(UIBuilder ui)
    {
        string path = VariableName.Value ?? "";
        InspectorStats.AddRow(ui, "Path", string.IsNullOrWhiteSpace(path) ? "<unbound>" : path);
        InspectorStats.AddRow(ui, "Triggers", $"load {(ResetOnLoad.Value ? "on" : "off")}, duplicate {(ResetOnDuplicate.Value ? "on" : "off")}");
        InspectorStats.AddRow(ui, "Last reset", _everRan ? _lastResult.ToString() : "has not run");
    }
}
