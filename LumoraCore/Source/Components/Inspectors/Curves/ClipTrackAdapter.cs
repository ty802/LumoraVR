// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Reads one clip track into editor rows and builds a fresh track back from them. Edits never touch the
// track they read: the clip is shared by everyone who requested it, so a changed key would show up in
// every animator on this machine without ever reaching anyone else's. -xlinka
public sealed class ClipTrackAdapter<T> : IClipTrackAdapter
{
    private static readonly int ChannelCount = CurveChannels<T>.Count;
    private static readonly CurveValueKind Kind = CurveChannels<T>.Kind;

    public CurveTrackInfo Describe(AnimationTrack track)
    {
        bool discrete = track is DiscreteAnimationTrack<T>;
        var info = new CurveTrackInfo
        {
            Group = track.Node,
            Name = track.Property,
            Channels = CurveChannels<T>.Labels,
            Rotation = Kind == CurveValueKind.Rotation,
            Whole = Kind is CurveValueKind.Whole or CurveValueKind.Flag,
            HoldOnly = discrete || Kind == CurveValueKind.Flag,
            AllowHold = true,
            // Cubic rotation keys carry quaternion-space tangents nobody can aim by hand.
            AllowSmooth = !discrete && Kind is CurveValueKind.Scalar or CurveValueKind.Whole,
        };
        if (Kind == CurveValueKind.Text)
        {
            info.Editable = false;
            info.ReadOnlyReason = "text keys have no curve to draw";
        }
        return info;
    }

    public List<CurveKeyRow> Read(AnimationTrack track)
    {
        var rows = new List<CurveKeyRow>(track.KeyCount);
        if (track is CurveAnimationTrack<T> curve)
        {
            for (int i = 0; i < curve.KeyCount; i++)
            {
                var row = new CurveKeyRow(ChannelCount) { Time = curve.KeyTime(i), Shape = curve.KeyInterpolation(i) };
                var value = curve.KeyValue(i);
                CurveChannels<T>.Split(value, row.Values);
                if (Kind == CurveValueKind.Rotation)
                {
                    row.Rotation = AsRotation(value);
                    row.RotationIn = AsRotation(curve.KeyInTangent(i));
                    row.RotationOut = AsRotation(curve.KeyOutTangent(i));
                }
                else if (curve.HasTangents)
                {
                    CurveChannels<T>.Split(curve.KeyInTangent(i), row.In);
                    CurveChannels<T>.Split(curve.KeyOutTangent(i), row.Out);
                }
                rows.Add(row);
            }
        }
        else if (track is DiscreteAnimationTrack<T> discrete)
        {
            for (int i = 0; i < discrete.KeyCount; i++)
            {
                var row = new CurveKeyRow(ChannelCount) { Time = discrete.KeyTime(i), Shape = KeyframeInterpolation.Step };
                var value = discrete.KeyValue(i);
                CurveChannels<T>.Split(value, row.Values);
                if (Kind == CurveValueKind.Rotation)
                    row.Rotation = AsRotation(value);
                rows.Add(row);
            }
        }

        if (Kind == CurveValueKind.Rotation)
            Unwrap(rows);
        return rows;
    }

    public AnimationTrack Build(AnimationTrack template, IReadOnlyList<CurveKeyRow> rows)
    {
        if (template is DiscreteAnimationTrack<T>)
        {
            var discrete = new DiscreteAnimationTrack<T> { Node = template.Node, Property = template.Property };
            for (int i = 0; i < rows.Count; i++)
                discrete.AddKey(rows[i].Time, ValueOf(rows[i]));
            return discrete;
        }

        var curve = new CurveAnimationTrack<T> { Node = template.Node, Property = template.Property };
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (Kind == CurveValueKind.Rotation)
                curve.AddKey(row.Time, ValueOf(row), row.Shape, FromRotation(row.RotationIn), FromRotation(row.RotationOut));
            else
                curve.AddKey(row.Time, ValueOf(row), row.Shape, CurveChannels<T>.Join(row.In), CurveChannels<T>.Join(row.Out));
        }
        return curve;
    }

    public ICurveSampler Sampler(AnimationTrack track)
        => new TrackSampler((IAnimationTrack<T>)track);

    private static T ValueOf(CurveKeyRow row)
    {
        if (Kind == CurveValueKind.Rotation && !row.RotationEdited)
            return FromRotation(row.Rotation);
        return CurveChannels<T>.Join(row.Values);
    }

    // Each key's Euler angles moved by whole turns to sit nearest the key before it, so a bone turning
    // through 180 draws as one sweep instead of a jump to the other edge of the graph.
    private static void Unwrap(List<CurveKeyRow> rows)
    {
        for (int i = 1; i < rows.Count; i++)
        {
            for (int c = 0; c < ChannelCount; c++)
            {
                float previous = rows[i - 1].Values[c];
                float value = rows[i].Values[c];
                rows[i].Values[c] = value + 360f * MathF.Round((previous - value) / 360f);
            }
        }
    }

    private static floatQ AsRotation(T value) => Unsafe.As<T, floatQ>(ref value);

    private static T FromRotation(floatQ value) => Unsafe.As<floatQ, T>(ref value);

    private sealed class TrackSampler : ICurveSampler
    {
        private readonly IAnimationTrack<T> _track;
        private readonly float[] _scratch = new float[System.Math.Max(1, ChannelCount)];

        public TrackSampler(IAnimationTrack<T> track)
        {
            _track = track;
        }

        public float Sample(int channel, float time, ref float carry)
        {
            if (channel < 0 || channel >= ChannelCount || !_track.Evaluate(time, out var value))
                return 0f;
            CurveChannels<T>.Split(value, _scratch);
            float x = _scratch[channel];
            if (Kind == CurveValueKind.Rotation && float.IsFinite(carry))
                x += 360f * MathF.Round((carry - x) / 360f);
            carry = x;
            return x;
        }

        public CurveKeyRow RowAt(float time)
        {
            var row = new CurveKeyRow(ChannelCount) { Time = time, Shape = KeyframeInterpolation.Linear };
            if (_track.Evaluate(time, out var value))
            {
                CurveChannels<T>.Split(value, row.Values);
                if (Kind == CurveValueKind.Rotation)
                    row.Rotation = AsRotation(value);
            }
            return row;
        }
    }
}
