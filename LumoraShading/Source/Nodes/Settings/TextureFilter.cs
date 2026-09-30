// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// How a texture is read between its pixels: smoothly, or the nearest one for crisp pixel art.
public enum TextureFilter : byte
{
    Linear,
    Nearest,
}
