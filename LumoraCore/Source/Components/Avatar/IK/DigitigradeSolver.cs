// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// A digitigrade leg is three segments in one plane: thigh, shin and the raised metatarsal, with the knee
// bending forward and the hock bending back. Three segments to one point leave one degree of freedom, and
// that freedom is what makes the leg read right or wrong, so it is fixed by a rule rather than left to an
// iterative solve: the knee and the hock fold TOGETHER from their rest angles, the hock by HockShare of
// every radian the knee takes, each clamped to its own range. One scalar fold then decides the whole
// shape, the span from hip to ball shrinks as it grows, and a bisection finds the fold whose span is the
// distance to the target. The folded shape is turned about the hip so its ball lands on the target.
//
// Standing at rest span gives the authored pose exactly. Compressing (a crouch, a step down) closes both
// joints, so the metatarsal leans further over the toes while the shin swings back and the knee comes
// forward, the way a digitigrade animal sinks. Reaching past rest opens both toward their extension limits.
// The answer is a pure function of the target, so it is identical every frame and on every machine. -xlinka
public static class DigitigradeSolver
{
    public struct Shape
    {
        public float Thigh;
        public float Shin;
        public float Metatarsal;
        // Radians, fold side positive: the knee folds the shin back under the thigh, the hock folds the
        // metatarsal forward from the shin.
        public float KneeRest;
        public float HockRest;
        public float KneeMin;
        public float KneeMax;
        public float HockMin;
        public float HockMax;
        public float HockShare;
    }

    public struct Result
    {
        public float3 Knee;
        public float3 Hock;
        public float3 Ball;
        public float KneeAngle;
        public float HockAngle;
        public float Fold;
        public float Span;
        public bool Reached;
    }

    private const int Samples = 48;
    private const float FloorBand = 0.06f;
    private const float CeilingBand = 0.03f;
    private const int Bisections = 24;

    public static float Total(in Shape shape) => shape.Thigh + shape.Shin + shape.Metatarsal;

    // down and forward span the leg plane (unit, perpendicular); forward is the way the knee bends.
    public static Result Solve(in Shape shape, float3 hip, float3 target, float3 down, float3 forward)
    {
        float3 normal = float3.Cross(down, forward);
        float3 toTarget = target - hip;
        if (normal.LengthSquared > 1e-10f)
        {
            normal = normal.Normalized;
            toTarget -= normal * float3.Dot(toTarget, normal);
        }
        float distance = toTarget.Length;

        // Near the closest and the farthest the fold can bring the ball, a little more distance takes a lot more
        // fold, so the joints would race. Ease the solved span onto both ends instead; a target past them leaves
        // the ball short, which the caller's stretch takes up at the far end.
        float floor = MinSpan(in shape);
        float band = floor * FloorBand;
        float solveDistance = distance;
        if (band > 1e-7f && distance < floor + band)
            solveDistance = floor + band - band * (1f - MathF.Exp(-(floor + band - distance) / band));
        float ceiling = MaxSpan(in shape);
        float give = ceiling * CeilingBand;
        if (give > 1e-7f && solveDistance > ceiling - give)
            solveDistance = ceiling - give + give * (1f - MathF.Exp(-(solveDistance - (ceiling - give)) / give));

        float sLow = MathF.Min(shape.KneeMin - shape.KneeRest, (shape.HockMin - shape.HockRest) / Share(shape));
        float sHigh = MathF.Max(shape.KneeMax - shape.KneeRest, (shape.HockMax - shape.HockRest) / Share(shape));
        sLow = MathF.Min(sLow, 0f);
        sHigh = MathF.Max(sHigh, 0f);

        float rest = Span(in shape, 0f);
        float fold = 0f;
        bool reached = solveDistance == distance;
        if (MathF.Abs(rest - solveDistance) > 1e-6f)
        {
            // Walk out from the rest shape and take the FIRST crossing, so a target that moves smoothly gets a
            // fold that moves smoothly even where the span is not monotonic near the limits.
            float end = solveDistance < rest ? sHigh : sLow;
            float previous = 0f;
            float previousSpan = rest;
            bool found = false;
            float closest = 0f;
            float closestError = MathF.Abs(rest - solveDistance);
            for (int i = 1; i <= Samples; i++)
            {
                float s = end * i / Samples;
                float span = Span(in shape, s);
                float error = MathF.Abs(span - solveDistance);
                if (error < closestError)
                {
                    closestError = error;
                    closest = s;
                }
                if ((previousSpan - solveDistance) * (span - solveDistance) <= 0f)
                {
                    float lo = previous, hi = s;
                    float fLo = previousSpan - solveDistance;
                    for (int k = 0; k < Bisections; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        float fMid = Span(in shape, mid) - solveDistance;
                        if (fMid * fLo > 0f)
                        {
                            lo = mid;
                            fLo = fMid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }
                    fold = (lo + hi) * 0.5f;
                    found = true;
                    break;
                }
                previous = s;
                previousSpan = span;
            }
            if (!found)
            {
                fold = closest;
                reached = false;
            }
        }

        float knee = KneeAt(in shape, fold);
        float hock = HockAt(in shape, fold);

        // The folded shape with the thigh straight down, then turned in the plane so its ball points at the
        // target.
        float2 k0 = Dir(0f) * shape.Thigh;
        float2 a0 = k0 + Dir(-knee) * shape.Shin;
        float2 b0 = a0 + Dir(hock - knee) * shape.Metatarsal;
        float aimShape = MathF.Atan2(b0.y, b0.x);
        float aimTarget = distance > 1e-7f
            ? MathF.Atan2(float3.Dot(toTarget, forward), float3.Dot(toTarget, down))
            : 0f;
        float thighAngle = aimTarget - aimShape;

        var result = new Result
        {
            KneeAngle = knee,
            HockAngle = hock,
            Fold = fold,
            Span = b0.Length,
        };
        result.Knee = hip + Lift(Dir(thighAngle), down, forward) * shape.Thigh;
        result.Hock = result.Knee + Lift(Dir(thighAngle - knee), down, forward) * shape.Shin;
        result.Ball = result.Hock + Lift(Dir(thighAngle - knee + hock), down, forward) * shape.Metatarsal;
        result.Reached = reached && float3.Distance(result.Ball, hip + toTarget) <= MathF.Max(Total(in shape) * 1e-3f, 1e-5f);
        return result;
    }

    // Largest span the ranges allow, both joints at their extension limits.
    public static float MaxSpan(in Shape shape)
    {
        float sLow = MathF.Min(MathF.Min(shape.KneeMin - shape.KneeRest, (shape.HockMin - shape.HockRest) / Share(shape)), 0f);
        float best = Span(in shape, 0f);
        for (int i = 1; i <= Samples; i++)
            best = MathF.Max(best, Span(in shape, sLow * i / Samples));
        return best;
    }

    // Smallest span the ranges allow, the fold closed as far as it goes.
    public static float MinSpan(in Shape shape)
    {
        float sHigh = MathF.Max(MathF.Max(shape.KneeMax - shape.KneeRest, (shape.HockMax - shape.HockRest) / Share(shape)), 0f);
        float best = Span(in shape, 0f);
        for (int i = 1; i <= Samples; i++)
            best = MathF.Min(best, Span(in shape, sHigh * i / Samples));
        return best;
    }

    // Signed fold angles of a measured leg in the plane spanned by down and forward.
    public static void MeasureAngles(float3 thigh, float3 shin, float3 metatarsal, float3 down, float3 forward,
        out float knee, out float hock)
    {
        float a1 = AngleIn(thigh, down, forward);
        float a2 = AngleIn(shin, down, forward);
        float a3 = AngleIn(metatarsal, down, forward);
        knee = WrapPi(a1 - a2);
        hock = WrapPi(a3 - a2);
    }

    public static float AngleIn(float3 direction, float3 down, float3 forward)
        => MathF.Atan2(float3.Dot(direction, forward), float3.Dot(direction, down));

    private static float Share(in Shape shape) => MathF.Max(shape.HockShare, 0.05f);

    private static float KneeAt(in Shape shape, float fold)
        => System.Math.Clamp(shape.KneeRest + fold, shape.KneeMin, shape.KneeMax);

    private static float HockAt(in Shape shape, float fold)
        => System.Math.Clamp(shape.HockRest + fold * Share(shape), shape.HockMin, shape.HockMax);

    private static float Span(in Shape shape, float fold)
    {
        float knee = KneeAt(in shape, fold);
        float hock = HockAt(in shape, fold);
        float2 end = Dir(0f) * shape.Thigh + Dir(-knee) * shape.Shin + Dir(hock - knee) * shape.Metatarsal;
        return end.Length;
    }

    // x along down, y along forward.
    private static float2 Dir(float angle) => new float2(MathF.Cos(angle), MathF.Sin(angle));

    private static float3 Lift(float2 planar, float3 down, float3 forward) => down * planar.x + forward * planar.y;

    private static float WrapPi(float a)
    {
        while (a > MathF.PI) a -= 2f * MathF.PI;
        while (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }
}
