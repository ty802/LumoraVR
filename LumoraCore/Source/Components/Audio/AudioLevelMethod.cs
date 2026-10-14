// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Audio;

// How AudioLevelMeter turns sound into a level: Magnitude is how far the wave swings, Change how fast it
// moves from one sample to the next, which leans towards the high end and ignores a deep hum.
public enum AudioLevelMethod
{
    Magnitude,
    Change,
}
