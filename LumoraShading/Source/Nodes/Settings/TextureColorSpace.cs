// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// What a texture holds: a picture (decoded from sRGB), plain numbers, or a normal map.
public enum TextureColorSpace : byte
{
    Color,
    Data,
    Normal,
}
