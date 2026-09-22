// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Buttons")]
public class PressToggle : ButtonAction
{
    public readonly SyncRef<IField<bool>> TargetValue;

    public PressToggle()
    {
        TargetValue = new SyncRef<IField<bool>>(this);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetValue.Target;
        if (target != null && target.CanWrite)
            target.Value = !target.Value;
    }
}
