// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// A Unity .anim clip as saved: per curve, the object it animates (a path under the animator's root and
// the component class), the property, and its keys. Positions, rotations and scales are whole-vector
// curves; everything else (on/off, blendshapes, component switches, material values) is one float per
// curve; material swaps are reference curves. Humanoid muscle curves (class 95) are sampled by the
// body animator and retargeted through the imported humanoid rig.
//
// Every number curve runs Unity's cubic Hermite between keys on each key's in and out slopes, vectors and
// rotations one component at a time like the floats; an infinite slope holds the key until the next one.
// A rotation curve comes out normalised. -xlinka
public sealed class UnityAnimationClip
{
    public string Name = "";
    public float Length;
    public bool Loop;
    // Where an additive layer measures this clip's change from: its own pose at this time.
    public float AdditiveReferenceTime;
    public readonly List<FloatCurve> Floats = new();
    public readonly List<VectorCurve> Positions = new();
    public readonly List<VectorCurve> Scales = new();
    public readonly List<VectorCurve> Eulers = new();
    public readonly List<RotationCurve> Rotations = new();
    public readonly List<ReferenceCurve> References = new();

    public readonly record struct Key(float Time, float Value, float InSlope, float OutSlope);

    public abstract class Curve
    {
        public string Path = "";
        public int ClassId;
        public string? Script;
    }

    // The key span holding this time: the index of the key it follows, -1 before the first key, Count-1 at
    // or after the last. Keys are in time order.
    internal static int Span<T>(List<T> keys, Func<T, float> time, float at)
    {
        int lo = 0, hi = keys.Count - 1;
        if (hi < 0 || at <= time(keys[0]))
            return -1;
        if (at >= time(keys[hi]))
            return hi;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (time(keys[mid]) <= at) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    internal static float Hermite(float a, float outSlope, float b, float inSlope, float span, float t)
    {
        if (!float.IsFinite(outSlope) || !float.IsFinite(inSlope))
            return t >= 1f ? b : a;
        float t2 = t * t, t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * a + (t3 - 2f * t2 + t) * outSlope * span
            + (-2f * t3 + 3f * t2) * b + (t3 - t2) * inSlope * span;
    }

    public sealed class FloatCurve : Curve
    {
        public string Attribute = "";
        public readonly List<Key> Keys = new();

        public bool IsConstant
        {
            get
            {
                for (int i = 1; i < Keys.Count; i++)
                {
                    var a = Keys[i - 1]; var b = Keys[i];
                    if (MathF.Abs(b.Value - a.Value) >= 1e-6f) return false;
                    // Equal endpoints can still move between keys when their Hermite tangents bend.
                    if (b.Time > a.Time && float.IsFinite(a.OutSlope) && float.IsFinite(b.InSlope)
                        && (MathF.Abs(a.OutSlope) > 1e-6f || MathF.Abs(b.InSlope) > 1e-6f)) return false;
                }
                return true;
            }
        }

        public float Sample(float time)
        {
            if (Keys.Count == 0)
                return 0f;
            int i = Span(Keys, k => k.Time, time);
            if (i < 0)
                return Keys[0].Value;
            if (i >= Keys.Count - 1)
                return Keys[^1].Value;
            var a = Keys[i];
            var b = Keys[i + 1];
            float span = b.Time - a.Time;
            if (span <= 0f)
                return b.Value;
            return Hermite(a.Value, a.OutSlope, b.Value, b.InSlope, span, (time - a.Time) / span);
        }
    }

    public readonly record struct VectorKey(float Time, float3 Value, float3 InSlope, float3 OutSlope);

    public sealed class VectorCurve : Curve
    {
        public readonly List<VectorKey> Keys = new();

        public bool IsConstant
        {
            get
            {
                for (int i = 1; i < Keys.Count; i++)
                {
                    var a = Keys[i - 1]; var b = Keys[i];
                    if (float3.Distance(a.Value, b.Value) >= 1e-6f) return false;
                    if (b.Time > a.Time && (Bends(a.OutSlope.x, b.InSlope.x) || Bends(a.OutSlope.y, b.InSlope.y) || Bends(a.OutSlope.z, b.InSlope.z)))
                        return false;
                }
                return true;
            }
        }

        public float3 Sample(float time)
        {
            if (Keys.Count == 0)
                return float3.Zero;
            int i = Span(Keys, k => k.Time, time);
            if (i < 0)
                return Keys[0].Value;
            if (i >= Keys.Count - 1)
                return Keys[^1].Value;
            var a = Keys[i];
            var b = Keys[i + 1];
            float span = b.Time - a.Time;
            if (span <= 0f)
                return b.Value;
            float t = (time - a.Time) / span;
            return new float3(Hermite(a.Value.x, a.OutSlope.x, b.Value.x, b.InSlope.x, span, t),
                Hermite(a.Value.y, a.OutSlope.y, b.Value.y, b.InSlope.y, span, t),
                Hermite(a.Value.z, a.OutSlope.z, b.Value.z, b.InSlope.z, span, t));
        }
    }

    public readonly record struct RotationKey(float Time, floatQ Value, float4 InSlope, float4 OutSlope);

    public sealed class RotationCurve : Curve
    {
        public readonly List<RotationKey> Keys = new();

        public bool IsConstant
        {
            get
            {
                for (int i = 1; i < Keys.Count; i++)
                {
                    var a = Keys[i - 1]; var b = Keys[i];
                    if (MathF.Abs(floatQ.Dot(a.Value, b.Value)) <= 0.999999f) return false;
                    if (b.Time > a.Time && (Bends(a.OutSlope.x, b.InSlope.x) || Bends(a.OutSlope.y, b.InSlope.y)
                        || Bends(a.OutSlope.z, b.InSlope.z) || Bends(a.OutSlope.w, b.InSlope.w)))
                        return false;
                }
                return true;
            }
        }

        public floatQ Sample(float time)
        {
            if (Keys.Count == 0)
                return floatQ.Identity;
            int i = Span(Keys, k => k.Time, time);
            if (i < 0)
                return Keys[0].Value;
            if (i >= Keys.Count - 1)
                return Keys[^1].Value;
            var a = Keys[i];
            var b = Keys[i + 1];
            float span = b.Time - a.Time;
            if (span <= 0f)
                return b.Value;
            float t = (time - a.Time) / span;
            var q = new floatQ(Hermite(a.Value.x, a.OutSlope.x, b.Value.x, b.InSlope.x, span, t),
                Hermite(a.Value.y, a.OutSlope.y, b.Value.y, b.InSlope.y, span, t),
                Hermite(a.Value.z, a.OutSlope.z, b.Value.z, b.InSlope.z, span, t),
                Hermite(a.Value.w, a.OutSlope.w, b.Value.w, b.InSlope.w, span, t));
            float length = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return length > 1e-8f ? new floatQ(q.x / length, q.y / length, q.z / length, q.w / length) : a.Value;
        }
    }

    private static bool Bends(float outSlope, float inSlope)
        => float.IsFinite(outSlope) && float.IsFinite(inSlope) && (MathF.Abs(outSlope) > 1e-6f || MathF.Abs(inSlope) > 1e-6f);

    public sealed class ReferenceCurve : Curve
    {
        public string Attribute = "";
        public readonly List<(float Time, UnityRef Value)> Keys = new();

        public bool IsConstant => Keys.Count <= 1 || Keys.All(k => k.Value == Keys[0].Value);

        public UnityRef Sample(float time)
        {
            var value = Keys.Count > 0 ? Keys[0].Value : default;
            foreach (var key in Keys)
            {
                if (key.Time > time)
                    break;
                value = key.Value;
            }
            return value;
        }
    }

    public bool IsEmpty => Floats.Count + Positions.Count + Scales.Count + Eulers.Count + Rotations.Count + References.Count == 0;

    public static UnityAnimationClip? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var doc = UnityYaml.Parse(text).FirstOrDefault(d => d.ClassId == 74);
        if (doc == null)
            return null;
        var clip = new UnityAnimationClip { Name = doc["m_Name"]?.Text ?? "" };
        var settings = doc["m_AnimationClipSettings"];
        clip.Length = MathF.Max(0f, (settings?["m_StopTime"]?.Float() ?? 0f) - (settings?["m_StartTime"]?.Float() ?? 0f));
        clip.Loop = (settings?["m_LoopTime"]?.Int() ?? 0) != 0;
        if ((settings?["m_HasAdditiveReferencePose"]?.Int() ?? 0) != 0 && (settings?["m_AdditiveReferencePoseClip"]?.Ref.IsNull ?? true))
            clip.AdditiveReferenceTime = settings?["m_AdditiveReferencePoseTime"]?.Float() ?? 0f;

        if (doc["m_FloatCurves"] is UnitySeq floats)
        {
            foreach (var item in floats.Items)
            {
                var curve = new FloatCurve { Attribute = item["attribute"]?.Text ?? "" };
                Locate(curve, item);
                if (item["curve"]?["m_Curve"] is UnitySeq keys)
                    foreach (var key in keys.Items)
                        curve.Keys.Add(new Key(key["time"]?.Float() ?? 0f, key["value"]?.Float() ?? 0f, key["inSlope"]?.Float() ?? 0f, key["outSlope"]?.Float() ?? 0f));
                curve.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                clip.Floats.Add(curve);
            }
        }
        ReadVectors(doc["m_PositionCurves"], clip.Positions);
        ReadVectors(doc["m_ScaleCurves"], clip.Scales);
        ReadVectors(doc["m_EulerCurves"], clip.Eulers);
        if (doc["m_RotationCurves"] is UnitySeq rotations)
        {
            foreach (var item in rotations.Items)
            {
                var curve = new RotationCurve();
                Locate(curve, item);
                if (item["curve"]?["m_Curve"] is UnitySeq keys)
                {
                    foreach (var key in keys.Items)
                    {
                        var v = key["value"];
                        curve.Keys.Add(new RotationKey(key["time"]?.Float() ?? 0f,
                            new floatQ(v?["x"]?.Float() ?? 0f, v?["y"]?.Float() ?? 0f, v?["z"]?.Float() ?? 0f, v?["w"]?.Float(1f) ?? 1f),
                            Vector4(key["inSlope"]), Vector4(key["outSlope"])));
                    }
                }
                curve.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                clip.Rotations.Add(curve);
            }
        }
        if (doc["m_PPtrCurves"] is UnitySeq references)
        {
            foreach (var item in references.Items)
            {
                var curve = new ReferenceCurve { Attribute = item["attribute"]?.Text ?? "" };
                Locate(curve, item);
                if (item["curve"] is UnitySeq keys)
                    foreach (var key in keys.Items)
                        curve.Keys.Add((key["time"]?.Float() ?? 0f, key["value"]?.Ref ?? default));
                curve.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                clip.References.Add(curve);
            }
        }
        if (clip.Length <= 0f)
            clip.Length = clip.LastKeyTime();
        return clip;
    }

    private static void Locate(Curve curve, UnityNode item)
    {
        curve.Path = item["path"]?.Text ?? "";
        curve.ClassId = item["classID"]?.Int() ?? 0;
        curve.Script = item["script"]?.Ref.Guid;
    }

    private static float3 Vector3(UnityNode? v)
        => new(v?["x"]?.Float() ?? 0f, v?["y"]?.Float() ?? 0f, v?["z"]?.Float() ?? 0f);

    private static float4 Vector4(UnityNode? v)
        => new(v?["x"]?.Float() ?? 0f, v?["y"]?.Float() ?? 0f, v?["z"]?.Float() ?? 0f, v?["w"]?.Float() ?? 0f);

    private static void ReadVectors(UnityNode? node, List<VectorCurve> into)
    {
        if (node is not UnitySeq seq)
            return;
        foreach (var item in seq.Items)
        {
            var curve = new VectorCurve();
            Locate(curve, item);
            if (item["curve"]?["m_Curve"] is UnitySeq keys)
                foreach (var key in keys.Items)
                    curve.Keys.Add(new VectorKey(key["time"]?.Float() ?? 0f, Vector3(key["value"]), Vector3(key["inSlope"]), Vector3(key["outSlope"])));
            curve.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            into.Add(curve);
        }
    }

    private float LastKeyTime()
    {
        float last = 0f;
        foreach (var c in Floats) if (c.Keys.Count > 0) last = MathF.Max(last, c.Keys[^1].Time);
        foreach (var c in Positions) if (c.Keys.Count > 0) last = MathF.Max(last, c.Keys[^1].Time);
        foreach (var c in Scales) if (c.Keys.Count > 0) last = MathF.Max(last, c.Keys[^1].Time);
        foreach (var c in Eulers) if (c.Keys.Count > 0) last = MathF.Max(last, c.Keys[^1].Time);
        foreach (var c in Rotations) if (c.Keys.Count > 0) last = MathF.Max(last, c.Keys[^1].Time);
        foreach (var c in References) if (c.Keys.Count > 0) last = MathF.Max(last, c.Keys[^1].Time);
        return last;
    }

    // True when every curve holds still: a pose, not a motion.
    public bool IsPose => Floats.All(c => c.IsConstant) && Positions.All(c => c.IsConstant) && Scales.All(c => c.IsConstant)
        && Eulers.All(c => c.IsConstant) && Rotations.All(c => c.IsConstant) && References.All(c => c.IsConstant);

    // The same clip with every curve's object path passed through a rewrite (null drops the curve) and every
    // animated animator parameter renamed. Used for controllers merged in under another object.
    public UnityAnimationClip Rewritten(Func<string, string?> path, Func<string, string> parameter)
    {
        var clip = new UnityAnimationClip { Name = Name, Length = Length, Loop = Loop, AdditiveReferenceTime = AdditiveReferenceTime };
        T? Moved<T>(T curve, T copy) where T : Curve
        {
            var to = curve.ClassId == 95 && curve.Path.Length == 0 ? "" : path(curve.Path);
            if (to == null)
                return null;
            copy.Path = to;
            copy.ClassId = curve.ClassId;
            copy.Script = curve.Script;
            return copy;
        }
        foreach (var c in Floats)
        {
            var copy = Moved(c, new FloatCurve { Attribute = c.ClassId == 95 ? parameter(c.Attribute) : c.Attribute });
            if (copy == null) continue;
            copy.Keys.AddRange(c.Keys);
            clip.Floats.Add(copy);
        }
        void Vectors(List<VectorCurve> from, List<VectorCurve> into)
        {
            foreach (var c in from)
                if (Moved(c, new VectorCurve()) is { } copy)
                {
                    copy.Keys.AddRange(c.Keys);
                    into.Add(copy);
                }
        }
        Vectors(Positions, clip.Positions);
        Vectors(Scales, clip.Scales);
        Vectors(Eulers, clip.Eulers);
        foreach (var c in Rotations)
            if (Moved(c, new RotationCurve()) is { } copy)
            {
                copy.Keys.AddRange(c.Keys);
                clip.Rotations.Add(copy);
            }
        foreach (var c in References)
            if (Moved(c, new ReferenceCurve { Attribute = c.Attribute }) is { } copy)
            {
                copy.Keys.AddRange(c.Keys);
                clip.References.Add(copy);
            }
        return clip;
    }
}
