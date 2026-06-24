// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Generic quantized + delta encode/decode for stream value types - the quantized counterpart to
/// SyncCoder's full-precision encoding. Each value is reduced to its float components; each component is
/// bit-packed to N bits within a range (absolute for quantized frames, or the change from the previous
/// value for delta frames). Quaternions renormalize on compose. Type-keyed, so any supported value type
/// works without a per-stream override. -xlinka
/// </summary>
internal static class StreamCoder
{
    private readonly struct Entry
    {
        public readonly Func<object, float[]> Extract;     // value -> components
        public readonly Func<float[], object> Compose;     // components -> value
        public readonly int Components;

        public Entry(Func<object, float[]> extract, Func<float[], object> compose, int components)
        {
            Extract = extract;
            Compose = compose;
            Components = components;
        }
    }

    private static readonly Dictionary<Type, Entry> Coders = Build();

    /// <summary>Whether this type has a quantized coder (else callers fall back to full precision).</summary>
    public static bool SupportsQuantization(Type type) => Coders.ContainsKey(type);

    /// <summary>How many float components the type reduces to.</summary>
    public static int ComponentCount(Type type) => Coders.TryGetValue(type, out var e) ? e.Components : 0;

    /// <summary>Byte size a quantized value of T occupies at the given bit depth (no length prefix needed).</summary>
    public static int QuantizedByteCount<T>(int bits) => (ComponentCount(typeof(T)) * bits + 7) / 8;

    public static void EncodeQuantized<T>(BitWriter bw, T value, T min, T max, int bits)
    {
        var e = Coders[typeof(T)];
        var v = e.Extract(value!); var lo = e.Extract(min!); var hi = e.Extract(max!);
        for (int i = 0; i < e.Components; i++)
            bw.WriteBits(Quantization.Quantize(v[i], lo[i], hi[i], bits), bits);
    }

    public static T DecodeQuantized<T>(BitReader br, T min, T max, int bits)
    {
        var e = Coders[typeof(T)];
        var lo = e.Extract(min!); var hi = e.Extract(max!);
        var v = new float[e.Components];
        for (int i = 0; i < e.Components; i++)
            v[i] = Quantization.Dequantize(br.ReadBits(bits), lo[i], hi[i], bits);
        return (T)e.Compose(v);
    }

    // Delta: quantize (value - previous) componentwise within the (small) delta range, reconstructed as
    // previous + dequantized delta. Lets slowly-changing values spend fewer bits than a full frame; the
    // stream sends periodic full keyframes so a lost frame's drift is bounded. -xlinka
    public static void EncodeDelta<T>(BitWriter bw, T value, T previous, T deltaMin, T deltaMax, int bits)
    {
        var e = Coders[typeof(T)];
        var v = e.Extract(value!); var p = e.Extract(previous!); var lo = e.Extract(deltaMin!); var hi = e.Extract(deltaMax!);
        for (int i = 0; i < e.Components; i++)
            bw.WriteBits(Quantization.Quantize(v[i] - p[i], lo[i], hi[i], bits), bits);
    }

    public static T DecodeDelta<T>(BitReader br, T previous, T deltaMin, T deltaMax, int bits)
    {
        var e = Coders[typeof(T)];
        var p = e.Extract(previous!); var lo = e.Extract(deltaMin!); var hi = e.Extract(deltaMax!);
        var v = new float[e.Components];
        for (int i = 0; i < e.Components; i++)
            v[i] = p[i] + Quantization.Dequantize(br.ReadBits(bits), lo[i], hi[i], bits);
        return (T)e.Compose(v);
    }

    private static Dictionary<Type, Entry> Build()
    {
        var d = new Dictionary<Type, Entry>();

        d[typeof(float)] = new Entry(v => new[] { (float)v }, c => c[0], 1);
        d[typeof(double)] = new Entry(v => new[] { (float)(double)v }, c => (double)c[0], 1);
        d[typeof(float2)] = new Entry(v => { var a = (float2)v; return new[] { a.x, a.y }; }, c => new float2(c[0], c[1]), 2);
        d[typeof(float3)] = new Entry(v => { var a = (float3)v; return new[] { a.x, a.y, a.z }; }, c => new float3(c[0], c[1], c[2]), 3);
        d[typeof(float4)] = new Entry(v => { var a = (float4)v; return new[] { a.x, a.y, a.z, a.w }; }, c => new float4(c[0], c[1], c[2], c[3]), 4);
        // Quaternion: renormalize on compose so quantization/delta error doesn't leave it non-unit.
        d[typeof(floatQ)] = new Entry(v => { var a = (floatQ)v; return new[] { a.x, a.y, a.z, a.w }; }, c => new floatQ(c[0], c[1], c[2], c[3]).Normalized, 4);
        d[typeof(color)] = new Entry(v => { var a = (color)v; return new[] { a.r, a.g, a.b, a.a }; }, c => new color(c[0], c[1], c[2], c[3]), 4);

        return d;
    }
}
