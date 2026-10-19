// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// One wire to draw, in the wire mesh's own space: from an output, leaving along +X, into an input,
// arriving along +X. A straight one runs point to point, the way a cut is drawn. Width scales the tube, for
// the wire a pointer is on.
public readonly record struct ScriptWireCurve(float3 From, float3 To, color FromColor, color ToColor, bool Straight = false,
    float Start = 0f, float End = 1f, bool Pulse = false, float Width = 1f);
