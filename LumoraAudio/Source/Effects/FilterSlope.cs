// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// Decibels per octave past the corner of a low-pass or high-pass: a Butterworth cascade of one, two or four
// second-order sections. Band-pass, notch and all-pass stack that many identical sections; peaking and the
// shelves are always one.
public enum FilterSlope
{
    Slope12,
    Slope24,
    Slope48,
}
