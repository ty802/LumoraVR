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

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceSelector<T> : Component where T : class, IWorldElement
{
    // Out-of-range indices wrap.
    public readonly Sync<int> Index;

    public readonly SyncRefList<T> References;

    public readonly DriveRef<T> Target;

    public ReferenceSelector()
    {
        Index = new Sync<int>(this, 0);
        References = new SyncRefList<T>();
        Target = new DriveRef<T>(this) { LocalValueOnly = true };
    }

    // Null when the list is empty.
    public T? Selected
    {
        get
        {
            int count = References.Count;
            return count == 0 ? null : References[SelectionIndex.Wrap(Index.Value, count)];
        }
    }

    public override void OnUpdate(float delta)
    {
        if (References.Count > 0)
            Target.SetValue(Selected);
    }
}

// Index wrapping shared by the selector components.
public static class SelectionIndex
{
    // C# remainder keeps the sign of the dividend, so a negative index would land outside the list and
    // throw. Push it back into range instead of clamping: a cycling index should come round to the end,
    // not stick at zero. -xlinka
    public static int Wrap(int index, int count)
    {
        if (count <= 0)
            return 0;
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }
}
