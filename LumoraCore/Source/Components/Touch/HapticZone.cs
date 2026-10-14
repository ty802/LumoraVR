// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// Buzzes the hand of the person at this machine while it is inside this slot's colliders: a curtain brushed
// aside, water stirred. Intensity is the strength. With UseSpeed the strength follows how fast the hand moves
// through instead, from StartIntensity at StartSpeed to EndIntensity at EndSpeed in metres a second, shaped by
// Power, the speed eased toward the hand's at SpeedResponse a second. Local VR hands only, a short pulse at a
// time while inside; nothing runs without VR. -xlinka
[ComponentCategory("Interaction/Touch")]
[SingleInstancePerSlot]
public sealed class HapticZone : Component
{
    private const float PulseSeconds = 0.05f;

    public readonly Sync<float> Intensity;
    public readonly Sync<bool> UseSpeed;
    public readonly Sync<float> StartSpeed;
    public readonly Sync<float> EndSpeed;
    public readonly Sync<float> StartIntensity;
    public readonly Sync<float> EndIntensity;
    public readonly Sync<float> Power;
    public readonly Sync<float> SpeedResponse;

    private readonly Hand _left = new();
    private readonly Hand _right = new();

    private sealed class Hand
    {
        public float3 Last;
        public bool Seen;
        public float Speed;
        public double NextPulse;
    }

    public HapticZone()
    {
        Intensity = new Sync<float>(this, 0.5f);
        UseSpeed = new Sync<bool>(this, false);
        StartSpeed = new Sync<float>(this, 0f);
        EndSpeed = new Sync<float>(this, 1f);
        StartIntensity = new Sync<float>(this, 0f);
        EndIntensity = new Sync<float>(this, 1f);
        Power = new Sync<float>(this, 1f);
        SpeedResponse = new Sync<float>(this, 40f);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var input = Engine.Current?.InputInterface;
        if (input == null || !input.IsVRActive || delta <= 0f)
            return;
        var user = World?.LocalUser?.Root;
        var slot = Slot;
        if (user == null || slot == null || slot.IsDestroyed || !slot.IsActive)
            return;
        double now = World!.Time.TotalTime;
        Feel(slot, user.LeftHandSlot, _left, input.LeftController, delta, now);
        Feel(slot, user.RightHandSlot, _right, input.RightController, delta, now);
    }

    private void Feel(Slot slot, Slot? handSlot, Hand hand, Input.VRController? controller, float delta, double now)
    {
        if (handSlot == null || handSlot.IsDestroyed || controller == null)
        {
            hand.Seen = false;
            return;
        }
        float3 at = handSlot.GlobalPosition;
        float target = hand.Seen ? (at - hand.Last).Length / delta : 0f;
        hand.Last = at;
        hand.Seen = true;
        hand.Speed += (target - hand.Speed) * (1f - MathF.Exp(-MathF.Max(SpeedResponse.Value, 0.001f) * delta));
        if (now < hand.NextPulse || !Inside(slot, at))
            return;
        float strength = Intensity.Value;
        if (UseSpeed.Value)
        {
            float span = EndSpeed.Value - StartSpeed.Value;
            float t = span != 0f ? System.Math.Clamp((hand.Speed - StartSpeed.Value) / span, 0f, 1f) : (hand.Speed >= EndSpeed.Value ? 1f : 0f);
            t = MathF.Pow(t, MathF.Max(Power.Value, 0.01f));
            strength *= StartIntensity.Value + (EndIntensity.Value - StartIntensity.Value) * t;
        }
        strength = System.Math.Clamp(strength, 0f, 1f);
        if (strength < 0.01f)
            return;
        hand.NextPulse = now + PulseSeconds;
        controller.TriggerHaptic(strength, PulseSeconds * 1.2f, 0f);
    }

    private static bool Inside(Slot slot, float3 point)
    {
        var components = slot.Components;
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is not Collider collider || collider.IsDestroyed || !collider.Enabled.Value)
                continue;
            float3 local = slot.GlobalPointToLocal(point);
            var bounds = collider.GetLocalBounds();
            if (local.x >= bounds.Min.x && local.x <= bounds.Max.x && local.y >= bounds.Min.y && local.y <= bounds.Max.y
                && local.z >= bounds.Min.z && local.z <= bounds.Max.z)
                return true;
        }
        return false;
    }
}
