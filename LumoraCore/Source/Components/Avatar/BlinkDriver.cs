// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Procedural eye blink: finds blink blendshapes on the avatar's skinned meshes (by name) and pulses
/// them closed on a randomized interval. Runs locally on every peer from its own timer - writes are
/// local (no network churn), so each peer animates the blink without replicating per-frame weights.
/// </summary>
[ComponentCategory("Users/Avatar")]
public sealed class BlinkDriver : Component
{
    public readonly Sync<float> MinInterval = new();
    public readonly Sync<float> MaxInterval = new();
    public readonly Sync<float> BlinkDuration = new();

    private static readonly string[] BlinkKeywords =
        { "blink", "eyesclosed", "eyes_closed", "eyeclose", "eye_close", "eyeblink", "まばたき" };

    private readonly List<(SkinnedMeshRenderer Renderer, int Index)> _targets = new();
    private bool _resolved;
    private float _timer;
    private float _nextBlink = 3f;
    private float _blinkElapsed = -1f; // <0 = eyes open / not mid-blink
    private readonly Random _rng = new();
    private EyeStreamManager? _eyeTracking;

    public override void OnInit()
    {
        base.OnInit();
        MinInterval.Value = 2.5f;
        MaxInterval.Value = 6f;
        BlinkDuration.Value = 0.12f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!_resolved)
            ResolveTargets();
        if (_targets.Count == 0)
            return;

        // Real eye tracking drives the lids directly (blink = 1 - openness); otherwise blink on a timer.
        _eyeTracking ??= Slot.ActiveUserRoot?.GetRegisteredComponent<EyeStreamManager>();
        float weight;
        if (_eyeTracking != null && _eyeTracking.IsTracking)
        {
            _blinkElapsed = -1f; // keep the procedural arc idle so it doesn't fight real data
            weight = 1f - System.Math.Clamp(_eyeTracking.Openness, 0f, 1f);
        }
        else
        {
            if (_blinkElapsed < 0f)
            {
                _timer += delta;
                if (_timer >= _nextBlink)
                {
                    _blinkElapsed = 0f;
                    _timer = 0f;
                }
            }

            weight = 0f;
            if (_blinkElapsed >= 0f)
            {
                _blinkElapsed += delta;
                float dur = MathF.Max(BlinkDuration.Value, 0.02f);
                float t = _blinkElapsed / dur;
                if (t >= 1f)
                {
                    _blinkElapsed = -1f;
                    float span = MathF.Max(0f, MaxInterval.Value - MinInterval.Value);
                    _nextBlink = MinInterval.Value + (float)_rng.NextDouble() * span;
                }
                else
                {
                    weight = MathF.Sin(t * MathF.PI); // 0 -> 1 -> 0 close/open arc
                }
            }
        }

        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index) = _targets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false; // mesh went away or our claim was lost - rescan next frame
                return;
            }
            renderer.DriveBlendShapeWeight(index, weight);
        }
    }

    // Pick a NON-overlapping blink set per mesh: a single combined (both-eye) shape if one exists, else the
    // left+right pair. Driving a combined shape AND its left/right counterparts together stacks the eyelids
    // past fully closed - that's the "blink goes too far" overshoot. We also claim each shape so the other
    // face drivers leave it alone.
    private void ResolveTargets()
    {
        _targets.Clear();
        foreach (var renderer in Slot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            int combined = -1, left = -1, right = -1;
            for (int i = 0; i < renderer.BlendShapeCount; i++)
            {
                var name = renderer.BlendShapeName(i);
                if (string.IsNullOrEmpty(name) || !IsBlinkName(name.ToLowerInvariant()))
                    continue;
                var lower = name.ToLowerInvariant();
                switch (Side(lower))
                {
                    case 0: if (left < 0) left = i; break;
                    case 1: if (right < 0) right = i; break;
                    default: if (combined < 0) combined = i; break;
                }
            }

            if (combined >= 0)
            {
                TryClaimTarget(renderer, combined);
            }
            else
            {
                if (left >= 0) TryClaimTarget(renderer, left);
                if (right >= 0) TryClaimTarget(renderer, right);
            }
        }
        _resolved = true;
        if (_targets.Count > 0)
            LumoraLogger.Log($"BlinkDriver: resolved {_targets.Count} blink blendshape target(s)");
    }

    private static bool IsBlinkName(string lower)
    {
        foreach (var kw in BlinkKeywords)
            if (lower.Contains(kw))
                return true;
        return false;
    }

    // Eye side from a blendshape name: 0 = left, 1 = right, -1 = combined/both. Handles the "left"/"right"
    // words and the common _L / .L / -L (and R) suffix conventions.
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

    private void TryClaimTarget(SkinnedMeshRenderer renderer, int index)
    {
        if (renderer.ClaimBlendShape(index, this, SkinnedMeshRenderer.BlendShapePriorityBlink))
            _targets.Add((renderer, index));
    }
}
