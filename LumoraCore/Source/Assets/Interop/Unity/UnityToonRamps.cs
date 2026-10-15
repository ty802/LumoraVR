// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Lumora.Core.Persistence;
using Lumora.Core.Photos;

namespace Lumora.Core.Assets.Interop.Unity;

// The SDK toon material's shading ramps. They ship with the SDK, so a package names them by guid and never
// carries them, and a material without one reads the shader's white default: all light, the same as the
// flat ramp. Each is drawn again from its measured shape: U is the wrapped N.L, the banded ones step at
// eighths, the realistic ones rise out of black at or before the terminator. Their levels are linear; the
// PNG holds them sRGB-encoded because our ramp slot decodes what it reads. -xlinka
internal static class UnityToonRamps
{
    public const string Flat = "8ed41581528c4fa4fa11970aca4edb8d";

    private const int Width = 256;
    private const int Rows = 2;

    private static readonly float[] Realistic =
    {
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.04f, 0.21f,
        0.43f, 0.53f, 0.57f, 0.62f, 0.66f, 0.70f, 0.74f, 0.77f, 0.81f, 0.85f, 0.88f, 0.91f, 0.94f, 0.96f, 0.98f, 1f,
    };

    private static readonly float[] RealisticSoft =
    {
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.01f, 0.04f, 0.08f, 0.12f, 0.17f, 0.22f, 0.28f, 0.34f, 0.40f,
        0.46f, 0.52f, 0.56f, 0.60f, 0.65f, 0.69f, 0.73f, 0.77f, 0.80f, 0.84f, 0.87f, 0.91f, 0.93f, 0.96f, 0.98f, 1f,
    };

    private static readonly float[] RealisticVerySoft =
    {
        0f, 0f, 0f, 0f, 0f, 0.02f, 0.04f, 0.07f, 0.09f, 0.13f, 0.16f, 0.20f, 0.24f, 0.28f, 0.32f, 0.36f, 0.41f,
        0.45f, 0.50f, 0.54f, 0.59f, 0.63f, 0.67f, 0.72f, 0.76f, 0.80f, 0.83f, 0.87f, 0.90f, 0.93f, 0.96f, 0.98f, 1f,
    };

    private static readonly Dictionary<string, Func<float, float>> Shapes = new(StringComparer.OrdinalIgnoreCase)
    {
        [Flat] = _ => 1f,
        ["348500adef1d2da428abc7b720b8b699"] = u => Curve(Realistic, u),
        ["636cf1b5dfca6f54b94ca3d2ff8216c9"] = u => Curve(RealisticSoft, u),
        ["5f304bf7a07313d43b8562d9eabce646"] = u => Curve(RealisticVerySoft, u),
        ["dfafc89321615114fb6dbecdba0c8214"] = u => Bands(u, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f),
        ["5d1b50be612cf1248b6e101f8d1c5b53"] = u => Bands(u, 0f, 0f, 0f, 0.5f, 0.5f, 1f, 1f, 1f),
        ["0d6a7a9ec31ab7448a777f0e2daef4af"] = u => Bands(u, 0f, 0f, 0.25f, 0.25f, 0.75f, 0.75f, 1f, 1f),
    };

    private static readonly ConditionalWeakTable<LocalDB, Dictionary<string, Uri>> _made = new();
    private static readonly Dictionary<string, Uri> _madeLoose = new(StringComparer.OrdinalIgnoreCase);

    public static bool Known(string? guid) => guid != null && Shapes.ContainsKey(guid);

    public static async Task<Uri?> ImportAsync(string guid, LocalDB? localDB)
    {
        if (!Shapes.TryGetValue(guid, out var shape))
            return null;
        var made = localDB != null ? _made.GetValue(localDB, _ => new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase)) : _madeLoose;
        lock (made)
            if (made.TryGetValue(guid, out var existing))
                return existing;

        var rgba = new byte[Width * Rows * 4];
        for (int x = 0; x < Width; x++)
        {
            byte level = (byte)MathF.Round(Encode(shape((x + 0.5f) / Width)) * 255f);
            for (int y = 0; y < Rows; y++)
            {
                int at = (y * Width + x) * 4;
                rgba[at] = rgba[at + 1] = rgba[at + 2] = level;
                rgba[at + 3] = 255;
            }
        }
        var png = PhotoEncoder.Png(rgba, Width, Rows);
        Uri? uri;
        if (localDB != null)
        {
            var local = await localDB.SaveAssetAsync(png, ".png").ConfigureAwait(false);
            uri = string.IsNullOrEmpty(local) ? null : new Uri(local);
        }
        else
        {
            string path = Path.Combine(PathResolver.TempPath, "toon-ramp-" + guid + ".png");
            await File.WriteAllBytesAsync(path, png).ConfigureAwait(false);
            uri = new Uri(path);
        }
        if (uri != null)
            lock (made)
                made[guid] = uri;
        return uri;
    }

    // The originals are eight texels read without filtering.
    private static float Bands(float u, params float[] levels) => levels[System.Math.Clamp((int)(u * levels.Length), 0, levels.Length - 1)];

    private static float Curve(float[] points, float u)
    {
        float at = System.Math.Clamp(u, 0f, 1f) * (points.Length - 1);
        int i = System.Math.Min((int)at, points.Length - 2);
        return points[i] + (points[i + 1] - points[i]) * (at - i);
    }

    private static float Encode(float linear)
    {
        linear = System.Math.Clamp(linear, 0f, 1f);
        return linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }
}
