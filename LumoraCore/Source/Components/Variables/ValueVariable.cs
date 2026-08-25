// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Variables;

// Declares a named variable and holds its value. This is the ordinary way an item or an avatar
// exposes a knob: attach one, name it, and anything under the same scope can read it, write it or
// drive off it without ever holding a reference to this component.
//
// The value is a plain synced member, so it replicates and saves like any other. The scope's table
// is rebuilt per peer from these components, never sent. -xlinka
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueVariable<T> : VariableBase<T>
{
    public readonly Sync<T> Value;

    // When this variable binds to a scope that already has a value under the same identity: adopt
    // that value (default), or replace it with this one.
    public readonly Sync<bool> OverrideOnBind;

    public ValueVariable()
    {
        Value = new Sync<T>(this, SyncCoder.GetDefault<T>());
        OverrideOnBind = new Sync<bool>(this, false);
    }

    // See Variables.IsSupportedValueType.
    public static bool IsValidGenericType => Variables.IsSupportedValueType(typeof(T));

    public override bool IsWriteOnly => false;

    public override bool OverridesOnBind => OverrideOnBind.Value;

    // A driven Value would take the write and then lose it again on the drive's next pass, so the
    // scope says the write was refused instead of pretending it landed.
    public override bool AcceptsWrites => !Value.IsDriven;

    protected override bool HasLocalValue => true;

    protected override T LocalValue
    {
        get => Value.Value;
        set => Value.Value = value;
    }
}
