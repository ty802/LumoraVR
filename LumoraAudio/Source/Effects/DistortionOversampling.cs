// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// The rate a distortion's curve runs at, as a multiple of the mixer's. Harmonics a curve makes above half the rate
// it runs at fold back down as inharmonic noise; running it faster leaves them room to be filtered out before the
// rate comes back down. None keeps the fold-back, which is its own lo-fi sound.
public enum DistortionOversampling
{
    None,
    Two,
    Four,
}
