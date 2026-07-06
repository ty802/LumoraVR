// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Drives the non-blink eye expression blendshapes: pupil dilation, eye widen, squint (squeeze), and
/// eye-area frown/brow-lower. Pupil dilation runs PROCEDURALLY (a slow noise, so the eyes look alive even
/// with no hardware) and switches to real values when eye tracking is active; widen/squeeze/frown come
/// from real tracking only and rest at 0 otherwise. Blendshapes are matched by name and claimed so this
/// never fights the blink/viseme/expression drivers (one owner per shape). Eye gaze + blink are handled
/// separately by EyeGazeDriver / BlinkDriver.
/// </summary>
[ComponentCategory("Users/Avatar/Face")]
public sealed class EyeExpressionDriver : Component
{
    /// <summary>Procedural pupil noise speed (cycles/sec-ish).</summary>
    public readonly Sync<float> PupilNoiseSpeed = new();
    /// <summary>Procedural pupil dilation floor (0..1).</summary>
    public readonly Sync<float> PupilNoiseMin = new();
    /// <summary>Procedural pupil dilation ceiling (0..1).</summary>
    public readonly Sync<float> PupilNoiseMax = new();

    private enum Channel { Pupil, Widen, Squeeze, Frown }

    private static readonly (Channel Channel, string[] Keywords)[] ChannelKeywords =
    {
        (Channel.Pupil, new[] { "pupil", "dilat" }),
        (Channel.Widen, new[] { "widen", "eyewide" }),
        (Channel.Squeeze, new[] { "squint", "squeeze" }),
        (Channel.Frown, new[] { "browdown", "brow_down", "eyefrown" }),
    };

    // side: -1 combined, 0 left, 1 right
    private readonly List<(SkinnedMeshRenderer Renderer, int Index, Channel Channel, int Side)> _targets = new();
    private bool _resolved;
    private float _time;
    private EyeStreamManager? _eye;

    public override void OnInit()
    {
        base.OnInit();
        PupilNoiseSpeed.Value = 0.25f;
        PupilNoiseMin.Value = 0.15f;
        PupilNoiseMax.Value = 0.45f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        _time += delta;

        if (!_resolved)
            ResolveTargets();
        if (_targets.Count == 0)
            return;

        _eye ??= Slot.ActiveUserRoot?.GetRegisteredComponent<EyeStreamManager>();
        bool tracking = _eye != null && _eye.IsTracking;
        float proceduralPupil = ProceduralPupil();

        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index, channel, side) = _targets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false; // mesh went away or our claim was lost - rescan next frame
                return;
            }

            float weight = channel switch
            {
                Channel.Pupil => tracking && _eye != null ? System.Math.Clamp(_eye.Pupil, 0f, 1f) : proceduralPupil,
                Channel.Widen => tracking ? EyeChannel(channel, side) : 0f,
                Channel.Squeeze => tracking ? EyeChannel(channel, side) : 0f,
                Channel.Frown => tracking ? EyeChannel(channel, side) : 0f,
                _ => 0f,
            };

            renderer.DriveBlendShapeWeight(index, System.Math.Clamp(weight, 0f, 1f));
        }
    }

    private float EyeChannel(Channel channel, int side)
    {
        if (_eye == null)
            return 0f;

        float left, right;
        switch (channel)
        {
            case Channel.Widen: left = _eye.GetWiden(Chirality.Left); right = _eye.GetWiden(Chirality.Right); break;
            case Channel.Squeeze: left = _eye.GetSqueeze(Chirality.Left); right = _eye.GetSqueeze(Chirality.Right); break;
            case Channel.Frown: left = _eye.GetFrown(Chirality.Left); right = _eye.GetFrown(Chirality.Right); break;
            default: return 0f;
        }

        return side == 0 ? left : side == 1 ? right : (left + right) * 0.5f;
    }

    // Slow multi-frequency noise in [PupilNoiseMin, PupilNoiseMax] so the pupils drift like a living eye
    // even without a tracker.
    private float ProceduralPupil()
    {
        float t = _time * PupilNoiseSpeed.Value;
        float n = MathF.Sin(t) * 0.5f + MathF.Sin(t * 2.3f + 1.7f) * 0.3f + MathF.Sin(t * 0.6f + 0.5f) * 0.2f;
        n = n * 0.5f + 0.5f; // -> 0..1
        return PupilNoiseMin.Value + (PupilNoiseMax.Value - PupilNoiseMin.Value) * n;
    }

    private void ResolveTargets()
    {
        _targets.Clear();
        foreach (var renderer in Slot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            for (int i = 0; i < renderer.BlendShapeCount; i++)
            {
                var name = renderer.BlendShapeName(i);
                if (string.IsNullOrEmpty(name))
                    continue;
                var lower = name.ToLowerInvariant();

                for (int c = 0; c < ChannelKeywords.Length; c++)
                {
                    if (!ContainsAny(lower, ChannelKeywords[c].Keywords))
                        continue;
                    if (renderer.ClaimBlendShape(i, this, SkinnedMeshRenderer.BlendShapePriorityEye))
                        _targets.Add((renderer, i, ChannelKeywords[c].Channel, Side(lower)));
                    break; // one channel per blendshape
                }
            }
        }
        _resolved = true;
    }

    private static bool ContainsAny(string lower, string[] keywords)
    {
        foreach (var kw in keywords)
            if (lower.Contains(kw))
                return true;
        return false;
    }

    private static int Side(string lower)
    {
        if (lower.Contains("left"))
            return 0;
        if (lower.Contains("right"))
            return 1;
        if (EndsWithSideToken(lower, 'l'))
            return 0;
        if (EndsWithSideToken(lower, 'r'))
            return 1;
        return -1;
    }

    private static bool EndsWithSideToken(string s, char side)
    {
        if (s.Length < 2 || s[s.Length - 1] != side)
            return false;
        char sep = s[s.Length - 2];
        return sep == '_' || sep == '.' || sep == '-' || sep == ' ';
    }
}
