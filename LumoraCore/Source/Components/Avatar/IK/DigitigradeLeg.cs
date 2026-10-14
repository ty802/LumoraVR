// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// One digitigrade leg as the rig has it: thigh, shin, the raised metatarsal (hock to ball) and the toes.
//
// Rigs carry it three ways, and the humanoid map hides two of them:
//   SharedFoot  the mapped Foot is the ball and the hock is an unmapped bone between it and the knee, so a
//               two-bone solve swings shin and metatarsal as one rigid piece;
//   Parallel    the mapped legs are a plain proxy chain and the skinned leg is a sibling chain that starts at
//               the same hip and meets the proxy at the knee and the ball, carried along by constraints;
//   FootOnHock  the mapped Foot sits on the hock and the mapped Toes on the ball.
// Forced is a plantigrade leg asked to stand on its toes: its foot becomes the metatarsal.
//
// Everything is read from the bind pose: a hock is a joint between knee and ball that sits behind the line
// joining them and well above the sole, with a metatarsal long enough to be one. -xlinka
public sealed class DigitigradeLeg
{
    public enum ChainKind : byte { SharedFoot, Parallel, FootOnHock, Forced }

    public ChainKind Kind { get; private set; }
    public Slot Thigh { get; private set; } = null!;
    public Slot Knee { get; private set; } = null!;
    public Slot Hock { get; private set; } = null!;
    public Slot Ball { get; private set; } = null!;
    public Slot? Toe { get; private set; }

    // Every chain bone the mapped legs do not already own, joints and the bones between them.
    public List<Slot> OwnBones { get; } = new();

    // Bind measurements, refreshed each solve at the live scale.
    public DigitigradeSolver.Shape Shape;
    public float3 BallOffsetFlat;
    public floatQ BindBallRotation = floatQ.Identity;
    public floatQ ToeInBall = floatQ.Identity;

    // Rest in the solve frame, refreshed each solve.
    public float3 RestThighDir, RestShinDir, RestMetaDir;
    public floatQ RestThighRot = floatQ.Identity, RestShinRot = floatQ.Identity, RestHockRot = floatQ.Identity;
    public float3 RestKneeToHock, RestKneeToBall;

    // What detection measured, for logs and tests: hock height over the ball and how far it sits behind the
    // knee-ball line, both as fractions of the leg.
    public float HockLift { get; private set; }
    public float HockBehind { get; private set; }
    public float MetatarsalShare { get; private set; }

    public bool SharesMappedChain => Kind != ChainKind.Parallel;

    public string Describe()
        => $"{Kind}: {Thigh.SlotName.Value} > {Knee.SlotName.Value} > {Hock.SlotName.Value} > {Ball.SlotName.Value}"
           + (Toe != null ? $" > {Toe.SlotName.Value}" : "")
           + $" (hock lift {HockLift:0.00}, behind {HockBehind:0.00}, metatarsal {MetatarsalShare:0.00})";

    // Smallest hock that counts, as fractions of the leg.
    private const float MinHockLift = 0.1f;
    private const float MinHockBehind = 0.06f;
    private const float MinMetatarsal = 0.12f;
    private const float MinShin = 0.15f;
    private const float JointTolerance = 0.04f;

    // restPosition gives a bone's world position with everything below the solver's reset bones at rest, so a
    // chain something else has posed still reads as authored. isBone says whether a slot deforms the mesh.
    public static DigitigradeLeg? Detect(HumanoidRig rig, Chirality side, Func<Slot, float3> restPosition,
        Func<Slot, bool> isBone, float3 forward, float3 up, bool force)
    {
        bool left = side == Chirality.Left;
        var hip = rig.TryGetBone(left ? BodyNode.LeftUpperLeg : BodyNode.RightUpperLeg);
        var knee = rig.TryGetBone(left ? BodyNode.LeftLowerLeg : BodyNode.RightLowerLeg);
        var foot = rig.TryGetBone(left ? BodyNode.LeftFoot : BodyNode.RightFoot);
        var toes = rig.TryGetBone(left ? BodyNode.LeftToes : BodyNode.RightToes);
        if (!Alive(hip) || !Alive(knee) || !Alive(foot))
            return null;
        if (!Alive(toes))
            toes = null;

        float3 h = restPosition(hip), k = restPosition(knee), f = restPosition(foot);
        float leg = float3.Distance(h, k) + float3.Distance(k, f);
        if (leg < 1e-4f)
            return null;

        // The hock between the mapped knee and the mapped foot.
        var between = Between(foot, knee);
        if (between != null && between.Count > 0)
        {
            var hock = MostBehind(between, k, f, restPosition, forward);
            var leg1 = TryShape(ChainKind.SharedFoot, hip, knee, hock, foot, toes != null && IsUnder(toes, foot) ? toes : null,
                restPosition, forward, up, leg);
            if (leg1 != null)
            {
                leg1.OwnBones.AddRange(between);
                return leg1;
            }
        }

        // A skinned chain beside the mapped one, meeting it at hip, knee and ball.
        var parallel = FindParallel(hip, knee, foot, toes, restPosition, isBone, forward, up, leg);
        if (parallel != null)
            return parallel;

        // The mapped foot on the hock.
        if (toes != null && IsUnder(toes, foot))
        {
            var toe = ContinuingChild(toes, restPosition(toes), restPosition, isBone, forward);
            var leg3 = TryShape(ChainKind.FootOnHock, hip, knee, foot, toes, toe, restPosition, forward, up, leg);
            if (leg3 != null)
            {
                AddBetween(leg3.OwnBones, toes, foot);
                if (toe != null)
                    leg3.OwnBones.Add(toe);
                return leg3;
            }
        }

        if (force && toes != null && IsUnder(toes, foot))
        {
            var toe = ContinuingChild(toes, restPosition(toes), restPosition, isBone, forward);
            var forced = new DigitigradeLeg
            {
                Kind = ChainKind.Forced, Thigh = hip, Knee = knee, Hock = foot, Ball = toes, Toe = toe,
            };
            AddBetween(forced.OwnBones, toes, foot);
            if (toe != null)
                forced.OwnBones.Add(toe);
            return forced;
        }
        return null;
    }

    // A plantigrade leg asked to stand on its toes, built on a chain the caller already knows: the foot becomes
    // the metatarsal and the toes the ball.
    public static DigitigradeLeg? Forced(Slot thigh, Slot knee, Slot foot, Slot? toes)
    {
        if (!Alive(thigh) || !Alive(knee) || !Alive(foot) || !Alive(toes))
            return null;
        var forced = new DigitigradeLeg { Kind = ChainKind.Forced, Thigh = thigh, Knee = knee, Hock = foot, Ball = toes! };
        AddBetween(forced.OwnBones, toes!, foot);
        forced.OwnBones.Add(toes!);
        return forced;
    }

    private static DigitigradeLeg? TryShape(ChainKind kind, Slot thigh, Slot knee, Slot hock, Slot ball, Slot? toe,
        Func<Slot, float3> restPosition, float3 forward, float3 up, float leg)
    {
        float3 k = restPosition(knee), a = restPosition(hock), b = restPosition(ball);
        float meta = float3.Distance(a, b);
        float shin = float3.Distance(k, a);
        if (meta < MinMetatarsal * leg || shin < MinShin * leg)
            return null;
        float lift = float3.Dot(a - b, up);
        if (lift < MinHockLift * leg || float3.Dot(k - a, up) <= 0f)
            return null;
        float behind = Behind(a, k, b, forward);
        float3 kneeToBall = b - k;
        if (behind < MinHockBehind * kneeToBall.Length)
            return null;
        return new DigitigradeLeg
        {
            Kind = kind, Thigh = thigh, Knee = knee, Hock = hock, Ball = ball, Toe = toe,
            HockLift = lift / leg,
            HockBehind = behind / MathF.Max(kneeToBall.Length, 1e-6f),
            MetatarsalShare = meta / leg,
        };
    }

    private static DigitigradeLeg? FindParallel(Slot hip, Slot knee, Slot foot, Slot? toes,
        Func<Slot, float3> restPosition, Func<Slot, bool> isBone, float3 forward, float3 up, float leg)
    {
        var parent = hip.Parent;
        if (parent == null)
            return null;
        float3 h = restPosition(hip), k = restPosition(knee), f = restPosition(foot);
        float tolerance = JointTolerance * leg;
        foreach (var candidate in parent.Children)
        {
            if (ReferenceEquals(candidate, hip) || !Alive(candidate) || !isBone(candidate))
                continue;
            if (float3.Distance(restPosition(candidate), h) > tolerance)
                continue;
            var shinBone = Nearest(candidate, k, restPosition, isBone, tolerance * 1.5f);
            if (shinBone == null || ReferenceEquals(shinBone, candidate))
                continue;
            var ballBone = Nearest(shinBone, f, restPosition, isBone, tolerance * 1.5f);
            if (ballBone == null || ReferenceEquals(ballBone, shinBone))
                continue;
            var between = Between(ballBone, shinBone);
            if (between == null || between.Count == 0)
                continue;
            var hock = MostBehind(between, restPosition(shinBone), restPosition(ballBone), restPosition, forward);
            float3 ballPos = restPosition(ballBone);
            Slot? toe = null;
            if (toes != null)
                toe = Nearest(ballBone, restPosition(toes), restPosition, isBone, tolerance * 1.5f, ballBone);
            toe ??= ContinuingChild(ballBone, ballPos, restPosition, isBone, forward);
            var found = TryShape(ChainKind.Parallel, candidate, shinBone, hock, ballBone, toe, restPosition, forward, up, leg);
            if (found == null)
                continue;
            found.OwnBones.Add(candidate);
            AddBetween(found.OwnBones, shinBone, candidate);
            found.OwnBones.Add(shinBone);
            found.OwnBones.AddRange(between);
            found.OwnBones.Add(ballBone);
            if (toe != null)
            {
                AddBetween(found.OwnBones, toe, ballBone);
                found.OwnBones.Add(toe);
            }
            return found;
        }
        return null;
    }

    // Perpendicular distance of a from the line k->b, on the side opposite forward.
    private static float Behind(float3 a, float3 k, float3 b, float3 forward)
    {
        float3 axis = b - k;
        if (axis.LengthSquared < 1e-10f)
            return 0f;
        axis = axis.Normalized;
        float3 v = a - k;
        float3 perp = v - axis * float3.Dot(v, axis);
        return -float3.Dot(perp, forward);
    }

    private static Slot MostBehind(List<Slot> bones, float3 k, float3 b, Func<Slot, float3> restPosition, float3 forward)
    {
        Slot best = bones[0];
        float bestBehind = float.MinValue;
        foreach (var bone in bones)
        {
            float behind = Behind(restPosition(bone), k, b, forward);
            if (behind > bestBehind)
            {
                bestBehind = behind;
                best = bone;
            }
        }
        return best;
    }

    // Descendant of root (root itself excluded unless allowed) nearest to point, within reach.
    private static Slot? Nearest(Slot root, float3 point, Func<Slot, float3> restPosition, Func<Slot, bool> isBone,
        float reach, Slot? exclude = null)
    {
        Slot? best = null;
        float bestDistance = reach;
        var stack = new Stack<Slot>();
        foreach (var child in root.Children)
            stack.Push(child);
        int visited = 0;
        while (stack.Count > 0 && visited < 4096)
        {
            var slot = stack.Pop();
            visited++;
            if (!Alive(slot))
                continue;
            if (isBone(slot) && !ReferenceEquals(slot, exclude))
            {
                float d = float3.Distance(restPosition(slot), point);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = slot;
                }
            }
            foreach (var child in slot.Children)
                stack.Push(child);
        }
        return best;
    }

    // The child bone that carries a chain on forward from parent, if there is one.
    private static Slot? ContinuingChild(Slot parent, float3 parentPos, Func<Slot, float3> restPosition,
        Func<Slot, bool> isBone, float3 forward)
    {
        Slot? best = null;
        float bestAhead = 0f;
        foreach (var child in parent.Children)
        {
            if (!Alive(child) || !isBone(child))
                continue;
            float ahead = float3.Dot(restPosition(child) - parentPos, forward);
            if (ahead > bestAhead)
            {
                bestAhead = ahead;
                best = child;
            }
        }
        return best;
    }

    // Bones strictly between descendant and ancestor, ancestor side first; null when not on one branch.
    private static List<Slot>? Between(Slot descendant, Slot ancestor)
    {
        var list = new List<Slot>();
        var cursor = descendant.Parent;
        int guard = 0;
        while (cursor != null && !ReferenceEquals(cursor, ancestor) && guard++ < 64)
        {
            list.Add(cursor);
            cursor = cursor.Parent;
        }
        if (!ReferenceEquals(cursor, ancestor))
            return null;
        list.Reverse();
        return list;
    }

    private static void AddBetween(List<Slot> into, Slot descendant, Slot ancestor)
    {
        var between = Between(descendant, ancestor);
        if (between != null)
            into.AddRange(between);
    }

    private static bool IsUnder(Slot slot, Slot ancestor)
    {
        for (var cursor = slot.Parent; cursor != null; cursor = cursor.Parent)
        {
            if (ReferenceEquals(cursor, ancestor))
                return true;
        }
        return false;
    }

    private static bool Alive(Slot? slot) => slot != null && !slot.IsDestroyed;
}
