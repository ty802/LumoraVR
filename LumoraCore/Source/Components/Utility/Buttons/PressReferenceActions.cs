// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Buttons")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class PressSetReference<T> : ButtonAction where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> TargetReference;

    // Leave empty to clear the target.
    public readonly SyncRef<T> Value;

    public PressSetReference()
    {
        TargetReference = new SyncRef<SyncRef<T>>(this);
        Value = new SyncRef<T>(this);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetReference.Target;
        if (target != null && target.CanWrite)
            target.Target = Value.Target;
    }
}

// Advances a reference field to the next element in a list when the button is pressed, wrapping at
// the end.
[ComponentCategory("Utility/Buttons")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class PressCycleReference<T> : ButtonAction where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> TargetReference;

    public readonly SyncRefList<T> References;

    public PressCycleReference()
    {
        TargetReference = new SyncRef<SyncRef<T>>(this);
        References = new SyncRefList<T>();
    }

    protected override void Pressed(User? actor)
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
// DestroyRootMarker and destroys that instead, so a delete button buried inside a prop removes the
// whole prop rather than the panel it happens to sit on. -xlinka
[ComponentCategory("Utility/Buttons")]
public class PressDestroy : ButtonAction
{
    // Defaults to this slot when empty.
    public readonly SyncRef<Slot> Target;

    public readonly Sync<bool> DestroyWholeObject;

    public PressDestroy()
    {
        Target = new SyncRef<Slot>(this);
        DestroyWholeObject = new Sync<bool>(this, false);
    }

    protected override void Pressed(User? actor)
    {
        var target = Target.Target ?? Slot;
        if (target == null || target.IsDestroyed)
            return;

        if (DestroyWholeObject.Value)
            target = DestroyRootMarker.FindRoot(target);

        // Never take the world's root slot down with a button press.
        if (target == null || target.IsDestroyed || target.IsRootSlot)
            return;

        target.Destroy();
    }
}
