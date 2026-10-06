// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.


namespace Lumora.Core.Components.Utility;

// Drives one value field from another.
//
// Copies when something it reads changed: the source field, its own members, or somebody writing over the
// field it drives (the drive wins again at once, as it did when this pushed every frame). A world with six
// hundred of these paid six hundred calls a frame to copy values that had not moved. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class CopyValue<T> : Component
{
    public readonly SyncRef<IField<T>> Source;

    public readonly HoldingFieldDrive<T> Target;

    // A write that lands on the target goes into the source instead, so the two stay one value both ways.
    public readonly Sync<bool> WriteBack;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    private ChangeFollow? _follow;

    public CopyValue()
    {
        Source = new SyncRef<IField<T>>(this);
        // Both peers hold the same source field and run the same copy, so broadcasting the result would
        // double the traffic the source already costs.
        Target = new HoldingFieldDrive<T>(this) { LocalValueOnly = true };
        WriteBack = new Sync<bool>(this, false);
        Target.Holds = () => WriteBack.Value;
        Target.SetupValueSetHook(CopyBack);
    }

    private void CopyBack(SyncField<T> field, T value)
    {
        var source = Source.Target;
        if (source != null && !ReferenceEquals(source, field) && source.CanWrite)
            source.Value = value;
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

        if (source == null || ReferenceEquals(source, Target.Target))
            return;
        follow.BeginQuiet();
        try
        {
            Target.SetValue(source.Value);
        }
        finally
        {
            follow.EndQuiet();
        }
    }
}
