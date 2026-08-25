// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// A bare synchronized value with nothing attached to it.
//
// Somewhere to point copies, drives and button actions at when the value does not belong to any
// existing component: a shared counter, a mode flag, a colour several materials read. Without one,
// building anything stateful without code means hijacking an unrelated component's field. -xlinka
[ComponentCategory("Utility/Values")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueHolder<T> : Component
{
    public readonly Sync<T> Value;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueHolder()
    {
        Value = new Sync<T>(this, SyncCoder.GetDefault<T>());
    }
}

// A bare synchronized reference with nothing attached to it. See ValueHolder.
[ComponentCategory("Utility/Values")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceHolder<T> : Component where T : class, IWorldElement
{
    public readonly SyncRef<T> Reference;

    public ReferenceHolder()
    {
        Reference = new SyncRef<T>(this);
    }
}
