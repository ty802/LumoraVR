// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Standard mouth-shape phoneme groups used to drive lip-sync blendshapes. Matches the common
/// 15-viseme set (plus a Laughter overlay) so blendshape name-matching lines up with typical
/// avatar rigs (Silence, PP, FF, ...).
/// </summary>
public enum Viseme
{
    Silence,
    PP,
    FF,
    TH,
    DD,
    KK,
    CH,
    SS,
    NN,
    RR,
    AA,
    E,
    IH,
    OH,
    OU,
    Laughter,
    COUNT
}
