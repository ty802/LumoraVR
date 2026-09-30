// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// What a texture shows past its edges: itself again, or its edge pixels.
public enum TextureWrap : byte
{
    Repeat,
    Clamp,
}
