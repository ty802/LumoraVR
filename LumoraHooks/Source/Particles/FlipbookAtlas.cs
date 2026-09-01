// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Godot.Hooks.Particles;

// Where two consecutive sheet frames live, and how much of the second one to mix in.
public readonly struct FlipbookBlend
{
    // Scale applied to the mesh UV before either offset. Same for both tiles.
    public readonly float2 TileSize;

    public readonly float2 OffsetA;
    public readonly float2 OffsetB;

    // 0 shows A alone, 1 shows B alone.
    public readonly float Blend;

    public FlipbookBlend(in float2 tileSize, in float2 offsetA, in float2 offsetB, float blend)
    {
        TileSize = tileSize;
        OffsetA = offsetA;
        OffsetB = offsetB;
        Blend = blend;
    }
}

// Sheet addressing, kept on this side as well as in the shader.
//
// The shader is what actually samples, so this duplicates it - deliberately, because it is the only
// way any of it gets tested, and because the wrap at the last frame and the row-major layout are the
// two things that go wrong silently. If one changes the other has to. -xlinka
public static class FlipbookAtlas
{
    // Custom data rides a MultiMesh instance buffer, and how that buffer reaches the GPU is the
    // renderer's business, not ours - some paths hand it over as normalised bytes. Sending frame/count
    // instead of the raw frame keeps the value inside 0..1 wherever it lands, and the shader has the
    // count already so unpacking is free. -xlinka
    public static float Pack(float frame, int frameCount)
    {
        int count = System.Math.Max(1, frameCount);
        if (count == 1)
            return 0f;
        float wrapped = frame % count;
        if (wrapped < 0f)
            wrapped += count;
        return wrapped / count;
    }

    public static float Unpack(float packed, int frameCount)
    {
        int count = System.Math.Max(1, frameCount);
        return System.Math.Clamp(packed, 0f, 1f) * count;
    }

    public static FlipbookBlend Resolve(float frame, int columns, int rows, int frameCount)
    {
        int cols = System.Math.Max(1, columns);
        int rowCount = System.Math.Max(1, rows);
        int count = System.Math.Clamp(frameCount, 1, cols * rowCount);

        var tile = new float2(1f / cols, 1f / rowCount);

        float wrapped = frame % count;
        if (wrapped < 0f)
            wrapped += count;

        int a = (int)wrapped;
        if (a >= count)
            a = count - 1;
        float blend = wrapped - a;
        int b = (a + 1) % count;

        return new FlipbookBlend(tile, Offset(a, cols, tile), Offset(b, cols, tile), blend);
    }

    // Row major, first row at the top: Godot's V runs down the image, so the row index scales straight
    // into the offset with no flip.
    private static float2 Offset(int frame, int columns, in float2 tile)
        => new float2((frame % columns) * tile.x, (frame / columns) * tile.y);

    public static float2 Sample(in FlipbookBlend blend, in float2 uv, bool second)
    {
        var offset = second ? blend.OffsetB : blend.OffsetA;
        return new float2(uv.x * blend.TileSize.x + offset.x, uv.y * blend.TileSize.y + offset.y);
    }
}
