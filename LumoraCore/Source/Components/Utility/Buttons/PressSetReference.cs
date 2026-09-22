// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Buttons")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class PressSetReference<T> : ButtonAction where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> TargetReference;

    // Leave empty to clear the target.
    public readonly SyncRef<T> Value;

    public PressSetReference()
    {
        TargetReference = new SyncRef<SyncRef<T>>(this);
        Value = new SyncRef<T>(this);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetReference.Target;
        if (target != null && target.CanWrite)
            target.Target = Value.Target;
    }
}
