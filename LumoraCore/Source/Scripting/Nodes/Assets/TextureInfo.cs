// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// What a texture is on this machine: its size in pixels, how many mip levels of pixels it keeps here,
// whether it holds values past 1 (a half-float picture), and how many frames a strip stacks down it.
// Readable says whether Get Texture Pixel and Sample Texture UV have pixels to read: a camera's or a
// video's picture lives on the graphics card alone. Nothing while it loads. The size can differ between
// machines, each loading the resolution its settings allow. format is how the picture is stored: as the
// graphics card holds it once the renderer has said, else as it was decoded, and unknown for a camera's
// or a video's picture.
[Node("Assets/Texture Info", Capability.WorldRead)]
public sealed class TextureInfo : ReadNode
{
    [return: Port("width")]
    public static int Run(ScriptContext ctx, IAssetProvider<TextureAsset>? texture, out int height, out int mips, out bool hdr,
        out int frames, out bool readable, out TextureFormatKind format)
    {
        height = 0;
        mips = 0;
        hdr = false;
        frames = 0;
        readable = false;
        format = TextureFormatKind.Unknown;
        switch (ScriptAssets.Texture(ctx, texture))
        {
            case RenderTexture render:
                height = render.RenderHeight;
                frames = 1;
                return render.RenderWidth;
            case VideoTexture video:
                height = video.VideoHeight;
                frames = video.VideoWidth > 0 ? 1 : 0;
                return video.VideoWidth;
            case { } picture when ScriptAssets.TryLevel(picture, 0, out var level):
                height = level.Height;
                mips = ScriptAssets.LevelCount(picture);
                hdr = level.Half;
                frames = System.Math.Max(1, picture.AnimationFrames);
                readable = true;
                format = picture.Metadata?.GpuFormat is { } stored and not TextureFormatKind.Unknown ? stored
                    : picture.Metadata?.ContentFormat is { } content and not TextureFormatKind.Unknown ? content
                    : picture.PixelFormat;
                return level.Width;
            default:
                return 0;
        }
    }
}
