// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Variables;

// Resets a named value variable to a fixed value on load and on duplicate.
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueVariableReset<T> : VariableResetBase<T>
{
    public readonly Sync<T> ResetValue;

    public ValueVariableReset()
    {
        ResetValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
    }

    // See Variables.IsSupportedValueType.
    public static bool IsValidGenericType => Variables.IsSupportedValueType(typeof(T));

    protected override T ResetVariableValue => ResetValue.Value;
}
