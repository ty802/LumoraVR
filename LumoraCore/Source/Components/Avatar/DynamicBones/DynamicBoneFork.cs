// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// What a swinging chain root with more than one child turns toward. Hold: nothing, it keeps the pose the skeleton
// gives it. First: its first child. Average: the middle of all its children.
public enum DynamicBoneFork
{
    Hold,
    First,
    Average,
}
