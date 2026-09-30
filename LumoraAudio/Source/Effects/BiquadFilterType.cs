// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// The RBJ cookbook shapes. Gain only means something to Peaking and the two shelves; BandPass peaks at 0 dB.
public enum BiquadFilterType
{
    LowPass,
    HighPass,
    BandPass,
    Notch,
    Peaking,
    LowShelf,
    HighShelf,
    AllPass,
}
