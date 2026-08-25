// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Variables;

// Resets a named reference variable to a fixed target on load and on duplicate.
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements)]
public class ReferenceVariableReset<T> : VariableResetBase<T> where T : class, IWorldElement
{
    // Empty clears the variable.
    public readonly SyncRef<T> ResetTarget;

    public ReferenceVariableReset()
    {
        ResetTarget = new SyncRef<T>(this);
    }

    protected override T ResetVariableValue => ResetTarget.Target;
}
