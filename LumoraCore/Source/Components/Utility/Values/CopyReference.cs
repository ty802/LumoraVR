// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.


namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class CopyReference<T> : Component where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> Source;

    public readonly DriveRef<T> Target;

    public CopyReference()
    {
        Source = new SyncRef<SyncRef<T>>(this);
        Target = new DriveRef<T>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        if (source == null || ReferenceEquals(source, Target.Target))
            return;
        Target.SetValue(source.Target);
    }
}
