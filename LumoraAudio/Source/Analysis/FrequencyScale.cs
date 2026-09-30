// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// How a spectrum is cut into bands. Linear gives every band the same width in hertz; Logarithmic the same
// width in octaves, the way pitch is heard; Mel overlapping triangles on the mel scale, the way loudness of
// a band is heard. Octave and ThirdOctave are the standard bands (1 kHz centred, base ten), as many as fit
// the range, whatever band count is asked for.
public enum FrequencyScale
{
    Linear,
    Logarithmic,
    Mel,
    Octave,
    ThirdOctave,
}
