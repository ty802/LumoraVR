// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// The transfer curves a distortion drives into. SoftClip is tanh; HardClip squares off at full scale; Tube is a
// tanh pushed off centre, so one side flattens before the other and even harmonics come out; Foldback reflects
// anything past full scale back inward, again and again as the drive goes up.
public enum DistortionCurve
{
    SoftClip,
    HardClip,
    Tube,
    Foldback,
}
