// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class BoolToValue<T> : Component
{
    // Point a drive or a button at this.
    public readonly Sync<bool> State;

    public readonly Sync<T> TrueValue;

    public readonly Sync<T> FalseValue;

    public readonly FieldDrive<T> Target;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public BoolToValue()
    {
        State = new Sync<bool>(this, false);
        TrueValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        FalseValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Target = new FieldDrive<T>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        Target.SetValue(State.Value ? TrueValue.Value : FalseValue.Value);
    }
}
