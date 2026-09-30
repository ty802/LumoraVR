// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// A value in shader code. Code is always something that can sit next to any operator as it is: a name,
// a call, a constructor, a swizzle of one of those, or a literal (a negative one in brackets).
public readonly record struct GlslValue(string Code, ShaderType Type)
{
    public override string ToString() => $"{Code} : {Type}";
}
