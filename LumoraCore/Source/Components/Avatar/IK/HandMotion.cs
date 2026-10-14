// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// An untracked arm as a pendulum on the shoulder. The legs say where the swing wants to be (the left arm
// forward while the right foot is), a damped spring follows that with the lag and settle of a real arm, and
// only ACCELERATION of the body pushes it off its line. The old model pushed the hand with the body's
// VELOCITY, so anything moving at a steady 5 m/s carried a constant force that pinned both hands against
// the reach wall behind the hips and held them there, swing gone, for as long as the walk lasted. A body
// at a steady speed hangs its arms where they would hang standing still; only a change of speed moves
// them. -xlinka
public struct HandMotion
{
    // Shoulder flexion in radians, forward positive.
    public float Swing;
    public float SwingVelocity;

    // Inertial drift of the hand off its pose, in the arm's own frame (x out, y up, z forward), arm-reach units.
    public float3 Offset;
    public float3 Velocity;

    public const float MaxSwing = 1.2f;
    public const float MaxOffset = 0.15f;

    public void Reset()
    {
        Swing = 0f;
        SwingVelocity = 0f;
        Offset = float3.Zero;
        Velocity = float3.Zero;
    }

    //   targetSwing  the swing the gait asks for, radians
    //   frequency    Hz the arm settles at; the caller keeps it above the stride rate so the swing keeps its phase
    //   damping      ratio, 1 = no overshoot
    //   accel        body acceleration in the arm's frame (out, up, forward), m/s^2
    //   reach        arm length in metres, for the pendulum and the offset units
    //   restOffset   where the drift settles, arm-reach units (raised and out while airborne)
    public void Step(float targetSwing, float frequency, float damping, in float3 accel, float reach,
        in float3 restOffset, float dt)
    {
        if (dt <= 0f || reach <= 1e-4f)
            return;
        // A spring integrated at a hitch's 4 Hz in one step explodes, so long frames go in pieces.
        int steps = dt > 0.02f ? System.Math.Min(6, (int)MathF.Ceiling(dt / 0.02f)) : 1;
        float h = dt / steps;
        float omega = MathF.PI * 2f * MathF.Max(frequency, 0.2f);
        float zeta = MathF.Max(damping, 0.05f);
        float offsetOmega = omega * 0.8f;
        float pendulum = reach * 0.55f;

        for (int i = 0; i < steps; i++)
        {
            float swingAccel = omega * omega * (targetSwing - Swing) - 2f * zeta * omega * SwingVelocity
                - accel.z / pendulum;
            SwingVelocity += swingAccel * h;
            Swing += SwingVelocity * h;
            if (MathF.Abs(Swing) > MaxSwing)
            {
                Swing = MathF.CopySign(MaxSwing, Swing);
                if (SwingVelocity * Swing > 0f)
                    SwingVelocity = 0f;
            }

            float3 drift = (restOffset - Offset) * (offsetOmega * offsetOmega) - Velocity * (2f * zeta * offsetOmega)
                - new float3(accel.x, accel.y, 0f) / reach;
            Velocity += drift * h;
            Offset += Velocity * h;
            float distance = Offset.Length;
            if (distance > MaxOffset)
            {
                float3 n = Offset / distance;
                Offset = n * MaxOffset;
                float outward = float3.Dot(Velocity, n);
                if (outward > 0f)
                    Velocity -= n * outward;
            }
        }
    }
}
