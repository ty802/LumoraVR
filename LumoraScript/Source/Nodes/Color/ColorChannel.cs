// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// The part of a color the channel nodes change. RGB is red, green and blue together; hue, saturation
// and brightness are the HSV ones.
public enum ColorChannel : byte
{
    Red,
    Green,
    Blue,
    Alpha,
    RGB,
    Hue,
    Saturation,
    Brightness
}
