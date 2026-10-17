// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lumora.Core.Photos;

// Rig frame: forward is local -Z, up +Y, right +X (right-handed, Y up).
//
// Face order and basis in the rig's local frame, right = forward x up:
//   0 front  forward -Z  up +Y  right +X
//   1 right  forward +X  up +Y  right +Z
//   2 back   forward +Z  up +Y  right -X
//   3 left   forward -X  up +Y  right -Z
//   4 up     forward +Y  up +Z  right +X  (image top points at the rig's back)
//   5 down   forward -Y  up -Z  right +X  (image top points at the rig's front)
//
// Face i is rendered by a square 90 degree camera that looks down its own -Z with +Y up and
// +X right, with world rotation rigRotation * FaceRotation(i). FaceRotation maps camera -Z onto
// the face forward and camera +Y onto the face up. floatQ.LookRotation cannot build it: it aims
// +Z and returns the inverse rotation.
//
// Face pixels are RGBA8 with row 0 at the top. Pixel (px, py) of an N square face sees
//   a = (px + 0.5) / N * 2 - 1
//   b = 1 - (py + 0.5) / N * 2
//   dir = forward + a * right + b * up
// so image x grows toward camera +X and row 0 is camera +Y. Readbacks that arrive bottom-up
// must be flipped before stitching.
//
// Equirect is W x W/2, row 0 straight up. Column x is longitude ((x + 0.5) / W - 0.5) * 360
// degrees: 0 at the centre column along rig forward, +90 three quarters across looking right
// (+X), +-180 at the edges looking back. Row y is latitude 90 - (y + 0.5) / H * 180.
//   dir = (cos lat * sin lon, sin lat, -cos lat * cos lon) -xlinka
public static class PhotoStitch
{
    public readonly record struct FaceView(float ForwardX, float ForwardY, float ForwardZ, float UpX, float UpY, float UpZ)
    {
        public float RightX => ForwardY * UpZ - ForwardZ * UpY;
        public float RightY => ForwardZ * UpX - ForwardX * UpZ;
        public float RightZ => ForwardX * UpY - ForwardY * UpX;
    }

    public const int FaceCount = 6;

    static readonly FaceView[] Faces =
    {
        new(0f, 0f, -1f, 0f, 1f, 0f),
        new(1f, 0f, 0f, 0f, 1f, 0f),
        new(0f, 0f, 1f, 0f, 1f, 0f),
        new(-1f, 0f, 0f, 0f, 1f, 0f),
        new(0f, 1f, 0f, 0f, 0f, 1f),
        new(0f, -1f, 0f, 0f, 0f, -1f),
    };

    // Right and up per face, flattened so the per-pixel loop skips the cross products.
    static readonly float[] Basis = BuildBasis();

    static float[] BuildBasis()
    {
        var basis = new float[FaceCount * 6];
        for (int i = 0; i < FaceCount; i++)
        {
            var f = Faces[i];
            int o = i * 6;
            basis[o] = f.RightX;
            basis[o + 1] = f.RightY;
            basis[o + 2] = f.RightZ;
            basis[o + 3] = f.UpX;
            basis[o + 4] = f.UpY;
            basis[o + 5] = f.UpZ;
        }
        return basis;
    }

    public static FaceView Face(int index)
    {
        if ((uint)index >= FaceCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Face index must be 0 to {FaceCount - 1}.");
        return Faces[index];
    }

    // Hamilton quaternion (X, Y, Z, W) in rig space for a camera that looks down its own -Z.
    public static (float X, float Y, float Z, float W) FaceRotation(int index)
    {
        var f = Face(index);
        float m00 = f.RightX, m01 = f.UpX, m02 = -f.ForwardX;
        float m10 = f.RightY, m11 = f.UpY, m12 = -f.ForwardY;
        float m20 = f.RightZ, m21 = f.UpZ, m22 = -f.ForwardZ;

        float x, y, z, w;
        float trace = m00 + m11 + m22;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            w = 0.25f * s;
            x = (m21 - m12) / s;
            y = (m02 - m20) / s;
            z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            w = (m21 - m12) / s;
            x = 0.25f * s;
            y = (m01 + m10) / s;
            z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s;
            y = 0.25f * s;
            z = (m12 + m21) / s;
        }
        else
        {
            float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s;
            y = (m12 + m21) / s;
            z = 0.25f * s;
        }

        if (w < 0f)
            return (-x, -y, -z, -w);
        return (x, y, z, w);
    }

    public static byte[] Equirect(IReadOnlyList<byte[]> faces, int faceSize, int width)
    {
        if (faces == null)
            throw new ArgumentException("Faces list is null.", nameof(faces));
        if (faces.Count != FaceCount)
            throw new ArgumentException($"Expected {FaceCount} faces, got {faces.Count}.", nameof(faces));
        if (faceSize < 1)
            throw new ArgumentException($"Face size must be at least 1, got {faceSize}.", nameof(faceSize));
        if (width < 2 || (width & 1) != 0)
            throw new ArgumentException($"Width must be even and at least 2, got {width}.", nameof(width));

        long faceBytes = (long)faceSize * faceSize * 4;
        var src = new byte[FaceCount][];
        for (int i = 0; i < FaceCount; i++)
        {
            var face = faces[i];
            if (face == null)
                throw new ArgumentException($"Face {i} is null.", nameof(faces));
            if (face.LongLength != faceBytes)
                throw new ArgumentException($"Face {i} is {face.LongLength} bytes, expected {faceBytes} for {faceSize}x{faceSize} RGBA8.", nameof(faces));
            src[i] = face;
        }

        int height = width / 2;
        long outBytes = (long)width * height * 4;
        if (outBytes > Array.MaxLength)
            throw new ArgumentException($"Width {width} needs {outBytes} bytes, over the array limit.", nameof(width));

        var sinLon = new float[width];
        var cosLon = new float[width];
        for (int x = 0; x < width; x++)
        {
            double lon = ((x + 0.5) / width - 0.5) * 2.0 * System.Math.PI;
            sinLon[x] = (float)System.Math.Sin(lon);
            cosLon[x] = (float)System.Math.Cos(lon);
        }

        var output = new byte[outBytes];
        Parallel.For(0, height, y => StitchRow(src, faceSize, sinLon, cosLon, output, width, height, y));
        return output;
    }

    static void StitchRow(byte[][] src, int n, float[] sinLon, float[] cosLon, byte[] output, int width, int height, int y)
    {
        double lat = (0.5 - (y + 0.5) / height) * System.Math.PI;
        float cosLat = (float)System.Math.Cos(lat);
        float dy = (float)System.Math.Sin(lat);
        float ay = MathF.Abs(dy);
        float half = n * 0.5f;
        var basis = Basis;
        int o = y * width * 4;

        for (int x = 0; x < width; x++, o += 4)
        {
            float dx = cosLat * sinLon[x];
            float dz = -cosLat * cosLon[x];
            float ax = MathF.Abs(dx);
            float az = MathF.Abs(dz);

            int face;
            float major;
            if (ay >= ax && ay >= az)
            {
                face = dy > 0f ? 4 : 5;
                major = ay;
            }
            else if (ax >= az)
            {
                face = dx > 0f ? 1 : 3;
                major = ax;
            }
            else
            {
                face = dz < 0f ? 0 : 2;
                major = az;
            }

            int b = face * 6;
            float inv = 1f / major;
            float a = (dx * basis[b] + dy * basis[b + 1] + dz * basis[b + 2]) * inv;
            float v = (dx * basis[b + 3] + dy * basis[b + 4] + dz * basis[b + 5]) * inv;

            Sample(src[face], n, (a + 1f) * half - 0.5f, (1f - v) * half - 0.5f, output, o);
        }
    }

    // Clamping to texel centres keeps samples inside the face, so the outer half texel holds
    // the edge colour instead of blending with the opposite side or reading out of range.
    static void Sample(byte[] p, int n, float fx, float fy, byte[] output, int o)
    {
        float max = n - 1;
        fx = fx < 0f ? 0f : (fx > max ? max : fx);
        fy = fy < 0f ? 0f : (fy > max ? max : fy);

        int x0 = (int)fx;
        int y0 = (int)fy;
        int x1 = x0 < n - 1 ? x0 + 1 : x0;
        int y1 = y0 < n - 1 ? y0 + 1 : y0;
        float tx = fx - x0;
        float ty = fy - y0;

        float w00 = (1f - tx) * (1f - ty);
        float w10 = tx * (1f - ty);
        float w01 = (1f - tx) * ty;
        float w11 = tx * ty;

        int i00 = (y0 * n + x0) * 4;
        int i10 = (y0 * n + x1) * 4;
        int i01 = (y1 * n + x0) * 4;
        int i11 = (y1 * n + x1) * 4;

        for (int c = 0; c < 4; c++)
            output[o + c] = (byte)(p[i00 + c] * w00 + p[i10 + c] * w10 + p[i01 + c] * w01 + p[i11 + c] * w11 + 0.5f);
    }

    public static byte[] SideBySide(byte[] left, byte[] right, int width, int height)
    {
        CheckImage(left, width, height, 4, nameof(left));
        CheckImage(right, width, height, 4, nameof(right));
        long outBytes = (long)width * 2 * height * 4;
        if (outBytes > Array.MaxLength)
            throw new ArgumentException($"Side by side of {width}x{height} needs {outBytes} bytes, over the array limit.", nameof(width));

        var output = new byte[outBytes];
        int row = width * 4;
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(left, y * row, output, y * row * 2, row);
            Buffer.BlockCopy(right, y * row, output, y * row * 2 + row, row);
        }
        return output;
    }

    public static byte[] Rgb8ToRgba8(byte[] rgb, int width, int height)
    {
        long pixels = CheckImage(rgb, width, height, 3, nameof(rgb));
        if (pixels * 4 > Array.MaxLength)
            throw new ArgumentException($"{width}x{height} RGBA8 is over the array limit.", nameof(width));

        var rgba = new byte[pixels * 4];
        for (long i = 0, s = 0, d = 0; i < pixels; i++, s += 3, d += 4)
        {
            rgba[d] = rgb[s];
            rgba[d + 1] = rgb[s + 1];
            rgba[d + 2] = rgb[s + 2];
            rgba[d + 3] = 255;
        }
        return rgba;
    }

    public static byte[] Rgba8ToRgb8(byte[] rgba, int width, int height)
    {
        long pixels = CheckImage(rgba, width, height, 4, nameof(rgba));
        var rgb = new byte[pixels * 3];
        for (long i = 0, s = 0, d = 0; i < pixels; i++, s += 4, d += 3)
        {
            rgb[d] = rgba[s];
            rgb[d + 1] = rgba[s + 1];
            rgb[d + 2] = rgba[s + 2];
        }
        return rgb;
    }

    static long CheckImage(byte[]? data, int width, int height, int channels, string name)
    {
        if (data == null)
            throw new ArgumentException("Image is null.", name);
        if (width < 1 || height < 1)
            throw new ArgumentException($"Image size must be at least 1x1, got {width}x{height}.", name);
        long pixels = (long)width * height;
        if (data.LongLength != pixels * channels)
            throw new ArgumentException($"Image is {data.LongLength} bytes, expected {pixels * channels} for {width}x{height} with {channels} channels.", name);
        return pixels;
    }
}
