// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Touch;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Interaction/Touch")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class TouchSetReference<T> : TouchResponder where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> TargetReference;

    // Leave empty to clear the target.
    public readonly SyncRef<T> Value;

    public TouchSetReference()
    {
        TargetReference = new SyncRef<SyncRef<T>>(this);
        Value = new SyncRef<T>(this);
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = TargetReference.Target;
        if (target != null && target.CanWrite)
            target.Target = Value.Target;
    }
}

// Advances a reference field to the next element in a list when the control is worked, wrapping at
// the end.
[ComponentCategory("Interaction/Touch")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class TouchCycleReference<T> : TouchResponder where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> TargetReference;

    public readonly SyncRefList<T> References;

    public TouchCycleReference()
    {
        TargetReference = new SyncRef<SyncRef<T>>(this);
        References = new SyncRefList<T>();
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = TargetReference.Target;
        int count = References.Count;
        if (target == null || !target.CanWrite || count == 0)
            return;

        var current = target.Target;
        int index = -1;
        for (int i = 0; i < count; i++)
        {
            if ((object?)References[i] == (object?)current)
            {
                index = i;
                break;
            }
        }

        // Not in the list (including "points at nothing") starts the cycle at the first entry.
        index = index < 0 ? 0 : SelectionIndex.Wrap(index + 1, count);
        target.Target = References[index]!;
    }
}

// With DestroyWholeObject set the request climbs to the nearest slot above the target carrying a
// DestroyRootMarker and destroys that instead, so a delete pad buried inside a prop removes the whole
// prop rather than the panel it happens to sit on. -xlinka
[ComponentCategory("Interaction/Touch")]
public class TouchDestroy : TouchResponder
{
    // Defaults to this slot when empty.
    public readonly SyncRef<Slot> Target;

    public readonly Sync<bool> DestroyWholeObject;

    public TouchDestroy()
    {
        Target = new SyncRef<Slot>(this);
        DestroyWholeObject = new Sync<bool>(this, false);
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = Target.Target ?? Slot;
        if (target == null || target.IsDestroyed)
            return;

        if (DestroyWholeObject.Value)
            target = DestroyRootMarker.FindRoot(target);

        // Never take the world's root slot down with a touch.
        if (target == null || target.IsDestroyed || target.IsRootSlot)
            return;

        target.Destroy();
    }
}
