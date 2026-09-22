// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Touch;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Interaction/Touch")]
public class TouchToggleBool : TouchResponder
{
    public readonly SyncRef<IField<bool>> TargetValue;

    public TouchToggleBool()
    {
        TargetValue = new SyncRef<IField<bool>>(this);
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = TargetValue.Target;
        if (target != null && target.CanWrite)
            target.Value = !target.Value;
    }
}
