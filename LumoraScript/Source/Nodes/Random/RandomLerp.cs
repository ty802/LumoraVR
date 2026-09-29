// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// A random point on the straight line from a to b, every point of it as likely: one draw shared by all
// lanes, where Random Vector draws each lane on its own.
[Node("Math/Random/Random Lerp")]
public sealed class RandomLerp : ReadNode
{
    public static float2 Run(ScriptContextBase ctx, float2 a, float2 b) => a + (b - a) * ctx.Random.NextSingle();
    public static float3 Run(ScriptContextBase ctx, float3 a, float3 b) => a + (b - a) * ctx.Random.NextSingle();
    public static float4 Run(ScriptContextBase ctx, float4 a, float4 b) => a + (b - a) * ctx.Random.NextSingle();
}
