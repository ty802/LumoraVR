// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Shading;

// The shader code a Run overload stands for. Ports are written {name}. Code with no semicolon is an
// expression and becomes the one output; code with semicolons is statements that set every output.
// Needs names the GlslLibrary functions the code calls; Stages says where it is allowed to run.
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GlslAttribute : Attribute
{
    public string Code { get; }
    public string[] Needs { get; init; } = Array.Empty<string>();
    public ShaderStage Stages { get; init; } = ShaderStage.Any;

    public GlslAttribute(string code)
    {
        Code = code;
    }
}
