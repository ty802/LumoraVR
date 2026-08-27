// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Components.Magnets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Springs the object back to where it started when it is let go: back under its original parent,
// back to the pose it was captured at, gliding rather than snapping.
//
// The origin is taken when the component starts and can be re-taken at any time with Capture, so a
// build can move the object, capture, and have that be the new home. A magnet that socketed the
// object on the way down keeps it - the socket is a deliberate placement and outranks going home.
//
// The glide is a local courtesy and only the final pose replicates. Every intermediate frame goes
// in through the silent setters, which mark the transform dirty for rendering without queueing a
// delta, and the real write lands on the last frame. Writing the destination up front instead would
// not work: a delta carries the field's value at flush time, so the rewind that starts the glide is
// what would ship. Remote peers therefore see the drop pose for the length of the glide and then the
// home pose, which is the same deal the magnets already make. -xlinka
[ComponentCategory("Interaction/Grabbables")]
public class GrabTransformReset : GrabEventBehaviour, ICustomInspectorUI
{
    // Empty leaves the parent alone.
    public readonly SyncRef<Slot> OriginParent;

    public readonly Sync<float3> OriginPosition;

    public readonly Sync<floatQ> OriginRotation;

    public readonly Sync<float3> OriginScale;

    // 0 snaps.
    public readonly Sync<float> SmoothTime;

    public readonly Sync<bool> ResetPosition;

    public readonly Sync<bool> ResetRotation;

    public readonly Sync<bool> ResetScale;

    public readonly Sync<bool> CaptureOnStart;

    private bool _captured;
    private bool _gliding;
    private float _elapsed;
    private Slot? _glideParent;
    private float3 _startPosition;
    private floatQ _startRotation;
    private float3 _startScale;

    public GrabTransformReset()
    {
        OriginParent = new SyncRef<Slot>(this);
        OriginPosition = new Sync<float3>(this, float3.Zero);
        OriginRotation = new Sync<floatQ>(this, floatQ.Identity);
        OriginScale = new Sync<float3>(this, float3.One);
        SmoothTime = new Sync<float>(this, 0.25f);
        ResetPosition = new Sync<bool>(this, true);
        ResetRotation = new Sync<bool>(this, true);
        ResetScale = new Sync<bool>(this, false);
        CaptureOnStart = new Sync<bool>(this, true);
    }

    public Slot? MovedSlot => Carrier?.Slot ?? Slot;

    // True while the glide is playing on this machine.
    public bool IsGliding => _gliding;

    public override void OnStart()
    {
        base.OnStart();
        if (CaptureOnStart.Value && !_captured)
            Capture();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        FinishInterruptedGlide();
    }

    public override void OnDestroy()
    {
        FinishInterruptedGlide();
        base.OnDestroy();
    }

    [SyncMethod]
    public void Capture()
    {
        var slot = MovedSlot;
        if (slot == null || slot.IsDestroyed)
            return;

        // Capturing mid-carry would record the hand's holder slot as home, which is a place that
        // stops existing the moment the user lets go.
        if (Carrier?.IsGrabbed == true)
            return;

        OriginParent.Target = slot.Parent!;
        OriginPosition.Value = slot.LocalPosition.Value;
        OriginRotation.Value = slot.LocalRotation.Value;
        OriginScale.Value = slot.LocalScale.Value;
        _captured = true;
    }

    protected override void OnReleased(Grabbable carrier)
    {
        // Same two-beat wait as the rest of the release handlers: a receiver claims immediately, a
        // socket claims a beat later, and going home is what happens when neither did.
        RunInUpdates(2, ResolveReset);
    }

    protected override void OnBehaviourUpdate(float delta)
    {
        if (_gliding)
            StepGlide(delta);
    }

    private void ResolveReset()
    {
        if (IsDestroyed || !Enabled.Value)
            return;

        var slot = MovedSlot;
        if (slot == null || slot.IsDestroyed)
            return;
        if (Carrier?.IsGrabbed == true)
            return;
        if (MagnetHelper.IsSocketed(slot))
            return;

        var parent = OriginParent.Target;
        if (parent != null && !parent.IsDestroyed && !ReferenceEquals(slot.Parent, parent)
            && ReparentGuard.CanReparent(slot, parent))
        {
            slot.SetParent(parent, preserveGlobalTransform: true);
        }

        BeginGlide(slot);
    }

    private void BeginGlide(Slot slot)
    {
        _glideParent = slot.Parent;
        _startPosition = slot.LocalPosition.Value;
        _startRotation = slot.LocalRotation.Value;
        _startScale = slot.LocalScale.Value;

        float duration = SmoothTime.Value;
        if (duration <= 0f)
        {
            _gliding = false;
            WriteHomePose();
            return;
        }

        _elapsed = 0f;
        _gliding = true;
    }

    private void StepGlide(float delta)
    {
        var slot = MovedSlot;
        if (slot == null || slot.IsDestroyed || !ReferenceEquals(slot.Parent, _glideParent)
            || Carrier?.IsGrabbed == true)
        {
            // Something else owns the pose now (picked back up, or moved out from under us). Its
            // business, not ours.
            _gliding = false;
            return;
        }

        _elapsed += delta;
        float duration = SmoothTime.Value;
        float t = duration <= 0f ? 1f : System.Math.Clamp(_elapsed / duration, 0f, 1f);
        if (t >= 1f)
        {
            _gliding = false;
            WriteHomePose();
            return;
        }

        float eased = t * t * (3f - 2f * t);
        if (ResetPosition.Value)
            slot.LocalPosition.SetValueSilently(float3.Lerp(_startPosition, OriginPosition.Value, eased), change: true);
        if (ResetRotation.Value)
            slot.LocalRotation.SetValueSilently(floatQ.Slerp(_startRotation, OriginRotation.Value, eased), change: true);
        if (ResetScale.Value)
            slot.LocalScale.SetValueSilently(float3.Lerp(_startScale, OriginScale.Value, eased), change: true);
    }

    // A glide that stops early still has to post the answer on its way out, or every other peer is
    // left looking at the drop pose forever - the settled write is the only one that replicates.
    private void FinishInterruptedGlide()
    {
        if (!_gliding)
            return;
        _gliding = false;
        var slot = MovedSlot;
        if (slot != null && !slot.IsDestroyed && ReferenceEquals(slot.Parent, _glideParent))
            WriteHomePose();
    }

    private void WriteHomePose()
    {
        var slot = MovedSlot;
        if (slot == null || slot.IsDestroyed)
            return;
        if (ResetPosition.Value)
            slot.LocalPosition.Value = OriginPosition.Value;
        if (ResetRotation.Value)
            slot.LocalRotation.Value = OriginRotation.Value;
        if (ResetScale.Value)
            slot.LocalScale.Value = OriginScale.Value;
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Home", OriginParent.Target?.SlotName.Value ?? "same parent");
        InspectorStats.AddRow(ui, "Captured", _captured ? "yes" : "not yet");
        InspectorStats.AddRow(ui, "State", _gliding ? "gliding home" : "idle");
        var position = OriginPosition.Value;
        InspectorStats.AddRow(ui, "Origin", $"{position.x:0.##}, {position.y:0.##}, {position.z:0.##}");
    }
}
