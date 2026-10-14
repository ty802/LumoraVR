// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Audio;

// How AudioCuePlayer.PickClip chooses among its clips.
public enum AudioCueOrder
{
    // Any clip, each time.
    Random,
    // Any clip but the one picked last.
    RandomNoRepeat,
    // One after another, wrapping at the end.
    InOrder,
    // The clip at the index given, wrapped into the list.
    ByIndex,
}
