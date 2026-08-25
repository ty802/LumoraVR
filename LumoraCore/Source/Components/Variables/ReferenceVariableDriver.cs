// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.Variables;

// Reads a named reference variable and drives a reference field with it. Reference-typed twin of
// ValueVariableDriver<T>, with the same write-only contract: it never supplies a value, and falls
// back to its own default when the variable is absent.
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements)]
public class ReferenceVariableDriver<T> : VariableBase<T> where T : class, IWorldElement
{
    public readonly DriveRef<T> Target;

    // Written to the target whenever the named variable does not exist.
    public readonly SyncRef<T> DefaultValue;

    public ReferenceVariableDriver()
    {
        Target = new DriveRef<T>(this);
        DefaultValue = new SyncRef<T>(this);
    }

    public override bool IsWriteOnly => true;

    public override bool OverridesOnBind => false;

    protected override bool HasLocalValue => false;

    protected override T LocalValue
    {
        get => null!;
        set => PushToTarget(value);
    }

    public override void OnStart()
    {
        base.OnStart();
        PushToTarget(binding?.LastValue!);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        PushToTarget(binding?.LastValue!);
    }

    private void PushToTarget(T? value)
    {
        if (binding == null || !binding.HasVariable)
            value = DefaultValue.Target;

        if (value is { IsDestroyed: true })
            value = null;

        Target.SetValue(value);
    }

    protected override void BuildExtraInspectorRows(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Target", Target.Target == null
            ? "none"
            : Target.IsLinkValid ? Target.Target.ParentHierarchyToString() : "linked, not granted");
        InspectorStats.AddRow(ui, "Source", HasVariable ? "variable" : "default target");
    }
}
