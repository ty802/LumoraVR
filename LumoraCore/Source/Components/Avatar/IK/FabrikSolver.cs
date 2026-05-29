// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// Pure IK math. No engine/Component dependencies so it stays testable and
// platform agnostic. Two solvers:
//   - FabrikChain: general N-joint forward-and-backward reaching, used for
//     the spine where joint count varies.
//   - TwoBoneIK: analytic law-of-cosines solve for a 3-joint limb (arm/leg)
//     with a pole vector controlling the elbow/knee bend direction. Cleaner
//     and more stable than FABRIK for limbs.
// - xlinka
public static class FabrikSolver
{
    private const float Epsilon = 1e-5f;

    // Solve an N-joint chain. joints[0] is the anchored root, joints[^1] is
    // the end effector pulled toward target. lengths[i] is the rest distance
    // between joints[i] and joints[i+1] (length == joints.Length - 1).
    // joints is modified in place.
    public static void SolveChain(
        float3[] joints,
        float[] lengths,
        float3 target,
        int iterations = 10,
        float tolerance = 0.001f)
    {
        int n = joints.Length;
        if (n < 2 || lengths.Length < n - 1)
            return;

        float3 root = joints[0];

        float totalLength = 0f;
        for (int i = 0; i < n - 1; i++)
            totalLength += lengths[i];

        float rootToTarget = float3.Distance(root, target);

        // Target unreachable: stretch the chain straight toward it.
        if (rootToTarget > totalLength)
        {
            for (int i = 0; i < n - 1; i++)
            {
                float r = float3.Distance(joints[i], target);
                if (r < Epsilon) continue;
                float lambda = lengths[i] / r;
                joints[i + 1] = joints[i] * (1f - lambda) + target * lambda;
            }
            return;
        }

        for (int iter = 0; iter < iterations; iter++)
        {
            // Backward: end to target, walk toward root.
            joints[n - 1] = target;
            for (int i = n - 2; i >= 0; i--)
            {
                float r = float3.Distance(joints[i + 1], joints[i]);
                if (r < Epsilon) continue;
                float lambda = lengths[i] / r;
                joints[i] = joints[i + 1] * (1f - lambda) + joints[i] * lambda;
            }

            // Forward: root back to anchor, walk toward end.
            joints[0] = root;
            for (int i = 0; i < n - 1; i++)
            {
                float r = float3.Distance(joints[i + 1], joints[i]);
                if (r < Epsilon) continue;
                float lambda = lengths[i] / r;
                joints[i + 1] = joints[i] * (1f - lambda) + joints[i + 1] * lambda;
            }

            if (float3.Distance(joints[n - 1], target) < tolerance)
                break;
        }
    }

    // Analytic 3-joint solve. root is fixed, end goes to target (clamped to
    // reach), mid (elbow/knee) bends toward pole. Returns solved mid + end.
    public static void SolveTwoBone(
        float3 root,
        float3 pole,
        float3 target,
        float upperLength,
        float lowerLength,
        out float3 mid,
        out float3 end)
    {
        float3 toTarget = target - root;
        float reach = upperLength + lowerLength;
        float dist = toTarget.Length;

        // Clamp so the law of cosines stays valid (no fully-straight / inside-out).
        float clampedDist = MathF.Max(Epsilon, MathF.Min(dist, reach - Epsilon));
        float3 dir = dist > Epsilon ? toTarget / dist : float3.Backward;

        // Angle at the root between the upper bone and the root->target line.
        float cosRoot = (upperLength * upperLength + clampedDist * clampedDist - lowerLength * lowerLength)
                        / (2f * upperLength * clampedDist);
        cosRoot = MathF.Max(-1f, MathF.Min(1f, cosRoot));
        float angleRoot = MathF.Acos(cosRoot);

        // Bend axis: perpendicular to dir, in the plane that contains the pole.
        float3 poleDir = pole - root;
        float3 bendAxis = float3.Cross(dir, poleDir);
        if (bendAxis.LengthSquared < Epsilon)
        {
            // Pole degenerate/colinear: pick a stable perpendicular.
            bendAxis = float3.Cross(dir, float3.Up);
            if (bendAxis.LengthSquared < Epsilon)
                bendAxis = float3.Cross(dir, float3.Right);
        }
        bendAxis = bendAxis.Normalized;

        float3 upperDir = floatQ.AxisAngle(bendAxis, angleRoot) * dir;
        mid = root + upperDir * upperLength;

        // End sits at clamped reach along root->target so the limb never
        // over-extends past the target.
        end = root + dir * MathF.Min(dist, reach);

        // Re-anchor the lower bone exactly: keep mid, push end to lowerLength
        // from mid along (target - mid).
        float3 midToEnd = end - mid;
        float midEndLen = midToEnd.Length;
        if (midEndLen > Epsilon)
            end = mid + midToEnd / midEndLen * lowerLength;
    }

    // Rotation that turns `from` onto `to`. Both should be non-zero; they get
    // normalized internally. Used to swing a bone's rest direction onto the
    // solved direction. - xlinka
    public static floatQ FromToRotation(float3 from, float3 to)
    {
        float3 f = from.Normalized;
        float3 t = to.Normalized;
        float d = float3.Dot(f, t);

        if (d >= 1f - Epsilon)
            return floatQ.Identity;

        if (d <= -1f + Epsilon)
        {
            // Opposite: rotate 180 around any perpendicular axis.
            float3 axis = float3.Cross(float3.Up, f);
            if (axis.LengthSquared < Epsilon)
                axis = float3.Cross(float3.Right, f);
            return floatQ.AxisAngle(axis.Normalized, MathF.PI);
        }

        float3 c = float3.Cross(f, t);
        if (c.LengthSquared < Epsilon)
            return floatQ.Identity;
        float angle = MathF.Acos(MathF.Max(-1f, MathF.Min(1f, d)));
        return floatQ.AxisAngle(c.Normalized, angle);
    }
}
