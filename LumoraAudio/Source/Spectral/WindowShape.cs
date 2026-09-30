// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// Taper applied to a frame before it is transformed. Narrower main lobes go with higher side lobes: Hann is
// the everyday choice, Blackman-Harris for a spectrum that must not leak, flat-top for reading a sine's
// level straight off its peak, Kaiser for a side lobe set by its beta.
public enum WindowShape
{
    Rectangular,
    Hann,
    Hamming,
    Blackman,
    BlackmanHarris,
    FlatTop,
    Kaiser,
}
