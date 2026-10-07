// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// How a picture is written to bytes. WebP is small at a quality that looks the same; PNG loses nothing.
public enum ImageEncoding
{
    Webp,
    Png,
    Jpg,
}

public static class ImageEncodings
{
    public static string Extension(this ImageEncoding encoding) => encoding switch
    {
        ImageEncoding.Png => ".png",
        ImageEncoding.Jpg => ".jpg",
        _ => ".webp",
    };
}
