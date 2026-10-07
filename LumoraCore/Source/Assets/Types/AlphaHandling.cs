// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// What a material that has no full blend mode does with alpha: ignore it, cut at a threshold, or blend.
// The slice and displace families carry this instead of BlendMode, under the same name and values a package
// stores, so an imported one keeps its setting.
public enum AlphaHandling
{
    Opaque,
    AlphaClip,
    AlphaBlend
}
