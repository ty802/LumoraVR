// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Audio;

// How far a voice carries. Declared in order of reach, which is what makes a permission cap a plain
// comparison: anything above the cap is clamped down to it, and Mute is the floor everything falls to.
// -xlinka
public enum VoiceMode
{
    Mute,
    Whisper,
    Normal,
    Shout,
    Broadcast,
}
