// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;

namespace Lumora.Core.Components;

// Sets a blendshape to Weight while any of Switches is on, and hands it back to its saved weight when
// they are all off. Amount, when set, scales how far it goes (a fist held halfway, a fade), and FadeTime
// eases between the two.
//
// The shape is claimed only while the driver is on. Toggles and gestures share shapes (the pages of one
// selector all set the same "hide shirt"), and a claim held while off would starve the one that is on.
// With no Renderer it acts on every skinned mesh under Scope that has a shape of that name, which is
// what an avatar means by "turn on the hoodie": the body, the shirt and the hoodie all carry it.
//
// Invert turns it around: on while every switch is off, for a shape a switch's OFF side sets. -xlinka
[ComponentCategory("Rendering")]
public class BlendShapeSwitchDriver : Component
{
    public readonly Sync<bool> On;

    public readonly SyncRefList<IField<bool>> Switches;

    public readonly Sync<bool> Invert;

    public readonly SyncRef<IField<float>> Amount;

    public readonly SyncRef<SkinnedMeshRenderer> Renderer;

    public readonly SyncRef<Slot> Scope;

    public readonly Sync<string> BlendShape;

    public readonly Sync<float> Weight;

    public readonly Sync<float> FadeTime;

    public readonly Sync<int> Priority;

    // Meshes load after the avatar is built and their shapes arrive with them, so a lookup that found
    // nothing is tried again on this clock rather than every frame.
    private const float RetryInterval = 1f;

    private readonly List<(SkinnedMeshRenderer Renderer, int Index, int Shapes)> _targets = new();
    private bool _resolved;
    private float _retry;
    private float _blend;
    private bool _claimed;

    public BlendShapeSwitchDriver()
    {
        On = new Sync<bool>(this, false);
        Switches = new SyncRefList<IField<bool>>(this);
        Invert = new Sync<bool>(this, false);
        Amount = new SyncRef<IField<float>>(this);
        Renderer = new SyncRef<SkinnedMeshRenderer>(this);
        Scope = new SyncRef<Slot>(this);
        BlendShape = new Sync<string>(this, "");
        Weight = new Sync<float>(this, 1f);
        FadeTime = new Sync<float>(this, 0f);
        Priority = new Sync<int>(this, SkinnedMeshRenderer.BlendShapePriorityToggle);
    }

    public bool IsOn
    {
        get
        {
            if (Switches.Count == 0)
                return On.Value;
            bool any = false;
            for (int i = 0; i < Switches.Count && !any; i++)
                any = Switches[i] is { } field && field.Value;
            return any != Invert.Value;
        }
    }

    public override void OnAwake()
    {
        base.OnAwake();
        if (Slot != null)
            Slot.ActiveChanged += OnSlotActiveChanged;
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // A new shape name, mesh or scope means a new set of targets.
        Release();
        _targets.Clear();
        _resolved = false;
        _retry = 0f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        float goal = 0f;
        if (IsOn)
            goal = Amount.Target is { } amount ? System.Math.Clamp(amount.Value, 0f, 1f) : 1f;
        float fade = FadeTime.Value;
        _blend = fade > 0f ? MoveTowards(_blend, goal, delta / fade) : goal;

        if (_blend <= 0f)
        {
            Release();
            return;
        }

        if (!_resolved || TargetsChanged())
        {
            _retry -= delta;
            if (_retry > 0f)
                return;
            _retry = RetryInterval;
            Resolve();
        }

        float weight = System.Math.Clamp(Weight.Value, 0f, 1f);
        int priority = Priority.Value;
        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index, _) = _targets[i];
            if (renderer == null || renderer.IsDestroyed)
                continue;
            // A shape held by something stronger stays theirs; it is asked for again next frame, so a
            // selector page that loses the race to the page before it gets the shape once that one lets go.
            if (!renderer.ClaimBlendShape(index, this, priority))
                continue;
            float rest = renderer.GetBlendShapeWeight(index);
            renderer.DriveBlendShapeWeight(index, rest + (weight - rest) * _blend);
        }
        _claimed = true;
    }

    private bool TargetsChanged()
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, _, shapes) = _targets[i];
            if (renderer == null || renderer.IsDestroyed || renderer.BlendShapeCount != shapes)
                return true;
        }
        return false;
    }

    private void Resolve()
    {
        Release();
        _targets.Clear();
        var name = BlendShape.Value;
        if (string.IsNullOrEmpty(name))
            return;
        if (Renderer.Target is { IsDestroyed: false } single)
        {
            Add(single, name);
        }
        else
        {
            var scope = Scope.Target ?? Slot;
            if (scope != null)
                foreach (var renderer in scope.GetComponentsInChildren<SkinnedMeshRenderer>())
                    Add(renderer, name);
        }
        _resolved = _targets.Count > 0;
    }

    private void Add(SkinnedMeshRenderer renderer, string name)
    {
        int index = renderer.GetBlendShapeIndex(name);
        if (index >= 0)
            _targets.Add((renderer, index, renderer.BlendShapeCount));
    }

    private void Release()
    {
        if (!_claimed)
            return;
        foreach (var (renderer, index, _) in _targets)
        {
            if (renderer != null && !renderer.IsDestroyed)
                renderer.ReleaseBlendShape(index, this);
        }
        _claimed = false;
    }

    private static float MoveTowards(float current, float target, float step)
    {
        if (MathF.Abs(target - current) <= step)
            return target;
        return current + MathF.Sign(target - current) * step;
    }

    private void OnSlotActiveChanged(Slot slot)
    {
        if (Slot != null && !Slot.IsActive)
        {
            Release();
            _blend = 0f;
        }
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.ActiveChanged -= OnSlotActiveChanged;
        Release();
        base.OnDestroy();
    }
}
