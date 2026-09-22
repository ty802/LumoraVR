// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueSelector<T> : Component
{
    // Out-of-range indices wrap.
    public readonly Sync<int> Index;

    public readonly SyncFieldList<T> Values;

    public readonly FieldDrive<T> Target;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueSelector()
    {
        Index = new Sync<int>(this, 0);
        Values = new SyncFieldList<T>();
        Target = new FieldDrive<T>(this) { LocalValueOnly = true };
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

    public override void OnUpdate(float delta)
    {
        if (Values.Count > 0)
            Target.SetValue(Selected);
    }
}
