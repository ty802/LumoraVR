// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

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
