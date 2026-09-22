// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Touch;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Interaction/Touch")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class TouchSetValue<T> : TouchResponder
{
    public readonly SyncRef<IField<T>> TargetValue;

    public readonly Sync<T> Value;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public TouchSetValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Value = new Sync<T>(this, SyncCoder.GetDefault<T>());
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = TargetValue.Target;
        if (target != null && target.CanWrite)
            target.Value = Value.Value;
    }
}
