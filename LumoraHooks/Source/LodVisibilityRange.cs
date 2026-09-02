// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;

namespace Lumora.Godot.Hooks;

// The distance band a renderer is visible in, in metres from the camera. Zero on either end means
// unbounded that way, which is how the renderer already reads it - a level that starts at the camera
// has Begin 0, and the last level of a group that does not cull has End 0.
public readonly struct LodVisibilityRange : IEquatable<LodVisibilityRange>
{
    public readonly float Begin;
    public readonly float BeginMargin;
    public readonly float End;
    public readonly float EndMargin;
    public readonly bool Fade;

    public LodVisibilityRange(float begin, float beginMargin, float end, float endMargin, bool fade)
    {
        Begin = begin;
        BeginMargin = beginMargin;
        End = end;
        EndMargin = endMargin;
        Fade = fade;
    }

    // visible at every distance; what a renderer that left a LOD group goes back to
    public static readonly LodVisibilityRange Unbounded = default;

    // A band that starts at the camera and stops at distance, 0 = unbounded.
    public static LodVisibilityRange To(float distance, float fadeMargin)
    {
        if (distance <= 0f)
            return Unbounded;
        float margin = System.Math.Max(0f, fadeMargin);
        return new LodVisibilityRange(0f, 0f, distance, margin, margin > 0f);
    }

    // The overlap of two bands. A renderer can be inside a LOD group AND carry a view distance of its own
    // (a canvas that stops drawing itself past 40 m), and the only answer that respects both is the tighter
    // of the two. Letting whichever spoke last win means a LOD group silently reinstates a panel someone
    // deliberately culled, or the panel's own distance overrides the group's near band. -xlinka
    public static LodVisibilityRange Tightest(in LodVisibilityRange a, in LodVisibilityRange b)
    {
        float begin = a.Begin;
        float beginMargin = a.BeginMargin;
        if (b.Begin > begin)
        {
            begin = b.Begin;
            beginMargin = b.BeginMargin;
        }

        float end = a.End;
        float endMargin = a.EndMargin;
        if (end <= 0f || (b.End > 0f && b.End < end))
        {
            end = b.End;
            endMargin = b.EndMargin;
        }

        return new LodVisibilityRange(begin, beginMargin, end, endMargin, a.Fade || b.Fade);
    }

    public bool Equals(LodVisibilityRange other) =>
        Begin.Equals(other.Begin) && BeginMargin.Equals(other.BeginMargin)
        && End.Equals(other.End) && EndMargin.Equals(other.EndMargin) && Fade == other.Fade;

    public override bool Equals(object? obj) => obj is LodVisibilityRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Begin, BeginMargin, End, EndMargin, Fade);

    public void ApplyTo(GeometryInstance3D instance)
    {
        if (instance == null || !GodotObject.IsInstanceValid(instance))
            return;

        instance.VisibilityRangeBegin = Begin;
        instance.VisibilityRangeBeginMargin = BeginMargin;
        instance.VisibilityRangeEnd = End;
        instance.VisibilityRangeEndMargin = EndMargin;

        // Self-fade dissolves this instance over its margins. Dependencies fades the CHILDREN of a
        // visibility parent instead, which is a different feature to the one a LOD group wants.
        instance.VisibilityRangeFadeMode = Fade
            ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Self
            : GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
    }
}

// This exists because a mesh renderer hook creates its Godot instances lazily and can create more of
// them later (the per-surface ordering path builds one per surface after the fact). A LOD group that
// reached in and set the range on whatever instances happened to exist would silently lose the
// setting on everything built afterwards, so the range is pushed to the hook and the hook is what
// remembers to apply it to each instance it makes. -xlinka
public interface ILodRangeTarget
{
    void SetLodVisibilityRange(in LodVisibilityRange range);

    // Largest world-space dimension of what this target draws, for size-aware banding. False means
    // "cannot be measured", and the size-aware path treats that as exempt: the safe answer for
    // something you cannot size is to keep drawing it.
    bool TryGetLargestDimension(out float size)
    {
        size = 0f;
        return false;
    }
}
