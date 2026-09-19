// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Text;
using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Helpers;

// Bytes for a res:// path out of the resource pack. A raw file comes back as it is. An imported
// resource comes back as the file it came from, or as a lossless encoding the engine's decoders
// already read, so the asset that asked cannot tell which one fed it. The exporter never stores the
// source of an imported file, only what the importer produced, which is why the second form exists.
// -xlinka
public static class ResourcePackReader
{
    public static byte[]? Read(string resPath)
    {
        if (string.IsNullOrEmpty(resPath))
            return null;

        if (FileAccess.FileExists(resPath))
            return NonEmpty(FileAccess.GetFileAsBytes(resPath));

        if (!ResourceLoader.Exists(resPath))
            return null;

        Resource? resource;
        try
        {
            resource = ResourceLoader.Load(resPath);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"ResourcePackReader: '{resPath}' failed to load: {ex.Message}");
            return null;
        }
        if (resource == null)
            return null;

        switch (resource)
        {
            case Texture2D texture:
                return EncodeTexture(texture, resPath);
            case FontFile font:
                return NonEmpty(font.Data);
            case Shader shader:
                return NonEmpty(Encoding.UTF8.GetBytes(shader.Code ?? string.Empty));
            case Json json:
                return NonEmpty(Encoding.UTF8.GetBytes(Json.Stringify(json.Data)));
            case AudioStreamMP3 mp3:
                return NonEmpty(mp3.Data);
            default:
                LumoraLogger.Warn($"ResourcePackReader: '{resPath}' is a {resource.GetType().Name}, which has no byte form the asset pipeline reads");
                return null;
        }
    }

    // The importer already rasterised or decoded the picture, so PNG hands the same texels on with
    // no second lossy step; float data goes out as EXR for the same reason. Block-compressed
    // formats are decompressed first because neither encoder takes them. -xlinka
    private static byte[]? EncodeTexture(Texture2D texture, string resPath)
    {
        using var image = texture.GetImage();
        if (image == null || image.IsEmpty())
            return null;
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
        {
            LumoraLogger.Warn($"ResourcePackReader: '{resPath}' is compressed in a format this build cannot decompress");
            return null;
        }

        var format = image.GetFormat();
        bool floatData = format is Image.Format.Rgbah or Image.Format.Rgbaf or Image.Format.Rgbh or Image.Format.Rgbf
            or Image.Format.Rgh or Image.Format.Rgf or Image.Format.Rh or Image.Format.Rf or Image.Format.Rgbe9995;
        if (floatData)
        {
            if (format != Image.Format.Rgbaf)
                image.Convert(Image.Format.Rgbaf);
            return NonEmpty(image.SaveExrToBuffer());
        }

        if (format != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);
        return NonEmpty(image.SavePngToBuffer());
    }

    private static byte[]? NonEmpty(byte[]? bytes) => bytes is { Length: > 0 } ? bytes : null;
}
