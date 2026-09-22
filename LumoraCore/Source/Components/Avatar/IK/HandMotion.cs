// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// How an untracked hand moves while its owner walks, runs, jumps and stands.
//
// It used to be a sine: offset = sin(phase) along body forward, amplitude from speed. A sine is
// exactly symmetric, arrives at every extreme at the same speed, never overshoots and never lags, and
// the eye reads all of that immediately - it is the difference between an arm swinging and an arm
// being waved by a machine.
//
// A real arm is a weight on a shoulder. It gets pushed, it carries on past where it was pushed to, it
// is dragged back, and when the body changes direction it keeps going for a moment first. So this is
// a mass with drag: the gait pushes it, the body's own movement drags it along, a spring pulls it
// home, and what comes out has momentum, overshoot and lag for free. The same model covers standing
// (no push, just sway), walking, running (harder push, same spring), jumping and floating (the hands
// go somewhere else entirely and the spring pulls them there instead).
//
// One of these per hand. Integrates in the BODY frame - forward, right, up - so the maths does not
// care which way the avatar is facing, and the caller rotates the result into the world. -xlinka
public struct HandMotion
{
    // Where the hand sits relative to where it would hang, in body-frame metres per unit of arm reach.
    public float3 Offset;
    public float3 Velocity;

    // A hand cannot be flung further than this from its hang, as a fraction of arm reach. Without it a
    // sharp direction change can throw the arm out past the shoulder's own reach and the solver spends
    // the next frames dragging it back through the body.
    public const float MaxOffset = 0.55f;

    public void Reset()
    {
        Offset = float3.Zero;
        Velocity = float3.Zero;
    }

    // One step of the simulation.
    //
    //   swingPhase   radians, advanced by distance walked; the two hands are half a cycle apart
    //   swingDrive   0..1 how hard the gait is pushing (speed-faded)
    //   bodyVelocity the body's own motion in the body frame, in arm-reach units per second
    //   restTarget   where the spring is pulling to, in arm-reach units (changes when airborne)
    //   parameters   the gait's feel
    public void Step(in HandMotionParameters p, float swingPhase, float swingDrive,
        in float3 bodyVelocity, in float3 restTarget, float dt)
    {
        if (dt <= 0f)
            return;
        // A long frame is integrated in pieces rather than in one leap, because a spring with drag
        // integrated at 4 Hz in one step is a spring that explodes.
        int steps = dt > 0.033f ? System.Math.Min(4, (int)MathF.Ceiling(dt / 0.033f)) : 1;
        float h = dt / steps;

        for (int i = 0; i < steps; i++)
            Integrate(in p, swingPhase, swingDrive, in bodyVelocity, in restTarget, h);
    }

    private void Integrate(in HandMotionParameters p, float swingPhase, float swingDrive,
        in float3 bodyVelocity, in float3 restTarget, float dt)
    {
        float3 force = float3.Zero;

        // THE PUSH. A swing is not symmetric: the arm is driven harder going forward than back, which
        // is why the forward and backward forces are two numbers rather than one signed one.
        if (swingDrive > 1e-4f)
        {
            float drive = MathF.Sin(swingPhase);
            float strength = drive >= 0f ? p.ForwardForce : p.BackwardForce;
            force.z += drive * strength * swingDrive;

            // A little of the swing goes sideways and up, tracing an arc rather than a line through
            // the hip. This is the difference between an arm swinging past a body and an arm swinging
            // through one.
            force.x += MathF.Abs(drive) * p.SwingArcOut * swingDrive;
            force.y += MathF.Cos(swingPhase * 2f) * p.SwingArcPeak * swingDrive;
        }

        // THE BODY DRAGGING THE HAND ALONG. Start walking and the hand is left behind for a moment;
        // stop and it carries on. Nothing else in the model produces that, and it is most of what
        // sells a change of speed.
        force -= bodyVelocity * p.MotionTransfer;

        // THE SPRING HOME, toward wherever the hand is supposed to be resting right now.
        force += (restTarget - Offset) * p.Spring;

        Velocity += force * dt;

        // DRAG. Exponential rather than linear so it is stable at any step size.
        Velocity *= MathF.Exp(-p.Drag * dt);

        float speed = Velocity.Length;
        if (speed > p.MaxSpeed)
            Velocity *= p.MaxSpeed / speed;

        Offset += Velocity * dt;

        float distance = Offset.Length;
        if (distance > MaxOffset)
        {
            Offset *= MaxOffset / distance;
            // Kill the outward part of the velocity at the wall, or the hand buzzes against it.
            float outward = float3.Dot(Velocity, Offset.Normalized);
            if (outward > 0f)
                Velocity -= Offset.Normalized * outward;
        }
    }
}

// The feel of one gait. Forces are in arm-reach units per second squared.
public struct HandMotionParameters
{
    public float ForwardForce;
    public float BackwardForce;
    public float Drag;
    public float Spring;
    public float MotionTransfer;
    public float MaxSpeed;
    public float SwingArcOut;
    public float SwingArcPeak;

    // A stroll. The arm barely leaves the side and the spring dominates.
    public static HandMotionParameters Walk => new()
    {
        ForwardForce = 5.5f,
        BackwardForce = 3.5f,
        Drag = 5.0f,
        Spring = 26f,
        // A moving body does leave its arms behind, but only a little: this is a lean, not a drag
        // anchor. High enough and the steady offset swamps the swing entirely and both arms simply
        // sit back, which measures as two arms that no longer oppose each other. -xlinka
        MotionTransfer = 1.2f,
        MaxSpeed = 3.0f,
        SwingArcOut = 0.35f,
        SwingArcPeak = 0.9f,
    };

    // A run. Harder push, looser spring, so the arm travels further and comes back later - which is
    // what makes a run read as a run rather than as a fast walk.
    public static HandMotionParameters Run => new()
    {
        ForwardForce = 13f,
        BackwardForce = 9f,
        Drag = 4.2f,
        Spring = 20f,
        MotionTransfer = 1.8f,
        MaxSpeed = 6.0f,
        SwingArcOut = 0.7f,
        SwingArcPeak = 2.2f,
    };

    // Off the ground. No gait to push it, heavy drag, and the spring pulls somewhere else entirely.
    public static HandMotionParameters Air => new()
    {
        ForwardForce = 0f,
        BackwardForce = 0f,
        Drag = 3.0f,
        Spring = 12f,
        MotionTransfer = 4.5f,
        MaxSpeed = 4.0f,
        SwingArcOut = 0f,
        SwingArcPeak = 0f,
    };

    public static HandMotionParameters Lerp(in HandMotionParameters a, in HandMotionParameters b, float t)
    {
        t = System.Math.Clamp(t, 0f, 1f);
        return new HandMotionParameters
        {
            ForwardForce = a.ForwardForce + (b.ForwardForce - a.ForwardForce) * t,
            BackwardForce = a.BackwardForce + (b.BackwardForce - a.BackwardForce) * t,
            Drag = a.Drag + (b.Drag - a.Drag) * t,
            Spring = a.Spring + (b.Spring - a.Spring) * t,
            MotionTransfer = a.MotionTransfer + (b.MotionTransfer - a.MotionTransfer) * t,
            MaxSpeed = a.MaxSpeed + (b.MaxSpeed - a.MaxSpeed) * t,
            SwingArcOut = a.SwingArcOut + (b.SwingArcOut - a.SwingArcOut) * t,
            SwingArcPeak = a.SwingArcPeak + (b.SwingArcPeak - a.SwingArcPeak) * t,
        };
    }
}
