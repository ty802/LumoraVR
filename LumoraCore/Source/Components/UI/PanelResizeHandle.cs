// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Corner drag that resizes a panel like a desktop window: the opposite corner stays put, and a size
// within a few units of the standard one snaps back to it so a stretched panel can be put back exactly.
[ComponentCategory("UI")]
public sealed class PanelResizeHandle : InteractionElement
{
    public readonly SyncRef<PanelShell> Panel = new();
    public readonly Sync<float2> Minimum = new(new float2(850, 650));
    public readonly Sync<float2> Standard = new(new float2(1000, 1360));
    private const float SnapDistance = 20f;
    private const float MaximumSide = 3000f;
    private float2 _size;
    private float3 _point, _position, _scale;
    private floatQ _rotation;
    private float2? _pending;

    protected override void OnPress(in UIInteractionContext context)
    {
        var panel = Panel.Target;
        if (panel == null) return;
        _size = panel.Size.Value;
        _point = context.WorldPoint;
        _position = panel.Slot.GlobalPosition;
        _rotation = panel.Slot.GlobalRotation;
        _scale = panel.Slot.GlobalScale;
        _pending = null;
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        var panel = Panel.Target;
        if (panel == null || panel.Slot.GetComponent<PanelWorkspace>()?.PositionLocked.Value == true) return;
        var delta = _rotation.Inverse * (context.WorldPoint - _point);
        var wanted = _size + new float2(delta.x / _scale.x, -delta.y / _scale.y);
        wanted.x = System.Math.Clamp(wanted.x, Minimum.Value.x, MaximumSide);
        wanted.y = System.Math.Clamp(wanted.y, Minimum.Value.y, MaximumSide);
        if (System.Math.Abs(wanted.x - Standard.Value.x) < SnapDistance) wanted.x = Standard.Value.x;
        if (System.Math.Abs(wanted.y - Standard.Value.y) < SnapDistance) wanted.y = Standard.Value.y;
        _pending = wanted;
        ApplyPending(force: false);
    }

    protected override void OnRelease(in UIInteractionContext context) => ApplyPending(force: true);

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (_pending.HasValue)
            ApplyPending(force: false);
    }

    // One size per finished canvas rebuild. A drag writes every frame, each write is a full relayout plus
    // a re-mesh of everything that stretches, and stacking them faster than the canvas can build them is
    // what left half the panel drawn at one size and half at another. The release always lands.
    private void ApplyPending(bool force)
    {
        var panel = Panel.Target;
        if (!_pending.HasValue || panel == null || panel.IsDestroyed)
            return;
        if (!force && Slot.GetComponentInParents<Canvas>() is { IsRebuildInProgress: true })
            return;
        var wanted = _pending.Value;
        _pending = null;
        if (wanted == panel.Size.Value)
            return;
        panel.Size.Value = wanted;
        // The pivot is the centre, so half the growth goes to each side; shifting by that half keeps the
        // top-left corner nailed where it was.
        var change = wanted - _size;
        panel.Slot.GlobalPosition = _position + _rotation * new float3(change.x * .5f * _scale.x, -change.y * .5f * _scale.y, 0);
    }
}
