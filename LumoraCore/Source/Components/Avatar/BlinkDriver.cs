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
[ComponentCategory("Users/Common Avatar System")]
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

        if (_blinkElapsed < 0f)
        {
            _timer += delta;
            if (_timer >= _nextBlink)
            {
                _blinkElapsed = 0f;
                _timer = 0f;
            }
        }

        float weight = 0f;
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

        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index) = _targets[i];
            if (renderer == null || renderer.IsDestroyed)
            {
                _resolved = false; // a mesh went away - rescan next frame
                return;
            }
            renderer.DriveBlendShapeWeight(index, weight);
        }
    }

    // Drive every blink-named shape (covers separate left/right + combined blink blendshapes).
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
                foreach (var kw in BlinkKeywords)
                {
                    if (lower.Contains(kw))
                    {
                        _targets.Add((renderer, i));
                        break;
                    }
                }
            }
        }
        _resolved = true;
        if (_targets.Count > 0)
            LumoraLogger.Log($"BlinkDriver: resolved {_targets.Count} blink blendshape target(s)");
    }
}
