// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components;

namespace Lumora.Godot.Hooks;

// One band, applied to every renderer under this slot. Same mechanism as the LOD group hook, minus
// the levels: the renderer is told the distance to stop drawing at and does the test itself. -xlinka
[ImplementableHook(typeof(LodDistanceCull))]
public sealed class LodDistanceCullHook : ComponentHook<LodDistanceCull>
{
    private readonly HashSet<ILodRangeTarget> _applied = new();
    private readonly List<Component> _scratch = new();
    private bool _subscribed;

    public static IHook<LodDistanceCull> Constructor() => new LodDistanceCullHook();

    public override void Initialize()
    {
        base.Initialize();
        Owner.BandInvalidated += OnBandInvalidated;
        _subscribed = true;
        ApplyChanges();
    }

    public override void ApplyChanges()
    {
        var slot = Owner.Slot;
        if (slot == null)
            return;

        bool enabled = Owner.Enabled && slot.IsActive && Owner.ScaledDistance > 0f;
        float fade = Owner.ScaledFade;
        var range = enabled
            ? new LodVisibilityRange(0f, 0f, Owner.ScaledDistance, fade, fade > 0f)
            : LodVisibilityRange.Unbounded;

        var current = new HashSet<ILodRangeTarget>();
        _scratch.Clear();
        slot.GetComponentsInChildren(_scratch);
        foreach (var component in _scratch)
        {
            if (component == null || component.IsDestroyed)
                continue;
            if (component is not IImplementable implementable || implementable.Hook is not ILodRangeTarget target)
                continue;
            target.SetLodVisibilityRange(in range);
            current.Add(target);
        }

        // Anything that was under this slot last time and is not now gets its band back, or it stays
        // culled by a component that no longer covers it.
        foreach (var target in _applied)
        {
            if (!current.Contains(target))
                target.SetLodVisibilityRange(LodVisibilityRange.Unbounded);
        }

        _applied.Clear();
        foreach (var target in current)
            _applied.Add(target);
    }

    private void OnBandInvalidated()
    {
        if (Owner is { IsDestroyed: false })
            Owner.MarkChangeDirty();
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (_subscribed && Owner != null)
        {
            Owner.BandInvalidated -= OnBandInvalidated;
            _subscribed = false;
        }

        if (!destroyingWorld)
        {
            foreach (var target in _applied)
                target.SetLodVisibilityRange(LodVisibilityRange.Unbounded);
        }
        _applied.Clear();

        base.Destroy(destroyingWorld);
    }
}
