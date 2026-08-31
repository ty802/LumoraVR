// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// The one headline colour of a thing, for anything that has one: a material's albedo or tint, a
// light's colour, the vertex colour on a label. This is what the eyedropper reads off whatever the
// laser is pointing at.
//
// "Primary" is deliberately narrow, and false is a real answer. Something with no single visible
// colour - a gradient sitting in a hue mode, a blur that renders whatever is behind it, a property
// block that only overrides one channel of somebody else's material - returns false and lets the
// sampler fall through to the next candidate, rather than picking one of its fields at random and
// handing back a colour that is nowhere on screen. -xlinka
public interface IPrimaryColorSource
{
    bool TryGetPrimaryColor(out colorHDR color);
}
