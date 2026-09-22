// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

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
