// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// What a value in shader code is. Color is a vec4 like Float4, kept apart so a color landing on a single
// number reads as its brightness instead of its red channel.
public enum ShaderType : byte
{
    Bool,
    Int,
    Float,
    Float2,
    Float3,
    Float4,
    Color,
    Texture,
}
