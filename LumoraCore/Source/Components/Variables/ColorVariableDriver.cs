// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Variables;

// Reads a named colorHDR variable and drives a plain color field with it: one scene-wide colour onto every light
// and every tint that takes a plain colour. The same contract as ValueVariableDriver, for the one pair of types
// a variable cannot cross, since a variable binds by its exact type. The colour carries over as it is; nothing
// is clamped.
[ComponentCategory("Data/Variables")]
public class ColorVariableDriver : VariableBase<colorHDR>
{
    public readonly FieldDrive<color> Target;

    // Written to the target whenever the named variable does not exist.
    public readonly Sync<colorHDR> DefaultValue;

    public ColorVariableDriver()
    {
        Target = new FieldDrive<color>(this);
        DefaultValue = new Sync<colorHDR>(this, colorHDR.White);
    }

    public override bool IsWriteOnly => true;

    public override bool OverridesOnBind => false;

    protected override bool HasLocalValue => false;

    protected override colorHDR LocalValue
    {
        get => default;
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
        PushToTarget(binding != null ? binding.LastValue : DefaultValue.Value);
    }

    private void PushToTarget(colorHDR value)
    {
        if (binding == null || !binding.HasVariable)
            value = DefaultValue.Value;
        Target.SetValue(new color(value.r, value.g, value.b, value.a));
    }

    protected override void BuildExtraInspectorRows(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Target", Target.Field == null
            ? "none"
            : Target.IsLinkValid ? Target.Field.ParentHierarchyToString() : "linked, not granted");
        InspectorStats.AddRow(ui, "Source", HasVariable ? "variable" : "default value");
    }
}
