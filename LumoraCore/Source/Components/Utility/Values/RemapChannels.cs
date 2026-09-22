// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

public static class RemapChannels
{
    public static float Pick(RemapChannel channel, float x, float y, float z, float w) => channel switch
    {
        RemapChannel.X => x,
        RemapChannel.Y => y,
        RemapChannel.Z => z,
        RemapChannel.W => w,
        RemapChannel.NegativeX => -x,
        RemapChannel.NegativeY => -y,
        RemapChannel.NegativeZ => -z,
        RemapChannel.NegativeW => -w,
        RemapChannel.One => 1f,
        _ => 0f,
    };
}
