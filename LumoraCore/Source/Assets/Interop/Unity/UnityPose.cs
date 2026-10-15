// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// One animated property: the object (a path under the avatar's root), the component class on it, and
// the property. Transforms use "position", "rotation", "scale" and "euler".
public readonly record struct UnityBinding(string Path, int ClassId, string Attribute, string? Script);

// What one or more clips set when they hold still: a value per property. A clip that really moves is
// kept whole in Motions, to be played rather than read. -xlinka
public sealed class UnityPose
{
    public readonly Dictionary<UnityBinding, float> Floats = new();
    public readonly Dictionary<UnityBinding, float3> Vectors = new();
    public readonly Dictionary<UnityBinding, floatQ> Rotations = new();
    public readonly Dictionary<UnityBinding, UnityRef> References = new();
    public readonly List<UnityAnimationClip> Motions = new();

    public bool IsEmpty => Floats.Count + Vectors.Count + Rotations.Count + References.Count + Motions.Count == 0;

    // The clip's values at this time. Play keeps a moving clip whole instead of reading one instant of it.
    public void Add(UnityAnimationClip clip, float time, bool play)
    {
        if (play && !clip.IsPose)
        {
            if (!Motions.Contains(clip))
                Motions.Add(clip);
            return;
        }
        foreach (var c in clip.Floats)
            Floats[new UnityBinding(c.Path, c.ClassId, c.Attribute, c.Script)] = c.Sample(time);
        foreach (var c in clip.Positions)
            Vectors[new UnityBinding(c.Path, 4, "position", null)] = c.Sample(time);
        foreach (var c in clip.Scales)
            Vectors[new UnityBinding(c.Path, 4, "scale", null)] = c.Sample(time);
        foreach (var c in clip.Eulers)
            Vectors[new UnityBinding(c.Path, 4, "euler", null)] = c.Sample(time);
        foreach (var c in clip.Rotations)
            Rotations[new UnityBinding(c.Path, 4, "rotation", null)] = c.Sample(time);
        foreach (var c in clip.References)
            References[new UnityBinding(c.Path, c.ClassId, c.Attribute, c.Script)] = c.Sample(time);
    }

    // The other pose's values over this one's.
    public void Merge(UnityPose other)
    {
        foreach (var (k, v) in other.Floats) Floats[k] = v;
        foreach (var (k, v) in other.Vectors) Vectors[k] = v;
        foreach (var (k, v) in other.Rotations) Rotations[k] = v;
        foreach (var (k, v) in other.References) References[k] = v;
        foreach (var m in other.Motions)
            if (!Motions.Contains(m))
                Motions.Add(m);
    }

    // Part way from one pose to another. A property only one of them sets is taken from that one; a
    // reference switches at the midpoint.
    public static UnityPose Blend(UnityPose a, UnityPose b, float t)
    {
        var pose = new UnityPose();
        foreach (var (k, v) in a.Floats) pose.Floats[k] = b.Floats.TryGetValue(k, out var w) ? v + (w - v) * t : v;
        foreach (var (k, v) in b.Floats) pose.Floats.TryAdd(k, v);
        foreach (var (k, v) in a.Vectors) pose.Vectors[k] = b.Vectors.TryGetValue(k, out var w) ? float3.Lerp(v, w, t) : v;
        foreach (var (k, v) in b.Vectors) pose.Vectors.TryAdd(k, v);
        foreach (var (k, v) in a.Rotations) pose.Rotations[k] = b.Rotations.TryGetValue(k, out var w) ? floatQ.Slerp(v, w, t) : v;
        foreach (var (k, v) in b.Rotations) pose.Rotations.TryAdd(k, v);
        foreach (var (k, v) in a.References) pose.References[k] = t >= 0.5f && b.References.TryGetValue(k, out var w) ? w : v;
        foreach (var (k, v) in b.References) pose.References.TryAdd(k, v);
        pose.Motions.AddRange(t < 0.5f ? a.Motions : b.Motions);
        return pose;
    }
}
