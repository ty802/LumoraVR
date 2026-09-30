// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// The shader sandbox's allowances, kept here too so a graph that would be refused says why on the node
// that did it instead of the material going blank. The sandbox itself still checks every source on every
// machine; these must never be looser than it is.
public static class ShaderLimits
{
    public const int MaxNodes = 512;
    public const int MaxSourceBytes = 64 * 1024;
    public const int MaxUniforms = 64;
    public const int MaxTextureSamples = 32;
    public const int MaxLoops = 8;
}
