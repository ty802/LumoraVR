// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// Stands in for a texture in a node signature. The texture handle belongs to LumoraCore, which this
// assembly cannot see, so ShaderNodes maps this type onto the "texture" script type and a port declared
// with it takes whatever that type takes. Never made: shader nodes do not run on the CPU. -xlinka
public sealed class ShaderTexture
{
    private ShaderTexture()
    {
    }
}
