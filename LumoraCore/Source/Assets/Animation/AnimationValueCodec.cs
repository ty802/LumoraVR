// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Animation;

// Everything the track machinery needs to know about one animated value type: its file tag, how it
// reads and writes, and how two keys blend.
//
// So there's ONE generic curve track and ONE generic discrete track instead of a class per
// (family x type) pair: adding a type is a codec, not two more track classes plus two more registry
// entries that can drift apart from each other. -xlinka
public interface IAnimationValueCodec<T>
{
    AnimationElementType ElementType { get; }

    // False for types with no meaningful midpoint (bool, string); those always step.
    bool IsInterpolable { get; }

    T Read(BinaryReader br);

    void Write(BinaryWriter bw, T value);

    // t arrives already clamped to [0,1].
    T Lerp(T a, T b, float t);

    // Tangents are value-per-SECOND, so the segment length is needed to convert them into the
    // normalized parameter space.
    T Hermite(T v0, T outTangent0, T v1, T inTangent1, float t, float segmentSeconds);
}

// Types absent here cannot be animated and cannot be serialized.
public static class AnimationCodecs
{
    // Non-generic face so the registry can read a codec's tag without knowing its element type.
    private interface ICodecInfo
    {
        AnimationElementType ElementType { get; }
    }

    private static readonly Dictionary<Type, object> _byType = new()
    {
        { typeof(bool), new BoolCodec() },
        { typeof(int), new IntCodec() },
        { typeof(long), new LongCodec() },
        { typeof(float), new FloatCodec() },
        { typeof(float2), new Float2Codec() },
        { typeof(float3), new Float3Codec() },
        { typeof(float4), new Float4Codec() },
        { typeof(floatQ), new FloatQCodec() },
        { typeof(color), new ColorCodec() },
        { typeof(colorHDR), new ColorHDRCodec() },
        { typeof(string), new StringCodec() }
    };

    private static readonly Dictionary<AnimationElementType, Type> _byTag = BuildTagMap();

    private static Dictionary<AnimationElementType, Type> BuildTagMap()
    {
        var map = new Dictionary<AnimationElementType, Type>();
        foreach (var pair in _byType)
        {
            map[((ICodecInfo)pair.Value).ElementType] = pair.Key;
        }
        return map;
    }

    // Null when the type cannot be animated.
    public static IAnimationValueCodec<T>? Get<T>()
        => _byType.TryGetValue(typeof(T), out var codec) ? (IAnimationValueCodec<T>)codec : null;

    public static bool IsSupported(Type type) => _byType.ContainsKey(type);

    // Null when unsupported.
    public static AnimationElementType? TagFor(Type type)
        => _byType.TryGetValue(type, out var codec) ? ((ICodecInfo)codec).ElementType : null;

    // Null for a tag this build does not know.
    public static Type? TypeFor(AnimationElementType tag)
        => _byTag.TryGetValue(tag, out var type) ? type : null;

    private abstract class CodecBase<T> : IAnimationValueCodec<T>, ICodecInfo
    {
        public abstract AnimationElementType ElementType { get; }

        public virtual bool IsInterpolable => true;

        public abstract T Read(BinaryReader br);

        public abstract void Write(BinaryWriter bw, T value);

        public abstract T Lerp(T a, T b, float t);

        public virtual T Hermite(T v0, T outTangent0, T v1, T inTangent1, float t, float segmentSeconds)
            => Lerp(v0, v1, t);
    }

    // Hermite basis, shared by every component-wise codec. Tangents are per-second; dt scales them
    // into the normalized segment.
    private static float Hermite1(float v0, float m0, float v1, float m1, float t, float dt)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * v0
             + (t3 - 2f * t2 + t) * dt * m0
             + (-2f * t3 + 3f * t2) * v1
             + (t3 - t2) * dt * m1;
    }

    private sealed class BoolCodec : CodecBase<bool>
    {
        public override AnimationElementType ElementType => AnimationElementType.Bool;
        public override bool IsInterpolable => false;
        public override bool Read(BinaryReader br) => br.ReadBoolean();
        public override void Write(BinaryWriter bw, bool value) => bw.Write(value);
        public override bool Lerp(bool a, bool b, float t) => a;
    }

    private sealed class StringCodec : CodecBase<string>
    {
        public override AnimationElementType ElementType => AnimationElementType.String;
        public override bool IsInterpolable => false;
        public override string Read(BinaryReader br) => br.ReadString();
        public override void Write(BinaryWriter bw, string value) => bw.Write(value ?? string.Empty);
        public override string Lerp(string a, string b, float t) => a;
    }

    // Integers blend through double and round rather than truncating, so a track ramping 0 -> 1 spends
    // half the segment on each value instead of snapping only at the far end.
    private sealed class IntCodec : CodecBase<int>
    {
        public override AnimationElementType ElementType => AnimationElementType.Int;
        public override int Read(BinaryReader br) => br.ReadInt32();
        public override void Write(BinaryWriter bw, int value) => bw.Write(value);
        public override int Lerp(int a, int b, float t) => (int)System.Math.Round(a + (b - a) * (double)t);

        public override int Hermite(int v0, int m0, int v1, int m1, float t, float dt)
            => (int)System.Math.Round(Hermite1(v0, m0, v1, m1, t, dt));
    }

    private sealed class LongCodec : CodecBase<long>
    {
        public override AnimationElementType ElementType => AnimationElementType.Long;
        public override long Read(BinaryReader br) => br.ReadInt64();
        public override void Write(BinaryWriter bw, long value) => bw.Write(value);
        public override long Lerp(long a, long b, float t) => (long)System.Math.Round(a + (b - a) * (double)t);

        public override long Hermite(long v0, long m0, long v1, long m1, float t, float dt)
            => (long)System.Math.Round(Hermite1(v0, m0, v1, m1, t, dt));
    }

    private sealed class FloatCodec : CodecBase<float>
    {
        public override AnimationElementType ElementType => AnimationElementType.Float;
        public override float Read(BinaryReader br) => br.ReadSingle();
        public override void Write(BinaryWriter bw, float value) => bw.Write(value);
        public override float Lerp(float a, float b, float t) => a + (b - a) * t;

        public override float Hermite(float v0, float m0, float v1, float m1, float t, float dt)
            => Hermite1(v0, m0, v1, m1, t, dt);
    }

    private sealed class Float2Codec : CodecBase<float2>
    {
        public override AnimationElementType ElementType => AnimationElementType.Float2;

        public override float2 Read(BinaryReader br) => new float2(br.ReadSingle(), br.ReadSingle());

        public override void Write(BinaryWriter bw, float2 value)
        {
            bw.Write(value.x);
            bw.Write(value.y);
        }

        public override float2 Lerp(float2 a, float2 b, float t) => float2.Lerp(a, b, t);

        public override float2 Hermite(float2 v0, float2 m0, float2 v1, float2 m1, float t, float dt)
            => new float2(
                Hermite1(v0.x, m0.x, v1.x, m1.x, t, dt),
                Hermite1(v0.y, m0.y, v1.y, m1.y, t, dt));
    }

    private sealed class Float3Codec : CodecBase<float3>
    {
        public override AnimationElementType ElementType => AnimationElementType.Float3;

        public override float3 Read(BinaryReader br)
            => new float3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        public override void Write(BinaryWriter bw, float3 value)
        {
            bw.Write(value.x);
            bw.Write(value.y);
            bw.Write(value.z);
        }

        public override float3 Lerp(float3 a, float3 b, float t) => float3.Lerp(a, b, t);

        public override float3 Hermite(float3 v0, float3 m0, float3 v1, float3 m1, float t, float dt)
            => new float3(
                Hermite1(v0.x, m0.x, v1.x, m1.x, t, dt),
                Hermite1(v0.y, m0.y, v1.y, m1.y, t, dt),
                Hermite1(v0.z, m0.z, v1.z, m1.z, t, dt));
    }

    private sealed class Float4Codec : CodecBase<float4>
    {
        public override AnimationElementType ElementType => AnimationElementType.Float4;

        public override float4 Read(BinaryReader br)
            => new float4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        public override void Write(BinaryWriter bw, float4 value)
        {
            bw.Write(value.x);
            bw.Write(value.y);
            bw.Write(value.z);
            bw.Write(value.w);
        }

        public override float4 Lerp(float4 a, float4 b, float t) => float4.Lerp(a, b, t);

        public override float4 Hermite(float4 v0, float4 m0, float4 v1, float4 m1, float t, float dt)
            => new float4(
                Hermite1(v0.x, m0.x, v1.x, m1.x, t, dt),
                Hermite1(v0.y, m0.y, v1.y, m1.y, t, dt),
                Hermite1(v0.z, m0.z, v1.z, m1.z, t, dt),
                Hermite1(v0.w, m0.w, v1.w, m1.w, t, dt));
    }

    private sealed class FloatQCodec : CodecBase<floatQ>
    {
        public override AnimationElementType ElementType => AnimationElementType.FloatQ;

        public override floatQ Read(BinaryReader br)
            => new floatQ(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        public override void Write(BinaryWriter bw, floatQ value)
        {
            bw.Write(value.x);
            bw.Write(value.y);
            bw.Write(value.z);
            bw.Write(value.w);
        }

        // Rotations blend on the arc, not the chord.
        public override floatQ Lerp(floatQ a, floatQ b, float t) => floatQ.Slerp(a, b, t);

        // Cubic rotation is component-wise Hermite plus a renormalize, the same shape glTF cubic
        // rotation sampling uses. The neighbour is flipped into v0's hemisphere first: q and -q are the
        // same rotation, and without the flip a sign disagreement between two exported keys sends the
        // interpolation the long way round and the bone spins through a full turn mid-segment. -xlinka
        public override floatQ Hermite(floatQ v0, floatQ m0, floatQ v1, floatQ m1, float t, float dt)
        {
            if (floatQ.Dot(v0, v1) < 0f)
            {
                v1 = new floatQ(-v1.x, -v1.y, -v1.z, -v1.w);
                m1 = new floatQ(-m1.x, -m1.y, -m1.z, -m1.w);
            }

            var q = new floatQ(
                Hermite1(v0.x, m0.x, v1.x, m1.x, t, dt),
                Hermite1(v0.y, m0.y, v1.y, m1.y, t, dt),
                Hermite1(v0.z, m0.z, v1.z, m1.z, t, dt),
                Hermite1(v0.w, m0.w, v1.w, m1.w, t, dt));

            return q.LengthSquared > 1e-12f ? q.Normalized : v0;
        }
    }

    private sealed class ColorCodec : CodecBase<color>
    {
        public override AnimationElementType ElementType => AnimationElementType.Color;

        public override color Read(BinaryReader br)
            => new color(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        public override void Write(BinaryWriter bw, color value)
        {
            bw.Write(value.r);
            bw.Write(value.g);
            bw.Write(value.b);
            bw.Write(value.a);
        }

        public override color Lerp(color a, color b, float t) => color.Lerp(a, b, t);

        public override color Hermite(color v0, color m0, color v1, color m1, float t, float dt)
            => new color(
                Hermite1(v0.r, m0.r, v1.r, m1.r, t, dt),
                Hermite1(v0.g, m0.g, v1.g, m1.g, t, dt),
                Hermite1(v0.b, m0.b, v1.b, m1.b, t, dt),
                Hermite1(v0.a, m0.a, v1.a, m1.a, t, dt));
    }

    private sealed class ColorHDRCodec : CodecBase<colorHDR>
    {
        public override AnimationElementType ElementType => AnimationElementType.ColorHDR;

        public override colorHDR Read(BinaryReader br)
            => new colorHDR(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        public override void Write(BinaryWriter bw, colorHDR value)
        {
            bw.Write(value.r);
            bw.Write(value.g);
            bw.Write(value.b);
            bw.Write(value.a);
        }

        public override colorHDR Lerp(colorHDR a, colorHDR b, float t) => colorHDR.Lerp(a, b, t);

        public override colorHDR Hermite(colorHDR v0, colorHDR m0, colorHDR v1, colorHDR m1, float t, float dt)
            => new colorHDR(
                Hermite1(v0.r, m0.r, v1.r, m1.r, t, dt),
                Hermite1(v0.g, m0.g, v1.g, m1.g, t, dt),
                Hermite1(v0.b, m0.b, v1.b, m1.b, t, dt),
                Hermite1(v0.a, m0.a, v1.a, m1.a, t, dt));
    }
}
