// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

// Runs on every peer because the mouth stream is synced; with nothing tracking the mouth rests at zero.
[ComponentCategory("Users/Avatar/Face")]
public sealed class MouthExpressionDriver : Component
{
    // Name keywords per mouth shape, indexed by the MouthShape enum order. Lowercased substring match.
    private static readonly string[][] Keywords =
    {
        new[] { "jawopen", "jaw_open", "mouthopen", "mouth_open" }, // JawOpen
        new[] { "jawleft", "jaw_left" },                            // JawLeft
        new[] { "jawright", "jaw_right" },                          // JawRight
        new[] { "jawforward", "jaw_forward" },                      // JawForward
        new[] { "pucker", "mouthpucker", "pout" },                  // MouthPucker
        new[] { "mouthwide", "mouthstretch", "stretch" },           // MouthWide
        // The vendor lip set writes Mouth_Upper_UpLeft and Mouth_Lower_DownLeft, and the unified set's smile
        // is MouthCornerPull; without these the tracker's lips and smile reached nothing on those avatars.
        new[] { "smileleft", "mouthsmileleft", "smile_l", "cornerpullleft" },   // SmileLeft
        new[] { "smileright", "mouthsmileright", "smile_r", "cornerpullright" }, // SmileRight
        new[] { "frownleft", "mouthfrownleft", "sad_l", "sadleft" },             // FrownLeft
        new[] { "frownright", "mouthfrownright", "sad_r", "sadright" },          // FrownRight
        new[] { "upperup", "mouthupperup", "upperlipup", "upper_up" },           // UpperUp
        new[] { "lowerdown", "mouthlowerdown", "lowerlipdown", "lower_down" },   // LowerDown
        new[] { "cheekpuff", "cheek_puff", "puff" },                // CheekPuff
        // Tongue intentionally NOT claimed: no mouth-tracking hardware is wired yet, so there is no real tongue
        // signal. Claiming a tongue shape and pinning it to weight 0 mis-renders any avatar whose neutral tongue
        // isn't authored at morph-0 (it pops the tongue out). Re-add an explicit tongue entry when mouth tracking
        // lands. The MouthShape.TongueOut enum value stays for that future use. - xlinka
    };

    private readonly List<(SkinnedMeshRenderer Renderer, int Index, MouthShape Shape)> _targets = new();
    private bool _resolved;
    private MouthStreamManager? _mouth;
    private readonly AvatarFaceWearer _wearer = new();

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (_wearer.Update(Slot))
            Release();
        var wearer = _wearer.Current;
        if (wearer == null)
            return;

        if (!_resolved)
            ResolveTargets();
        if (_targets.Count == 0)
            return;

        if (_mouth != null && _mouth.IsDestroyed)
            _mouth = null;
        _mouth ??= wearer.GetRegisteredComponent<MouthStreamManager>();
        bool tracking = _mouth != null && _mouth.IsTracking;

        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index, shape) = _targets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false; // mesh went away or a higher-priority driver took this shape; rescan
                return;
            }

            float weight = tracking ? System.Math.Clamp(_mouth!.GetWeight(shape), 0f, 1f) : 0f;
            renderer.DriveBlendShapeWeight(index, weight);
        }
    }

    private void Release()
    {
        foreach (var (renderer, index, _) in _targets)
        {
            if (renderer != null && !renderer.IsDestroyed)
                renderer.ReleaseBlendShape(index, this);
        }
        _targets.Clear();
        _resolved = false;
        _mouth = null;
    }

    public override void OnAwake()
    {
        base.OnAwake();
        if (Slot != null)
            Slot.ActiveChanged += OnSlotActiveChanged;
    }

    // Switched off, it stops updating and would never see anything else change, so it lets go now.
    private void OnSlotActiveChanged(Slot slot)
    {
        if (Slot != null && !Slot.IsActive)
            Release();
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.ActiveChanged -= OnSlotActiveChanged;
        Release();
        base.OnDestroy();
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

                for (int s = 0; s < Keywords.Length; s++)
                {
                    bool matched = false;
                    foreach (var kw in Keywords[s])
                    {
                        if (lower.Contains(kw))
                        {
                            // Lowest priority: only take the shape if blink/viseme haven't already claimed it,
                            // so the catch-all expression mapping never fights a more specific driver.
                            if (renderer.ClaimBlendShape(i, this, SkinnedMeshRenderer.BlendShapePriorityExpression))
                                _targets.Add((renderer, i, (MouthShape)s));
                            matched = true;
                            break;
                        }
                    }
                    if (matched)
                        break; // one shape per blendshape
                }
            }
        }
        _resolved = true;
        if (_targets.Count > 0)
            LumoraLogger.Debug($"MouthExpressionDriver: resolved {_targets.Count} mouth blendshape target(s)");
    }
}
