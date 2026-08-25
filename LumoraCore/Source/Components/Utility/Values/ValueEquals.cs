// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueEquals<T> : Component
{
    // A cleared reference compares as the type default.
    public readonly SyncRef<IField<T>> Source;

    public readonly Sync<T> Reference;

    // Drive true when they DIFFER instead.
    public readonly Sync<bool> Invert;

    public readonly FieldDrive<bool> Target;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueEquals()
    {
        Source = new SyncRef<IField<T>>(this);
        Reference = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Invert = new Sync<bool>(this, false);
        Target = new FieldDrive<bool>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        var value = source != null ? source.Value : SyncCoder.GetDefault<T>();
        // Same comparison the sync layer uses to decide a field changed, so "equal" here means exactly
        // what "no delta was sent" means.
        bool equal = SyncCoder.Equals(value, Reference.Value);
        Target.SetValue(Invert.Value ? !equal : equal);
    }
}

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceEquals<T> : Component where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> Source;

    // Leave empty to test for "points at nothing".
    public readonly SyncRef<T> Reference;

    // Drive true when they DIFFER instead.
    public readonly Sync<bool> Invert;

    public readonly FieldDrive<bool> Target;

    public ReferenceEquals()
    {
        Source = new SyncRef<SyncRef<T>>(this);
        Reference = new SyncRef<T>(this);
        Invert = new Sync<bool>(this, false);
        Target = new FieldDrive<bool>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        var current = source?.Target;
        bool equal = (object?)current == (object?)Reference.Target;
        Target.SetValue(Invert.Value ? !equal : equal);
    }
}
