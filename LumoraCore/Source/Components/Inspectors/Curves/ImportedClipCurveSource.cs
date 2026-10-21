// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Assets.Interop.Unity;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// The clips inside an imported avatar's animator program, opened as curves: a tail wag, an ear flick,
// every moving curve of every clip with a length, grouped by clip. Member "clip:<name>" narrows it to one.
//
// View only for now. The program is one asset holding every layer's controller and clip as the package
// wrote them, and changing one clip means writing that clip back into it and re-storing the program; that
// is its own job. What is shown is exactly what plays: each curve is sampled the way the live animator
// samples it, in our space (positions and rotations mirrored the way the import mirrors the bones). -xlinka
public sealed class ImportedClipCurveSource : ICurveSource
{
    private enum Kind
    {
        Float,
        Position,
        Scale,
        Euler,
        Rotation,
    }

    private sealed class Entry
    {
        public UnityAnimationClip Clip = null!;
        public UnityAnimationClip.Curve Curve = null!;
        public Kind Kind;
    }

    private const float RadToDeg = 180f / MathF.PI;

    private readonly ImportedAvatarAnimator _animator;
    private readonly string _clipName;
    private readonly List<CurveTrackInfo> _tracks = new();
    private readonly List<Entry> _entries = new();
    private UnityAnimatorProgram? _described;
    private float _duration = 1f;

    public ImportedClipCurveSource(ImportedAvatarAnimator animator, string member)
    {
        _animator = animator;
        _clipName = member.StartsWith("clip:", StringComparison.Ordinal) ? member[5..] : "";
    }

    private UnityAnimatorProgram? Program
    {
        get
        {
            var program = _animator.IsDestroyed ? null : (_animator.Controller.Asset as ImportedAnimatorAsset)?.Program;
            if (!ReferenceEquals(program, _described))
                Describe(program);
            return program;
        }
    }

    public string Title => _clipName.Length > 0 ? _clipName : "Avatar animations";

    public bool IsAlive => !_animator.IsDestroyed;

    public bool IsReady => Program != null;

    public long Version
    {
        get
        {
            var program = Program;
            return program == null ? 0 : RuntimeHelpers.GetHashCode(program);
        }
    }

    public bool CanEdit => false;

    public string ReadOnlyReason => IsReady ? "imported avatar animations are view only for now" : "the avatar's animations are loading";

    public IReadOnlyList<CurveTrackInfo> Tracks
    {
        get
        {
            _ = Program;
            return _tracks;
        }
    }

    public float Duration
    {
        get
        {
            _ = Program;
            return _duration;
        }
    }

    public bool FixedTimeRange => false;

    public float SnapStep => 1f / 60f;

    private void Describe(UnityAnimatorProgram? program)
    {
        _described = program;
        _tracks.Clear();
        _entries.Clear();
        _duration = 1f;
        if (program == null)
            return;

        var clips = new List<UnityAnimationClip>();
        var seen = new HashSet<UnityAnimationClip>();
        foreach (var clip in program.Clips.Values)
        {
            if (clip == null || !seen.Add(clip))
                continue;
            if (_clipName.Length > 0 ? !string.Equals(clip.Name, _clipName, StringComparison.Ordinal) : !(clip.Length > 0f))
                continue;
            clips.Add(clip);
        }
        clips.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // A clip merged twice (once per layer that plays it) lists once.
        var names = new HashSet<string>(StringComparer.Ordinal);
        float longest = 0f;
        foreach (var clip in clips)
        {
            if (!names.Add(clip.Name))
                continue;
            int before = _entries.Count;
            foreach (var curve in clip.Rotations)
                if (!curve.IsConstant || _clipName.Length > 0) Add(clip, curve, Kind.Rotation, "Rotation", new[] { "X", "Y", "Z" });
            foreach (var curve in clip.Eulers)
                if (!curve.IsConstant || _clipName.Length > 0) Add(clip, curve, Kind.Euler, "Rotation", new[] { "X", "Y", "Z" });
            foreach (var curve in clip.Positions)
                if (!curve.IsConstant || _clipName.Length > 0) Add(clip, curve, Kind.Position, "Position", new[] { "X", "Y", "Z" });
            foreach (var curve in clip.Scales)
                if (!curve.IsConstant || _clipName.Length > 0) Add(clip, curve, Kind.Scale, "Scale", new[] { "X", "Y", "Z" });
            foreach (var curve in clip.Floats)
                if (!curve.IsConstant || _clipName.Length > 0) Add(clip, curve, Kind.Float, curve.Attribute, new[] { "Value" });
            if (_entries.Count > before)
                longest = MathF.Max(longest, clip.Length);
        }
        _duration = longest > 0f ? longest : 1f;
    }

    private void Add(UnityAnimationClip clip, UnityAnimationClip.Curve curve, Kind kind, string property, string[] channels)
    {
        string path = curve.Path ?? "";
        int slash = path.LastIndexOf('/');
        string node = slash >= 0 ? path[(slash + 1)..] : path.Length > 0 ? path : "(root)";
        _entries.Add(new Entry { Clip = clip, Curve = curve, Kind = kind });
        _tracks.Add(new CurveTrackInfo
        {
            Group = clip.Name,
            Name = $"{node} · {property}",
            Channels = channels,
            Editable = false,
            Rotation = kind == Kind.Rotation,
            // Slopes are drawn as handles where the curve has real ones per channel.
            AllowSmooth = kind != Kind.Rotation,
            ReadOnlyReason = "imported avatar animations are view only for now",
        });
    }

    public List<CurveKeyRow> ReadKeys(int track)
    {
        var rows = new List<CurveKeyRow>();
        _ = Program;
        if (track < 0 || track >= _entries.Count)
            return rows;
        var entry = _entries[track];
        switch (entry.Curve)
        {
            case UnityAnimationClip.FloatCurve floats:
                foreach (var key in floats.Keys)
                {
                    var row = new CurveKeyRow(1) { Time = key.Time, Shape = ShapeOf(key.OutSlope) };
                    row.Values[0] = key.Value;
                    row.In[0] = Finite(key.InSlope);
                    row.Out[0] = Finite(key.OutSlope);
                    rows.Add(row);
                }
                break;
            case UnityAnimationClip.VectorCurve vectors:
                foreach (var key in vectors.Keys)
                {
                    var row = new CurveKeyRow(3) { Time = key.Time, Shape = ShapeOf(key.OutSlope.x) };
                    var value = Space(entry.Kind, key.Value);
                    var inSlope = Space(entry.Kind, key.InSlope);
                    var outSlope = Space(entry.Kind, key.OutSlope);
                    for (int c = 0; c < 3; c++)
                    {
                        row.Values[c] = value[c];
                        row.In[c] = Finite(inSlope[c]);
                        row.Out[c] = Finite(outSlope[c]);
                    }
                    rows.Add(row);
                }
                break;
            case UnityAnimationClip.RotationCurve rotations:
                foreach (var key in rotations.Keys)
                {
                    var q = Ours(key.Value);
                    var row = new CurveKeyRow(3) { Time = key.Time, Shape = ShapeOf(key.OutSlope.x), Rotation = q };
                    var e = q.ToEuler();
                    row.Values[0] = e.x * RadToDeg;
                    row.Values[1] = e.y * RadToDeg;
                    row.Values[2] = e.z * RadToDeg;
                    rows.Add(row);
                }
                for (int i = 1; i < rows.Count; i++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        float previous = rows[i - 1].Values[c];
                        rows[i].Values[c] += 360f * MathF.Round((previous - rows[i].Values[c]) / 360f);
                    }
                }
                break;
        }
        return rows;
    }

    public ICurveSampler CreateSampler(int track, IReadOnlyList<CurveKeyRow> rows)
    {
        _ = Program;
        return track >= 0 && track < _entries.Count ? new Sampler(_entries[track]) : EmptySampler.Instance;
    }

    public void Preview(int track, IReadOnlyList<CurveKeyRow> rows)
    {
    }

    public void Commit(int track, IReadOnlyList<CurveKeyRow> before, IReadOnlyList<CurveKeyRow> after, LocaleText step, Action<string> report)
        => report(ReadOnlyReason);

    public void Dispose()
    {
    }

    // An infinite slope holds the key until the next one.
    private static KeyframeInterpolation ShapeOf(float outSlope)
        => float.IsFinite(outSlope) ? KeyframeInterpolation.Cubic : KeyframeInterpolation.Step;

    private static float Finite(float slope) => float.IsFinite(slope) ? slope : 0f;

    // Into our space the way the import places the bones: positions and Euler angles about the mirrored
    // axis flip, scale does not.
    private static float3 Space(Kind kind, float3 v) => kind switch
    {
        Kind.Position => new float3(-v.x, v.y, v.z),
        Kind.Euler => new float3(v.x, -v.y, -v.z),
        _ => v,
    };

    private static floatQ Ours(floatQ q) => new(q.x, -q.y, -q.z, q.w);

    private sealed class Sampler : ICurveSampler
    {
        private readonly Entry _entry;

        public Sampler(Entry entry)
        {
            _entry = entry;
        }

        public float Sample(int channel, float time, ref float carry)
        {
            float x = 0f;
            switch (_entry.Curve)
            {
                case UnityAnimationClip.FloatCurve floats:
                    x = floats.Sample(time);
                    break;
                case UnityAnimationClip.VectorCurve vectors:
                    x = Space(_entry.Kind, vectors.Sample(time))[System.Math.Clamp(channel, 0, 2)];
                    break;
                case UnityAnimationClip.RotationCurve rotations:
                {
                    var e = Ours(rotations.Sample(time)).ToEuler();
                    x = (channel == 0 ? e.x : channel == 1 ? e.y : e.z) * RadToDeg;
                    if (float.IsFinite(carry))
                        x += 360f * MathF.Round((carry - x) / 360f);
                    break;
                }
            }
            carry = x;
            return x;
        }

        public CurveKeyRow RowAt(float time)
        {
            int channels = _entry.Kind == Kind.Float ? 1 : 3;
            var row = new CurveKeyRow(channels) { Time = time, Shape = KeyframeInterpolation.Linear };
            for (int c = 0; c < channels; c++)
            {
                float carry = float.NaN;
                row.Values[c] = Sample(c, time, ref carry);
            }
            return row;
        }
    }
}
