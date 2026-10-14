// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Moves and turns a body pose by a fixed amount, in the space of the user it belongs to: the offset is
// added to the position and the rotation is turned by RotationOffset first.
[ComponentCategory("Users/Avatar")]
public class PoseOffset : Component, IPoseFilter
{
    public readonly Sync<float3> PositionOffset = new(float3.Zero);

    public readonly Sync<floatQ> RotationOffset = new(floatQ.Identity);

    public void ProcessPose(AvatarSocket socket, Slot space, ref float3 position, ref floatQ rotation, ref bool isTracking)
    {
        if (!Enabled.Value)
            return;
        position += PositionOffset.Value;
        rotation = (RotationOffset.Value * rotation).Normalized;
    }
}
