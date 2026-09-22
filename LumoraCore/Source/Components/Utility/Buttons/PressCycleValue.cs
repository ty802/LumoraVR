// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Advances a field to the next entry in a list when the button is pressed, wrapping at the end.
[ComponentCategory("Utility/Buttons")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class PressCycleValue<T> : ButtonAction
{
    public readonly SyncRef<IField<T>> TargetValue;

    public readonly SyncFieldList<T> Values;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public PressCycleValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Values = new SyncFieldList<T>();
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetValue.Target;
        int count = Values.Count;
        if (target == null || !target.CanWrite || count == 0)
            return;

        // A value that is not in the list starts the cycle at the first entry, rather than being
        // treated as index -1 and jumping to the last.
        int index = Values.IndexOf(target.Value);
        index = index < 0 ? 0 : SelectionIndex.Wrap(index + 1, count);
        target.Value = Values[index];
    }
}
