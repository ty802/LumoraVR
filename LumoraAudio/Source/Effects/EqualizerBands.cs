// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Runtime.CompilerServices;

namespace Lumora.Audio;

// The bands of an equalizer held inline, so a whole EQ stays one plain value with no array behind it.
[InlineArray(EqualizerSettings.MaxBands)]
public struct EqualizerBands
{
    private EqualizerBandSettings _band;
}
