// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

// Every peer blinks from its own timer and the writes stay local, so per-frame weights never replicate.
//
// The lids are whatever the avatar says they are. A shape named here closes the eyes, lid shapes named here
// follow the eyes up and down, and lid bones turn between the poses given for them. Anything named like a
// blink closes the eyes as well, unless ExactLids says the avatar named everything: then an avatar that names
// no blink does not blink. -xlinka
[ComponentCategory("Users/Avatar")]
public sealed class BlinkDriver : Component
{
    public readonly Sync<float> MinInterval = new();
    public readonly Sync<float> MaxInterval = new();
    public readonly Sync<float> BlinkDuration = new();

    public readonly Sync<bool> ExactLids = new();
    public readonly SyncRef<SkinnedMeshRenderer> LidMesh = new();
    public readonly Sync<string> BlinkShape = new();
    public readonly Sync<string> LookUpShape = new();
    public readonly Sync<string> LookDownShape = new();

    // Upper left, upper right, lower left, lower right. Each pose list holds one rotation per lid, in the
    // lid's parent: open, shut, and open with the eyes looking up or down.
    public readonly SyncRefList<Slot> LidBones = new();
    public readonly SyncFieldList<floatQ> LidOpen = new();
    public readonly SyncFieldList<floatQ> LidShut = new();
    public readonly SyncFieldList<floatQ> LidUp = new();
    public readonly SyncFieldList<floatQ> LidDown = new();

    private static readonly string[] BlinkKeywords =
        { "blink", "eyesclosed", "eyes_closed", "eyeclose", "eye_close", "eyeblink", "まばたき" };

    // Side: 0 left, 1 right, -1 a shape that closes both.
    private readonly List<(SkinnedMeshRenderer Renderer, int Index, int Side)> _targets = new();
    private readonly List<(SkinnedMeshRenderer Renderer, int Index, bool Up)> _lookTargets = new();
    private readonly List<(Slot Bone, floatQ Home, int Lid)> _bones = new();
    private bool _resolved;
    private float _retry;
    private float _timer;
    private float _nextBlink = 3f;
    private float _blinkElapsed = -1f; // <0 = eyes open / not mid-blink
    private readonly Random _rng = new();
    private EyeStreamManager? _eyeTracking;
    private EyeGazeDriver? _gaze;
    private readonly AvatarFaceWearer _wearer = new();

    // How shut each eye is this frame, 0 open to 1 shut. Zero while held.
    public float LeftShut { get; private set; }
    public float RightShut { get; private set; }

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

        if (_wearer.Update(Slot))
            Release();
        var wearer = _wearer.Current;
        if (wearer == null)
            return;

        // Held by an expression or an animation: the lids are theirs until every holder lets go, and the
        // blink starts over from open.
        bool held = (_heldOpenFrame != NeverHeld && (World?.Time?.UpdateIndex ?? 0UL) <= _heldOpenFrame + 1UL)
            || FaceHold.BlinkingHeld(Slot);
        if (held)
        {
            LetGo();
            _blinkElapsed = -1f;
            LeftShut = RightShut = 0f;
            return;
        }

        if (!_resolved)
        {
            _retry -= delta;
            if (_retry <= 0f)
                ResolveTargets();
        }
        if (_targets.Count == 0 && _lookTargets.Count == 0 && _bones.Count == 0)
            return;

        // Real eye tracking drives the lids directly (blink = 1 - openness); otherwise blink on a timer.
        if (_eyeTracking != null && _eyeTracking.IsDestroyed)
            _eyeTracking = null;
        _eyeTracking ??= wearer.GetRegisteredComponent<EyeStreamManager>();
        float weight;
        float leftWeight = 0f, rightWeight = 0f;
        bool tracked = _eyeTracking != null && _eyeTracking.IsTracking;
        if (tracked)
        {
            _blinkElapsed = -1f; // keep the procedural arc idle so it doesn't fight real data
            weight = 1f - System.Math.Clamp(_eyeTracking!.Openness, 0f, 1f);
            leftWeight = 1f - System.Math.Clamp(_eyeTracking.GetOpenness(Chirality.Left), 0f, 1f);
            rightWeight = 1f - System.Math.Clamp(_eyeTracking.GetOpenness(Chirality.Right), 0f, 1f);
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
            leftWeight = rightWeight = weight;
        }
        LeftShut = leftWeight;
        RightShut = rightWeight;

        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index, side) = _targets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false; // mesh went away or our claim was lost; rescan next frame
                return;
            }
            float w = !tracked || side < 0 ? weight : side == 0 ? leftWeight : rightWeight;
            renderer.DriveBlendShapeWeight(index, w);
        }

        float look = Look();
        for (int i = 0; i < _lookTargets.Count; i++)
        {
            var (renderer, index, up) = _lookTargets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false;
                return;
            }
            renderer.DriveBlendShapeWeight(index, up ? MathF.Max(look, 0f) : MathF.Max(-look, 0f));
        }

        for (int i = 0; i < _bones.Count; i++)
        {
            var (bone, rest, lid) = _bones[i];
            if (bone == null || bone.IsDestroyed)
            {
                _resolved = false;
                return;
            }
            float shut = (lid & 1) == 0 ? leftWeight : rightWeight;
            bone.LocalRotation.SetValueSilently(LidPose(lid, rest, look, shut), change: true);
        }
    }

    // Where the eyes look up and down, -1 (all the way down) to 1 (all the way up), from the gaze beside us.
    private float Look()
    {
        if (_lookTargets.Count == 0 && _bones.Count == 0)
            return 0f;
        if (_gaze == null || _gaze.IsDestroyed)
            _gaze = Slot.GetComponent<EyeGazeDriver>() ?? Slot.GetComponentInChildren<EyeGazeDriver>();
        return _gaze is { Enabled.Value: true } gaze ? gaze.LookPitch : 0f;
    }

    private floatQ LidPose(int lid, floatQ rest, float look, float shut)
    {
        var open = Pose(LidOpen, lid, rest);
        var looked = look > 0f ? floatQ.Slerp(open, Pose(LidUp, lid, open), look)
            : look < 0f ? floatQ.Slerp(open, Pose(LidDown, lid, open), -look) : open;
        return shut > 0f ? floatQ.Slerp(looked, Pose(LidShut, lid, looked), shut) : looked;
    }

    // A pose the avatar gave, or the fallback where it gave none (a zero rotation is none).
    private static floatQ Pose(SyncFieldList<floatQ> list, int lid, floatQ fallback)
    {
        if (lid >= list.Count)
            return fallback;
        var q = list[lid];
        return MathF.Abs(q.x) + MathF.Abs(q.y) + MathF.Abs(q.z) + MathF.Abs(q.w) < 1e-6f ? fallback : q;
    }

    // Keeps the lids out of our hands this frame and the next. Called every frame by whatever wants them;
    // FaceHold does the same for holders that set and clear.
    public void HoldOpen()
    {
        _heldOpenFrame = World?.Time?.UpdateIndex ?? 0UL;
    }

    private const ulong NeverHeld = ulong.MaxValue;
    private ulong _heldOpenFrame = NeverHeld;

    // Every claimed shape back to its synced weight and every lid bone to where it rests. Rescanned when
    // driving starts again.
    private void LetGo()
    {
        foreach (var (renderer, index, _) in _targets)
        {
            if (renderer != null && !renderer.IsDestroyed)
                renderer.ReleaseBlendShape(index, this);
        }
        foreach (var (renderer, index, _) in _lookTargets)
        {
            if (renderer != null && !renderer.IsDestroyed)
                renderer.ReleaseBlendShape(index, this);
        }
        foreach (var (bone, rest, _) in _bones)
        {
            if (bone != null && !bone.IsDestroyed)
                bone.LocalRotation.SetValueSilently(rest, change: true);
        }
        _targets.Clear();
        _lookTargets.Clear();
        _bones.Clear();
        _resolved = false;
        _retry = 0f;
    }

    // And nothing of the last wearer kept.
    private void Release()
    {
        LetGo();
        _eyeTracking = null;
        _gaze = null;
        _blinkElapsed = -1f;
        _timer = 0f;
        LeftShut = RightShut = 0f;
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

    public override void OnDisabled()
    {
        base.OnDisabled();
        Release();
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.ActiveChanged -= OnSlotActiveChanged;
        Release();
        base.OnDestroy();
    }

    // The named lids first. Then, unless the avatar named everything, a NON-overlapping blink set per mesh
    // found by name: a single combined (both-eye) shape if one exists, else the left+right pair. Driving a
    // combined shape AND its left/right counterparts together stacks the eyelids past fully closed: that's
    // the "blink goes too far" overshoot. Each shape is claimed so the other face drivers leave it alone.
    private void ResolveTargets()
    {
        LetGo();

        var mesh = LidMesh.Target;
        int named = FaceShapes.Find(mesh, BlinkShape.Value);
        if (named >= 0)
            TryClaimTarget(mesh!, named, -1);
        TryClaimLook(mesh, LookUpShape.Value, true);
        TryClaimLook(mesh, LookDownShape.Value, false);

        for (int lid = 0; lid < LidBones.Count && lid < 4; lid++)
        {
            var bone = LidBones[lid];
            if (bone != null && !bone.IsDestroyed)
                _bones.Add((bone, bone.LocalRotation.Value, lid));
        }

        if (!ExactLids.Value && named < 0 && string.IsNullOrEmpty(BlinkShape.Value))
            FindByName();

        // A named shape whose mesh has not loaded its shapes yet is looked for again in a moment.
        bool missing = (!string.IsNullOrEmpty(BlinkShape.Value) && named < 0)
            || (!string.IsNullOrEmpty(LookUpShape.Value) && !_lookTargets.Exists(static t => t.Up))
            || (!string.IsNullOrEmpty(LookDownShape.Value) && !_lookTargets.Exists(static t => !t.Up));
        _resolved = !missing || mesh == null || mesh.IsDestroyed;
        _retry = 1f;
        if (_targets.Count + _lookTargets.Count + _bones.Count > 0)
            LumoraLogger.Debug($"BlinkDriver: resolved {_targets.Count} blink, {_lookTargets.Count} lid look shape(s), {_bones.Count} lid bone(s)");
    }

    private void FindByName()
    {
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
                TryClaimTarget(renderer, combined, -1);
            }
            else
            {
                if (left >= 0) TryClaimTarget(renderer, left, 0);
                if (right >= 0) TryClaimTarget(renderer, right, 1);
            }
        }
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

    private void TryClaimTarget(SkinnedMeshRenderer renderer, int index, int side)
    {
        if (renderer.ClaimBlendShape(index, this, SkinnedMeshRenderer.BlendShapePriorityBlink))
            _targets.Add((renderer, index, side));
    }

    private void TryClaimLook(SkinnedMeshRenderer? renderer, string? name, bool up)
    {
        int index = FaceShapes.Find(renderer, name);
        if (index >= 0 && renderer!.ClaimBlendShape(index, this, SkinnedMeshRenderer.BlendShapePriorityBlink))
            _lookTargets.Add((renderer, index, up));
    }
}
