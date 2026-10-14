// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components;

// Sets a blendshape to the weight a curve gives for where Input sits: Stops are weights from 0 to 1 at
// points along Input's range, blended between and held past either end. With no Renderer it acts on every
// skinned mesh under Scope that has a shape of that name, as a switch's shape driver does.
//
// A slider decides the shape all along its range, so the shape is claimed for as long as the driver runs,
// at Priority: a gesture or a blink above it still wins, the rest pose below it gives way. -xlinka
[ComponentCategory("Rendering")]
public class BlendShapeCurveDriver : Component
{
    public readonly SyncRef<IField<float>> Input;

    public readonly SyncRef<SkinnedMeshRenderer> Renderer;

    public readonly SyncRef<Slot> Scope;

    public readonly Sync<string> BlendShape;

    public readonly SyncList<GradientStop<float>> Stops;

    public readonly Sync<int> Priority;

    // Meshes load after the avatar is built and their shapes arrive with them, so a lookup that found
    // nothing is tried again on this clock rather than every frame.
    private const float RetryInterval = 1f;

    private readonly List<(SkinnedMeshRenderer Renderer, int Index, int Shapes)> _targets = new();
    private bool _resolved;
    private float _retry;
    private bool _claimed;

    public BlendShapeCurveDriver()
    {
        Input = new SyncRef<IField<float>>(this);
        Renderer = new SyncRef<SkinnedMeshRenderer>(this);
        Scope = new SyncRef<Slot>(this);
        BlendShape = new Sync<string>(this, "");
        Stops = new SyncList<GradientStop<float>>();
        Priority = new Sync<int>(this, SkinnedMeshRenderer.BlendShapePriorityToggle);
    }

    public GradientStop<float> AddStop(float position, float weight)
    {
        var stop = Stops.Add();
        stop.Position.Value = position;
        stop.Value.Value = weight;
        return stop;
    }

    // The weight for an input, 0 with no stops.
    public float Sample(float at)
    {
        GradientStop<float>? before = null;
        GradientStop<float>? after = null;
        GradientStop<float>? first = null;
        foreach (var stop in Stops.Elements)
        {
            float position = stop.Position.Value;
            if (first == null || position < first.Position.Value)
                first = stop;
            if (position <= at)
            {
                if (before == null || position > before.Position.Value)
                    before = stop;
            }
            else if (after == null || position < after.Position.Value)
            {
                after = stop;
            }
        }
        if (before == null)
            return first?.Value.Value ?? 0f;
        if (after == null)
            return before.Value.Value;
        float span = after.Position.Value - before.Position.Value;
        if (span <= 0f)
            return before.Value.Value;
        float t = (at - before.Position.Value) / span;
        return before.Value.Value + (after.Value.Value - before.Value.Value) * t;
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
        if (Stops.Count == 0)
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

        float at = Input.Target is { } input ? input.Value : 0f;
        float weight = System.Math.Clamp(Sample(float.IsNaN(at) ? 0f : at), 0f, 1f);
        int priority = Priority.Value;
        for (int i = 0; i < _targets.Count; i++)
        {
            var (renderer, index, _) = _targets[i];
            if (renderer == null || renderer.IsDestroyed)
                continue;
            if (!renderer.ClaimBlendShape(index, this, priority))
                continue;
            renderer.DriveBlendShapeWeight(index, weight);
            _claimed = true;
        }
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
}
