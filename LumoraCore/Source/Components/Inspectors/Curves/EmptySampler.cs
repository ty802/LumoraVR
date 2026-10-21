// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

public sealed class EmptySampler : ICurveSampler
{
    public static readonly EmptySampler Instance = new();

    public float Sample(int channel, float time, ref float carry) => 0f;

    public CurveKeyRow RowAt(float time) => new(0) { Time = time };
}
