// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.


namespace Lumora.Core.Components.Utility;

// Drives one value field from another.
//
// The push runs every frame rather than only when the source changes. A drive here is a member, but a
// plain reference does NOT relay its target's change event back to the owning component, so nothing
// would wake this up when the source moved. The push costs one equality compare: a driven write that
// matches the field's current value returns before touching sync data or firing events. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class CopyValue<T> : Component
{
    public readonly SyncRef<IField<T>> Source;

    public readonly FieldDrive<T> Target;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public CopyValue()
    {
        Source = new SyncRef<IField<T>>(this);
        // Both peers hold the same source field and run the same copy, so broadcasting the result would
        // double the traffic the source already costs.
        Target = new FieldDrive<T>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        if (source == null || ReferenceEquals(source, Target.Target))
            return;
        Target.SetValue(source.Value);
    }
}

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
