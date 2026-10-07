// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// A texture's pixels as this machine decoded them: mip levels from the full size down, rows from the
// top, RGBA8 or RGBA half floats. Never changed once made, so a reader on any thread can keep one as
// long as it likes. Version is the texture's version these pixels belong to; a newer delivery of the
// same texture makes it stale.
public sealed class TexturePixels
{
    public TexturePixels(byte[][] levels, int width, int height, TextureFormatKind format, int version)
    {
        Levels = levels;
        Width = width;
        Height = height;
        Format = format;
        Version = version;
        long total = 0;
        foreach (var level in levels)
            total += level?.LongLength ?? 0;
        Bytes = total;
    }

    public byte[][] Levels { get; }

    public int Width { get; }

    public int Height { get; }

    public TextureFormatKind Format { get; }

    // Eight bytes a texel, half floats, instead of four bytes.
    public bool IsHalf => Format == TextureFormatKind.RGBA16F;

    public int Version { get; }

    public long Bytes { get; }
}
