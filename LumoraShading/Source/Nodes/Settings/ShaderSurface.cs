// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// How the material covers what is behind it. Cutout drops pixels whose alpha is under the clip value;
// the see-through kinds blend and draw after everything solid.
public enum ShaderSurface : byte
{
    Opaque,
    Cutout,
    Transparent,
    Additive,
    Multiply,
}
