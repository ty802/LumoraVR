// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;

namespace Lumora.Core.Components;

// Keeps one mesh's shapes where another mesh's are: a shirt that follows the body's belly and chest sliders,
// so the body never pokes through it. Each frame every linked shape on Target is set to the weight its
// partner on Source shows, whatever moved it there (a switch, a dial, a gesture, the face).
//
// AllShapes links every shape the two share by name (IgnoreCase for exporters that disagree on case), minus
// Excluded. SourceShapes and TargetShapes pair names that differ, entry by entry, and with AllShapes off they
// are the whole list. A link only ever drives Target, under any switch that sets the same shape on it
// directly. -xlinka
[ComponentCategory("Rendering")]
public class BlendShapeLink : Component
{
    public readonly SyncRef<SkinnedMeshRenderer> Source;

    public readonly SyncRef<SkinnedMeshRenderer> Target;

    public readonly Sync<bool> AllShapes;

    public readonly Sync<bool> IgnoreCase;

    public readonly SyncFieldList<string> SourceShapes;

    public readonly SyncFieldList<string> TargetShapes;

    public readonly SyncFieldList<string> Excluded;

    public readonly Sync<int> Priority;

    // Shapes arrive with their meshes, after the link is made.
    private const float RetryInterval = 1f;

    private readonly List<(int From, int To)> _pairs = new();
    private SkinnedMeshRenderer? _source, _target;
    private int _sourceShapes = -1, _targetShapes = -1;
    private bool _dirty = true;
    private float _retry;
    private bool _claimed;

    public BlendShapeLink()
    {
        Source = new SyncRef<SkinnedMeshRenderer>(this);
        Target = new SyncRef<SkinnedMeshRenderer>(this);
        AllShapes = new Sync<bool>(this, true);
        IgnoreCase = new Sync<bool>(this, false);
        SourceShapes = new SyncFieldList<string>();
        TargetShapes = new SyncFieldList<string>();
        Excluded = new SyncFieldList<string>();
        Priority = new Sync<int>(this, SkinnedMeshRenderer.BlendShapePriorityToggle - 2);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        _dirty = true;
        _retry = 0f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var source = Source.Target;
        var target = Target.Target;
        if (source == null || source.IsDestroyed || target == null || target.IsDestroyed || ReferenceEquals(source, target))
        {
            Release();
            return;
        }
        if (_dirty || !ReferenceEquals(source, _source) || !ReferenceEquals(target, _target)
            || source.BlendShapeCount != _sourceShapes || target.BlendShapeCount != _targetShapes)
        {
            _retry -= delta;
            if (_retry > 0f && !_dirty)
                return;
            _retry = RetryInterval;
            Resolve(source, target);
        }

        int priority = Priority.Value;
        for (int i = 0; i < _pairs.Count; i++)
        {
            var (from, to) = _pairs[i];
            if (!target.ClaimBlendShape(to, this, priority))
                continue;
            target.DriveBlendShapeWeight(to, source.GetEffectiveBlendShapeWeight(from));
        }
        _claimed = _pairs.Count > 0;
    }

    private void Resolve(SkinnedMeshRenderer source, SkinnedMeshRenderer target)
    {
        Release();
        _pairs.Clear();
        _source = source;
        _target = target;
        _sourceShapes = source.BlendShapeCount;
        _targetShapes = target.BlendShapeCount;
        _dirty = false;
        var comparer = IgnoreCase.Value ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var targetIndex = new Dictionary<string, int>(comparer);
        for (int i = 0; i < _targetShapes; i++)
            if (target.BlendShapeName(i) is { Length: > 0 } name)
                targetIndex.TryAdd(name, i);
        var sourceIndex = new Dictionary<string, int>(comparer);
        for (int i = 0; i < _sourceShapes; i++)
            if (source.BlendShapeName(i) is { Length: > 0 } name)
                sourceIndex.TryAdd(name, i);

        var taken = new HashSet<int>();
        int named = System.Math.Min(SourceShapes.Count, TargetShapes.Count);
        for (int i = 0; i < named; i++)
        {
            var to = string.IsNullOrEmpty(TargetShapes[i]) ? SourceShapes[i] : TargetShapes[i];
            if (sourceIndex.TryGetValue(SourceShapes[i] ?? "", out int a) && targetIndex.TryGetValue(to ?? "", out int b) && taken.Add(b))
                _pairs.Add((a, b));
        }
        if (!AllShapes.Value)
            return;
        var excluded = new HashSet<string>(comparer);
        for (int i = 0; i < Excluded.Count; i++)
            if (!string.IsNullOrEmpty(Excluded[i]))
                excluded.Add(Excluded[i]);
        foreach (var (name, a) in sourceIndex)
            if (!excluded.Contains(name) && targetIndex.TryGetValue(name, out int b) && taken.Add(b))
                _pairs.Add((a, b));
    }

    private void Release()
    {
        if (!_claimed)
            return;
        if (_target != null && !_target.IsDestroyed)
            foreach (var (_, to) in _pairs)
                _target.ReleaseBlendShape(to, this);
        _claimed = false;
    }

    public override void OnDisabled()
    {
        Release();
        base.OnDisabled();
    }

    public override void OnDestroy()
    {
        Release();
        base.OnDestroy();
    }
}
