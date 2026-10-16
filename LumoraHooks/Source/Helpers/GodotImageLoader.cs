// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Xml;
using Godot;
using Lumora.Core.Assets;

namespace Lumora.Godot.Helpers;

public static class GodotImageLoader
{
    // Godot logs native errors on a format mismatch, even when the caller handles the return
    // value. Choose one reader from the contents instead of probing every reader in turn.
    public static Error Load(Image image, byte[] encoded)
    {
        if (encoded == null || encoded.Length == 0)
            return Error.FileUnrecognized;

        ReadOnlySpan<byte> data = encoded;
        if (data.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return image.LoadPngFromBuffer(encoded);
        if (data.StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
            return image.LoadJpgFromBuffer(encoded);
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            if (WebpStill.TryExtractFirstFrame(encoded, out var still))
                encoded = still;
            return image.LoadWebpFromBuffer(encoded);
        }
        if (data.StartsWith("BM"u8))
            return image.LoadBmpFromBuffer(encoded);
        if (data.StartsWith(new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x31, 0x31, 0xbb, 0x0d, 0x0a, 0x1a, 0x0a })
            || data.StartsWith(new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 0x0d, 0x0a, 0x1a, 0x0a }))
            return image.LoadKtxFromBuffer(encoded);
        if (data.StartsWith("DDS "u8))
            return image.LoadDdsFromBuffer(encoded);
        if (LooksLikeTga(data))
            return image.LoadTgaFromBuffer(encoded);
        if (IsSvg(encoded))
        {
            var error = image.LoadSvgFromBuffer(encoded, 1.0f);
            if (error == Error.Ok)
                FillClearPixels(image);
            return error;
        }

        return Error.FileUnrecognized;
    }

    // The rasteriser leaves clear pixels black, and filtering and mipmaps blend that black into every
    // edge as a dark rim. Each clear pixel takes the colour of the nearest inked one; alpha is untouched. -xlinka
    private static void FillClearPixels(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8 || image.HasMipmaps())
            return;
        int width = image.GetWidth();
        int height = image.GetHeight();
        int count = width * height;
        byte[] data = image.GetData();
        var reached = new bool[count];
        var queue = new int[count];
        int head = 0, tail = 0;
        for (int i = 0; i < count; i++)
        {
            if (data[i * 4 + 3] == 0)
                continue;
            reached[i] = true;
            queue[tail++] = i;
        }
        if (tail == 0 || tail == count)
            return;

        void Reach(int from, int to)
        {
            if (reached[to])
                return;
            reached[to] = true;
            data[to * 4] = data[from * 4];
            data[to * 4 + 1] = data[from * 4 + 1];
            data[to * 4 + 2] = data[from * 4 + 2];
            queue[tail++] = to;
        }

        while (head < tail)
        {
            int i = queue[head++];
            int x = i % width;
            if (x > 0) Reach(i, i - 1);
            if (x < width - 1) Reach(i, i + 1);
            if (i >= width) Reach(i, i - width);
            if (i < count - width) Reach(i, i + width);
        }
        image.SetData(width, height, false, Image.Format.Rgba8, data);
    }

    private static bool LooksLikeTga(ReadOnlySpan<byte> data)
    {
        // Older TGA files have no signature. Check the header rather than requiring the v2 footer.
        if (data.Length < 18 || data[1] > 1
            || (data[12] | data[13]) == 0 || (data[14] | data[15]) == 0
            || data[16] is not (8 or 16 or 24 or 32))
            return false;

        return data[2] switch
        {
            1 or 9 => data[1] == 1 && data[16] == 8 && data[7] == 24,
            2 or 10 => data[1] == 0 && data[16] is 16 or 24 or 32,
            3 or 11 => data[1] == 0 && data[16] is 8 or 16,
            _ => false,
        };
    }

    private static bool IsSvg(byte[] encoded)
    {
        try
        {
            // Read only the root element. This handles a BOM, XML declaration, comments and
            // doctypes without mistaking an HTML error response for an SVG or resolving a DTD.
            using var stream = new MemoryStream(encoded, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            });
            return reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "svg"
                && (reader.NamespaceURI.Length == 0 || reader.NamespaceURI == "http://www.w3.org/2000/svg");
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
