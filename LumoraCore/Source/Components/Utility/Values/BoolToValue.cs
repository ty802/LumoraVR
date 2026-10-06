// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Drives one of two values by a flag. Works when one of its own members changed, or somebody wrote over
// the field it drives.
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

    private ChangeFollow? _follow;

    public BoolToValue()
    {
        State = new Sync<bool>(this, false);
        TrueValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        FalseValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Target = new FieldDrive<T>(this) { LocalValueOnly = true };
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
        follow.Begin();
        follow.Add(Target.Target);
        follow.End();

        follow.BeginQuiet();
        try
        {
            Target.SetValue(State.Value ? TrueValue.Value : FalseValue.Value);
        }
        finally
        {
            follow.EndQuiet();
        }
    }
}
