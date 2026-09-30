// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// Which faces are drawn: the front ones, the back ones, or both.
public enum ShaderCull : byte
{
    Back,
    Front,
    Both,
}
