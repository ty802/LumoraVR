// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Shading;

// Where a piece of shader code runs: once per vertex, once per pixel, or either.
[Flags]
public enum ShaderStage : byte
{
    Vertex = 1,
    Fragment = 2,
    Any = Vertex | Fragment,
}
