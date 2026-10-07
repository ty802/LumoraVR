// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Turns directions into cube-face pixels and back. Every cubemap producer goes through here so the
// face convention is written down exactly once.
//
// The mapping is the ordinary graphics-API cube layout: for a face pixel at (s, t) in 0..1 with t
// counted from the TOP row, the sampled direction is built from sc = 2s - 1 and tc = 2t - 1 by the
// per-face table in FaceDirection. Get this wrong and the symptom is subtle - a sky
// that looks fine until you notice the horizon seams do not line up, or text in a panorama reading
// backwards - so the table is stated rather than derived at each call site. -xlinka
public static class CubemapProjector
{
    // Not normalized on purpose: the callers that need a unit vector normalize once, and the ones that only
    // need a ray do not pay for a square root per pixel.
    public static float3 FaceDirection(CubemapFace face, float s, float t)
    {
        float sc = 2f * s - 1f;
        float tc = 2f * t - 1f;
        return face switch
        {
            CubemapFace.PositiveX => new float3(1f, -tc, -sc),
            CubemapFace.NegativeX => new float3(-1f, -tc, sc),
            CubemapFace.PositiveY => new float3(sc, 1f, tc),
            CubemapFace.NegativeY => new float3(sc, -1f, -tc),
            CubemapFace.PositiveZ => new float3(sc, -tc, 1f),
            _ => new float3(-sc, -tc, -1f),
        };
    }

    // The direction handed to the generator is normalized. Faces run in parallel because they are independent
    // writes into separate buffers, and a 1024-edge cube is six million generator calls.
    public static byte[][] Generate(int faceSize, Func<CubemapFace, float3, color> generator)
    {
        if (faceSize < CubemapAsset.MinFaceSize)
            throw new ArgumentOutOfRangeException(nameof(faceSize));

        var faces = new byte[6][];
        Parallel.For(0, 6, faceIndex =>
        {
            var face = (CubemapFace)faceIndex;
            var pixels = new byte[faceSize * faceSize * 4];
            float inv = 1f / faceSize;

            for (int y = 0; y < faceSize; y++)
            {
                float t = (y + 0.5f) * inv;
                int row = y * faceSize * 4;
                for (int x = 0; x < faceSize; x++)
                {
                    float s = (x + 0.5f) * inv;
                    var dir = Normalize(FaceDirection(face, s, t));
                    WriteColor(pixels, row + x * 4, generator(face, dir));
                }
            }

            faces[faceIndex] = pixels;
        });
        return faces;
    }

    // Bilinear, wrapping horizontally and clamping vertically, which is what the projection actually is:
    // longitude wraps, latitude does not.
    public static byte[][] ProjectPanorama(byte[] panorama, int width, int height, int faceSize)
    {
        if (panorama == null)
            throw new ArgumentNullException(nameof(panorama));
        if (width <= 0 || height <= 0 || panorama.Length < (long)width * height * 4)
            throw new ArgumentException("Panorama buffer is smaller than its declared size", nameof(panorama));

        return Generate(faceSize, (_, dir) => SamplePanorama(panorama, width, height, dir));
    }

    // A quarter of the panorama width is the size at which a face has roughly the source's angular detail
    // across its 90 degrees, so auto lands there and then snaps down to a power of two. Both the explicit and
    // the auto answer are clamped, because six faces is six allocations and a face size typed into a field
    // should not be able to ask for gigabytes.
    public static int ChooseFaceSize(int requested, int panoramaWidth)
    {
        int size = requested > 0 ? requested : NearestPowerOfTwoAtOrBelow(System.Math.Max(1, panoramaWidth / 4));
        return System.Math.Clamp(size, CubemapAsset.MinFaceSize, CubemapAsset.MaxFaceSize);
    }

    // The face size that holds all of a panorama's detail: a quarter of its width, rounded up to a power of
    // two. ChooseFaceSize rounds down, so this is only ever a ceiling.
    public static int SourceFaceSize(int panoramaWidth)
    {
        int quarter = System.Math.Max(1, panoramaWidth / 4);
        int size = CubemapAsset.MinFaceSize;
        while (size < quarter && size < CubemapAsset.MaxFaceSize)
            size *= 2;
        return size;
    }

    // ProjectPanorama for half-float RGBA, eight bytes a texel in and out. Values pass through as they
    // are, above 1 included, which is the point of an HDR panorama.
    public static byte[][] ProjectPanoramaHalf(byte[] panorama, int width, int height, int faceSize)
    {
        if (panorama == null)
            throw new ArgumentNullException(nameof(panorama));
        if (width <= 0 || height <= 0 || panorama.Length < (long)width * height * 8)
            throw new ArgumentException("Panorama buffer is smaller than its declared size", nameof(panorama));
        if (faceSize < CubemapAsset.MinFaceSize)
            throw new ArgumentOutOfRangeException(nameof(faceSize));

        var faces = new byte[6][];
        Parallel.For(0, 6, faceIndex =>
        {
            var face = (CubemapFace)faceIndex;
            var pixels = new byte[faceSize * faceSize * 8];
            float inv = 1f / faceSize;
            for (int y = 0; y < faceSize; y++)
            {
                float t = (y + 0.5f) * inv;
                for (int x = 0; x < faceSize; x++)
                {
                    float s = (x + 0.5f) * inv;
                    var dir = Normalize(FaceDirection(face, s, t));
                    SamplePanoramaHalf(panorama, width, height, dir, pixels, (y * faceSize + x) * 8);
                }
            }
            faces[faceIndex] = pixels;
        });
        return faces;
    }

    private static void SamplePanoramaHalf(byte[] pixels, int width, int height, in float3 dir, byte[] target, int at)
    {
        var uv = DirectionToPanoramaUv(dir);
        float fx = uv.x * width - 0.5f;
        float fy = uv.y * height - 0.5f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float tx = fx - x0;
        float ty = fy - y0;
        int x1 = WrapX(x0 + 1, width);
        int y1 = ClampY(y0 + 1, height);
        x0 = WrapX(x0, width);
        y0 = ClampY(y0, height);

        int i00 = (y0 * width + x0) * 8;
        int i10 = (y0 * width + x1) * 8;
        int i01 = (y1 * width + x0) * 8;
        int i11 = (y1 * width + x1) * 8;
        for (int c = 0; c < 8; c += 2)
        {
            float top = ReadHalf(pixels, i00 + c) + (ReadHalf(pixels, i10 + c) - ReadHalf(pixels, i00 + c)) * tx;
            float bottom = ReadHalf(pixels, i01 + c) + (ReadHalf(pixels, i11 + c) - ReadHalf(pixels, i01 + c)) * tx;
            BinaryPrimitives.WriteHalfLittleEndian(target.AsSpan(at + c), (Half)(top + (bottom - top) * ty));
        }
    }

    private static float ReadHalf(byte[] pixels, int at) => (float)BinaryPrimitives.ReadHalfLittleEndian(pixels.AsSpan(at));

    public static float2 DirectionToPanoramaUv(in float3 dir)
    {
        float u = MathF.Atan2(dir.x, -dir.z) / (2f * MathF.PI) + 0.5f;
        float v = MathF.Acos(System.Math.Clamp(dir.y, -1f, 1f)) / MathF.PI;
        return new float2(u, v);
    }

    // The inverse of DirectionToPanoramaUv, unit length: -Z at the centre column, straight up at v = 0.
    public static float3 PanoramaUvToDirection(float u, float v)
    {
        float longitude = (u - 0.5f) * 2f * MathF.PI;
        float polar = v * MathF.PI;
        float ring = MathF.Sin(polar);
        return new float3(MathF.Sin(longitude) * ring, MathF.Cos(polar), -MathF.Cos(longitude) * ring);
    }

    // The inverse of FaceDirection: the face a direction lands on and its (s, t) there, t from the top row.
    public static CubemapFace DirectionToFace(in float3 dir, out float s, out float t)
    {
        float ax = MathF.Abs(dir.x);
        float ay = MathF.Abs(dir.y);
        float az = MathF.Abs(dir.z);
        CubemapFace face;
        float sc, tc, major;
        if (ax >= ay && ax >= az)
        {
            major = ax;
            face = dir.x >= 0f ? CubemapFace.PositiveX : CubemapFace.NegativeX;
            sc = dir.x >= 0f ? -dir.z : dir.z;
            tc = -dir.y;
        }
        else if (ay >= az)
        {
            major = ay;
            face = dir.y >= 0f ? CubemapFace.PositiveY : CubemapFace.NegativeY;
            sc = dir.x;
            tc = dir.y >= 0f ? dir.z : -dir.z;
        }
        else
        {
            major = az;
            face = dir.z >= 0f ? CubemapFace.PositiveZ : CubemapFace.NegativeZ;
            sc = dir.z >= 0f ? dir.x : -dir.x;
            tc = -dir.y;
        }

        if (major <= 1e-8f)
        {
            s = 0.5f;
            t = 0.5f;
            return face;
        }
        s = (sc / major + 1f) * 0.5f;
        t = (tc / major + 1f) * 0.5f;
        return face;
    }

    private static color SamplePanorama(byte[] pixels, int width, int height, in float3 dir)
    {
        var uv = DirectionToPanoramaUv(dir);

        float fx = uv.x * width - 0.5f;
        float fy = uv.y * height - 0.5f;

        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float tx = fx - x0;
        float ty = fy - y0;

        int x1 = WrapX(x0 + 1, width);
        int y1 = ClampY(y0 + 1, height);
        x0 = WrapX(x0, width);
        y0 = ClampY(y0, height);

        int i00 = (y0 * width + x0) * 4;
        int i10 = (y0 * width + x1) * 4;
        int i01 = (y1 * width + x0) * 4;
        int i11 = (y1 * width + x1) * 4;

        return new color(
            Lerp2(pixels[i00], pixels[i10], pixels[i01], pixels[i11], tx, ty),
            Lerp2(pixels[i00 + 1], pixels[i10 + 1], pixels[i01 + 1], pixels[i11 + 1], tx, ty),
            Lerp2(pixels[i00 + 2], pixels[i10 + 2], pixels[i01 + 2], pixels[i11 + 2], tx, ty),
            Lerp2(pixels[i00 + 3], pixels[i10 + 3], pixels[i01 + 3], pixels[i11 + 3], tx, ty));
    }

    private static float Lerp2(byte a, byte b, byte c, byte d, float tx, float ty)
    {
        float top = a + (b - a) * tx;
        float bottom = c + (d - c) * tx;
        return (top + (bottom - top) * ty) * (1f / 255f);
    }

    private static int WrapX(int x, int width)
    {
        x %= width;
        return x < 0 ? x + width : x;
    }

    private static int ClampY(int y, int height) => System.Math.Clamp(y, 0, height - 1);

    private static float3 Normalize(in float3 v)
    {
        float length = MathF.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
        if (length <= 1e-8f)
            return new float3(0f, 1f, 0f);
        float inv = 1f / length;
        return new float3(v.x * inv, v.y * inv, v.z * inv);
    }

    private static int NearestPowerOfTwoAtOrBelow(int value)
    {
        int result = CubemapAsset.MinFaceSize;
        while (result * 2 <= value)
            result *= 2;
        return result;
    }

    // Shared so every generator rounds and clamps the same way.
    public static void WriteColor(byte[] pixels, int index, in color value)
    {
        pixels[index] = ToByte(value.r);
        pixels[index + 1] = ToByte(value.g);
        pixels[index + 2] = ToByte(value.b);
        pixels[index + 3] = ToByte(value.a);
    }

    private static byte ToByte(float value)
    {
        if (value <= 0f) return 0;
        if (value >= 1f) return 255;
        return (byte)(value * 255f + 0.5f);
    }
}
