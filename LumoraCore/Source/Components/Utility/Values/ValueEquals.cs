// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Drives true while the source equals the reference. Works when something it reads changed: its own
// members, the source field, or somebody writing over the flag it drives.
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

    private ChangeFollow? _follow;

    public ValueEquals()
    {
        Source = new SyncRef<IField<T>>(this);
        Reference = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Invert = new Sync<bool>(this, false);
        Target = new FieldDrive<bool>(this) { LocalValueOnly = true };
    }

    public override void OnDestroy()
    {
        _follow?.Clear();
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        var follow = _follow ??= new ChangeFollow(this);
        var source = Source.Target;
        follow.Begin();
        follow.AddRef(Source);
        follow.Add(Target.Target);
        follow.End();

        var value = source != null ? source.Value : SyncCoder.GetDefault<T>();
        // Same comparison the sync layer uses to decide a field changed, so "equal" here means exactly
        // what "no delta was sent" means.
        bool equal = SyncCoder.Equals(value, Reference.Value);
        follow.BeginQuiet();
        try
        {
            Target.SetValue(Invert.Value ? !equal : equal);
        }
        finally
        {
            follow.EndQuiet();
        }
    }
}
