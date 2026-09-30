// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// How Mix Color puts the second color over the first.
public enum MixMode : byte
{
    Mix,
    Darken,
    Multiply,
    Burn,
    Lighten,
    Screen,
    Dodge,
    Add,
    Overlay,
    SoftLight,
    LinearLight,
    Difference,
    Exclusion,
    Subtract,
    Divide,
}
