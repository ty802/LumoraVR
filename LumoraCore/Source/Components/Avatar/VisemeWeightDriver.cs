// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Lip-sync: maps a <see cref="LipSyncAnalyzer"/>'s per-viseme weights onto the avatar's viseme
/// blendshapes (matched by name) and drives them locally each frame. With the analyzer currently
/// outputting silence (audio not wired yet), this holds the mouth neutral until the voice pipeline
/// lands - then it animates automatically.
/// </summary>
[ComponentCategory("Users/Avatar")]
public sealed class VisemeWeightDriver : Component
{
    public readonly SyncRef<LipSyncAnalyzer> Source = new();
    public readonly Sync<float> StrengthMultiplier = new();

    // Name tokens per viseme (lowercased). A blendshape matches if its name contains any token.
    private static readonly Dictionary<Viseme, string[]> Keywords = new()
    {
        { Viseme.Silence, new[] { "sil", "viseme_sil" } },
        { Viseme.PP, new[] { "v_pp", "viseme_pp", "_pp", "vrc.v_pp" } },
        { Viseme.FF, new[] { "v_ff", "viseme_ff", "_ff", "_fv" } },
        { Viseme.TH, new[] { "v_th", "viseme_th", "_th" } },
        { Viseme.DD, new[] { "v_dd", "viseme_dd", "_dd", "_td" } },
        { Viseme.KK, new[] { "v_kk", "viseme_kk", "_kk" } },
        { Viseme.CH, new[] { "v_ch", "viseme_ch", "_ch", "_sh" } },
        { Viseme.SS, new[] { "v_ss", "viseme_ss", "_ss" } },
        { Viseme.NN, new[] { "v_nn", "viseme_nn", "_nn", "ん" } },
        { Viseme.RR, new[] { "v_rr", "viseme_rr", "_rr" } },
        { Viseme.AA, new[] { "v_aa", "viseme_aa", "_aa", "あ" } },
        { Viseme.E, new[] { "v_e", "viseme_e", "_ee", "え" } },
        { Viseme.IH, new[] { "v_ih", "viseme_ih", "_ih", "い" } },
        { Viseme.OH, new[] { "v_oh", "viseme_oh", "_oh", "_oo", "お" } },
        { Viseme.OU, new[] { "v_ou", "viseme_ou", "_ou", "う" } },
        { Viseme.Laughter, new[] { "laugh" } },
    };

    private readonly List<(SkinnedMeshRenderer Renderer, int Index, Viseme V)> _targets = new();
    private bool _resolved;

    public override void OnInit()
    {
        base.OnInit();
        StrengthMultiplier.Value = 1f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!_resolved)
            ResolveTargets();
        if (_targets.Count == 0)
            return;

        var source = Source.Target;
        float strength = StrengthMultiplier.Value;

        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index, viseme) = _targets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false; // mesh went away or our claim was lost - rescan next frame
                return;
            }
            // No source / silence -> 0 -> mouth closed.
            float w = source != null ? source[viseme] * strength : 0f;
            renderer.DriveBlendShapeWeight(index, System.Math.Clamp(w, 0f, 1f));
        }
    }

    private void ResolveTargets()
    {
        _targets.Clear();

        if (Source.Target == null || Source.Target.IsDestroyed)
            Source.Target = Slot.GetComponentInChildren<LipSyncAnalyzer>() ?? Slot.GetComponent<LipSyncAnalyzer>();

        foreach (var renderer in Slot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            for (int i = 0; i < renderer.BlendShapeCount; i++)
            {
                var name = renderer.BlendShapeName(i);
                if (string.IsNullOrEmpty(name))
                    continue;
                var lower = name.ToLowerInvariant();

                foreach (var kv in Keywords)
                {
                    bool match = false;
                    foreach (var token in kv.Value)
                    {
                        if (lower.Contains(token))
                        {
                            match = true;
                            break;
                        }
                    }
                    if (match)
                    {
                        // Claim the shape so the catch-all expression driver leaves it alone (one owner per shape).
                        if (renderer.ClaimBlendShape(i, this, SkinnedMeshRenderer.BlendShapePriorityViseme))
                            _targets.Add((renderer, i, kv.Key));
                        break; // one viseme per blendshape
                    }
                }
            }
        }
        _resolved = true;
    }
}
