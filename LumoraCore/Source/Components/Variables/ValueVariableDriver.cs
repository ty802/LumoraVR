// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Variables;

// Reads a named variable and drives a field with it. This is the consuming half of the system: no
// reference to whoever declared the variable, just a name and a target.
//
// A driver never supplies a value, so a variable that only has drivers on it counts as absent and
// every one of them falls back to its own DefaultValue. That is what makes a driver safe to author
// against a variable the content might not carry. -xlinka
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueVariableDriver<T> : VariableBase<T>
{
    public readonly FieldDrive<T> Target;

    // Written to the target whenever the named variable does not exist.
    public readonly Sync<T> DefaultValue;

    public ValueVariableDriver()
    {
        Target = new FieldDrive<T>(this);
        DefaultValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
    }

    // See Variables.IsSupportedValueType.
    public static bool IsValidGenericType => Variables.IsSupportedValueType(typeof(T));

    public override bool IsWriteOnly => true;

    public override bool OverridesOnBind => false;

    protected override bool HasLocalValue => false;

    protected override T LocalValue
    {
        get => default!;
        set => PushToTarget(value);
    }

    public override void OnStart()
    {
        base.OnStart();
        PushToTarget(binding != null ? binding.LastValue : DefaultValue.Value);
    }

    public override void OnChanges()
    {
        base.OnChanges();

        // Unconditional push on a changes pass, which only runs when something about this component
        // actually moved: the bound value, the target link resolving, or the default being edited.
        PushToTarget(binding != null ? binding.LastValue : DefaultValue.Value);
    }

    private void PushToTarget(T value)
    {
        if (binding == null || !binding.HasVariable)
            value = DefaultValue.Value;

        // A no-op unless the drive link was granted, so there is nothing to check first.
        Target.SetValue(value);
    }

    protected override void BuildExtraInspectorRows(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Target", Target.Field == null
            ? "none"
            : Target.IsLinkValid ? Target.Field.ParentHierarchyToString() : "linked, not granted");
        InspectorStats.AddRow(ui, "Source", HasVariable ? "variable" : "default value");
    }
}
