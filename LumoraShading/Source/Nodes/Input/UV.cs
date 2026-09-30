// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Shading;

// The mesh's texture coordinates. V is 0 at the top of a texture.
[Node("Input/UV")]
public sealed class UV : ShaderNode
{
    [return: Port("uv")]
    public static float2 Run(ShaderUV channel = ShaderUV.UV1) => throw GpuOnly;

    public override void Emit(ShaderNodeContext context)
        => context.Output("uv", context.Option<ShaderUV>("channel") == ShaderUV.UV2 ? "UV2" : "UV");
}
