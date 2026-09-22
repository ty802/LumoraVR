// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class BoolToReference<T> : Component where T : class, IWorldElement
{
    public readonly Sync<bool> State;

    public readonly SyncRef<T> TrueReference;

    public readonly SyncRef<T> FalseReference;

    public readonly DriveRef<T> Target;

    public BoolToReference()
    {
        State = new Sync<bool>(this, false);
        TrueReference = new SyncRef<T>(this);
        FalseReference = new SyncRef<T>(this);
        Target = new DriveRef<T>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        Target.SetValue(State.Value ? TrueReference.Target : FalseReference.Target);
    }
}
