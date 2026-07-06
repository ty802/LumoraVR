// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Drives an avatar's authored breathing blendshape (chest/belly rise), when it has one, on the same
/// rhythm as the solver's bone breathing so mesh and skeleton inhale together. Runs locally on every
/// peer (weights derive from world time, no sync churn). Avatars without a breath shape are a no-op;
/// the bone breathing still carries them.
/// </summary>
[ComponentCategory("Users/Avatar/Face")]
public sealed class BreathingDriver : Component
{
    /// <summary>0 disables, 1 = full authored shape at peak inhale.</summary>
    public readonly Sync<float> Amount = new();

    // Same rhythm constants as the solver's bone breathing, so skin and skeleton stay in phase.
    private const float PeriodSeconds = 4.3f;

    private static readonly string[] Keywords = { "breathe", "breathing", "breath" };

    private readonly List<(SkinnedMeshRenderer Renderer, int Index)> _targets = new();
    private bool _resolved;

    public override void OnInit()
    {
        base.OnInit();
        Amount.Value = 1f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!_resolved)
            Resolve();
        if (_targets.Count == 0)
            return;

        float amount = System.Math.Clamp(Amount.Value, 0f, 2f);
        float t = (float)(World?.Time.TotalTime ?? 0.0) * (MathF.PI * 2f / PeriodSeconds);
        // Matches the solver's two-sine cycle (inhale slightly quicker than exhale), mapped 0..1.
        float breath = MathF.Sin(t) + 0.35f * MathF.Sin(t * 2f + 0.8f);
        float weight = System.Math.Clamp((breath + 1.35f) / 2.7f, 0f, 1f) * amount;

        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index) = _targets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false; // renderer went away or the claim was lost - rescan next frame
                return;
            }
            renderer.DriveBlendShapeWeight(index, weight);
        }
    }

    private void Resolve()
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
                bool matched = false;
                for (int k = 0; k < Keywords.Length && !matched; k++)
                    matched = lower.Contains(Keywords[k]);
                if (!matched)
                    continue;
                if (renderer.ClaimBlendShape(i, this, SkinnedMeshRenderer.BlendShapePriorityExpression))
                    _targets.Add((renderer, i));
            }
        }
        _resolved = true;
    }
}
