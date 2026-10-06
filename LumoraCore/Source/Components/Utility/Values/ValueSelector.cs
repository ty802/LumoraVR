// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Drives the entry the index picks. Works when something it reads changed: the index, an entry, or somebody
// writing over the field it drives.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueSelector<T> : Component
{
    // Out-of-range indices wrap.
    public readonly Sync<int> Index;

    public readonly SyncFieldList<T> Values;

    public readonly HoldingFieldDrive<T> Target;

    // A write that lands on the target goes into the entry it is showing, so editing the shown value edits
    // the list.
    public readonly Sync<bool> WriteBack;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    private ChangeFollow? _follow;

    public ValueSelector()
    {
        Index = new Sync<int>(this, 0);
        Values = new SyncFieldList<T>();
        Target = new HoldingFieldDrive<T>(this) { LocalValueOnly = true };
        WriteBack = new Sync<bool>(this, false);
        Target.Holds = () => WriteBack.Value;
        Target.SetupValueSetHook(WriteEntry);
    }

    private void WriteEntry(SyncField<T> field, T value)
    {
        int count = Values.Count;
        if (count > 0)
            Values[SelectionIndex.Wrap(Index.Value, count)] = value;
    }

    // The type default when the list is empty.
    public T Selected
    {
        get
        {
            int count = Values.Count;
            if (count == 0)
                return Networking.Sync.SyncCoder.GetDefault<T>();
            return Values[SelectionIndex.Wrap(Index.Value, count)];
        }
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
        follow.AddList(Values);
        follow.Add(Target.Target);
        follow.End();

        if (Values.Count == 0)
            return;
        follow.BeginQuiet();
        try
        {
            Target.SetValue(Selected);
        }
        finally
        {
            follow.EndQuiet();
        }
    }
}
