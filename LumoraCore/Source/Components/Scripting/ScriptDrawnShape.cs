// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// One piece of what the drawing layer shows, in world space: a line of Radius from A to B, or a filled
// triangle A B C.
public readonly record struct ScriptDrawnShape(bool Line, float3 A, float3 B, float3 C, color Color, float Radius)
{
    public static ScriptDrawnShape Segment(float3 a, float3 b, color color, float radius) => new(true, a, b, default, color, radius);

    public static ScriptDrawnShape Triangle(float3 a, float3 b, float3 c, color color) => new(false, a, b, c, color, 0f);
}
