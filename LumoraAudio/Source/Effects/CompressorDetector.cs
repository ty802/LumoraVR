// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// What a compressor measures: the louder channel's instantaneous peak, or the power of both averaged over
// 10 ms. The two agree on a signal of steady magnitude; on a sine the RMS reads 3 dB under the peak.
public enum CompressorDetector
{
    Peak,
    Rms,
}
