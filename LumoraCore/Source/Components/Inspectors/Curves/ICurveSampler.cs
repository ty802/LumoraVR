// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Evaluates one track's keys exactly the way playback does, one channel at a time.
public interface ICurveSampler
{
    // Carry is the previous sample on the same channel, for rotations to stay continuous across the
    // +-180 seam. NaN starts fresh.
    float Sample(int channel, float time, ref float carry);

    // A full key at this time, every channel filled from the curve as it stands.
    CurveKeyRow RowAt(float time);
}
