// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Shading;

// What an input reads when nothing is wired to it and no value is set, when that is not a constant: the
// mesh's UV, the surface normal, or a default a C# signature cannot spell, like a white color.
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class GlslDefaultAttribute : Attribute
{
    public string Code { get; }

    public GlslDefaultAttribute(string code)
    {
        Code = code;
    }
}
