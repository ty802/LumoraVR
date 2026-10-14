// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// A skinned leg beside the mapped one that ends on the same foot.
//
// Some rigs map the humanoid onto a plain proxy skeleton and skin the body to a second set of leg bones with
// their own proportions: the thigh starts lower, the knee sits elsewhere, and only the ankle and the toes land
// where the proxy's do. Whatever posed those bones in the rig's own tooling does not come with it, so left
// alone they hang at rest while the proxy walks. The solver poses them as a leg of their own: thigh and shin
// from where the skinned thigh hangs to the proxy foot's target, the same knee hinge, and the foot turned as
// the proxy foot is turned. -xlinka
public sealed class SideLeg
{
    public Slot Thigh { get; private set; } = null!;
    public Slot Knee { get; private set; } = null!;
    public Slot Foot { get; private set; } = null!;
    public Slot? Toe { get; private set; }
    // The toe is posed only when nothing else holds it: a roll or curl the rig drives from the mapped foot stays.
    public bool OwnsToe { get; private set; }
    public List<Slot> OwnBones { get; } = new();

    // The skinned foot's rest rotation against the mapped foot's, and the toe's against the skinned foot.
    public floatQ FootInMapped = floatQ.Identity;
    public floatQ ToeInFoot = floatQ.Identity;

    // Measured each solve at the live scale.
    public float ThighLength, ShinLength;

    // How far the skin around the foot bone reaches below it at rest, against the rig's scale: a fold that lays
    // the foot bone down keeps it this far off the floor.
    public float HeelPerScale;

    public string Describe()
        => $"Side: {Thigh.SlotName.Value} > {Knee.SlotName.Value} > {Foot.SlotName.Value}"
           + (Toe != null ? $" > {Toe.SlotName.Value}{(OwnsToe ? "" : " (held by the rig)")}" : "");

    private const float JointTolerance = 0.03f;
    private const float HipReach = 0.5f;
    private const float MinSegment = 0.25f;

    // restPosition and restRotation give a bone's world pose with everything below the solver's reset bones at
    // rest; bones lists every skinned bone; isHeld says whether something else drives a bone's rotation.
    public static SideLeg? Detect(HumanoidRig rig, Chirality side, IEnumerable<Slot> bones,
        Func<Slot, float3> restPosition, Func<Slot, floatQ> restRotation, Func<Slot, bool> isBone,
        Func<Slot, bool> isHeld, float3 up, float3 forward)
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

        var mapped = new HashSet<Slot>();
        foreach (var entry in rig.Bones)
            if (entry.Value.Target is { IsDestroyed: false } bone)
                mapped.Add(bone);

        float3 h = restPosition(hip), k = restPosition(knee), f = restPosition(foot);
        float leg = float3.Distance(h, k) + float3.Distance(k, f);
        if (leg < 1e-4f)
            return null;
        float tolerance = JointTolerance * leg;

        foreach (var candidate in bones)
        {
            if (!Alive(candidate) || mapped.Contains(candidate) || IsUnder(candidate, hip))
                continue;
            float3 a = restPosition(candidate);
            if (float3.Distance(a, f) > tolerance)
                continue;
            var shin = SkinnedParent(candidate, isBone);
            var thigh = shin != null ? SkinnedParent(shin, isBone) : null;
            if (shin == null || thigh == null || mapped.Contains(shin) || mapped.Contains(thigh))
                continue;
            float3 sk = restPosition(shin), sh = restPosition(thigh);
            if (float3.Distance(sh, h) > HipReach * leg)
                continue;
            if (float3.Distance(sh, sk) < MinSegment * leg || float3.Distance(sk, a) < MinSegment * leg)
                continue;
            if (float3.Dot(sh - sk, up) <= 0f || float3.Dot(sk - a, up) <= 0f)
                continue;

            var found = new SideLeg { Thigh = thigh, Knee = shin, Foot = candidate };
            found.OwnBones.Add(thigh);
            AddBetween(found.OwnBones, shin, thigh);
            found.OwnBones.Add(shin);
            AddBetween(found.OwnBones, candidate, shin);
            found.OwnBones.Add(candidate);

            Slot? toe = null;
            if (toes != null)
                toe = Nearest(candidate, restPosition(toes), restPosition, isBone, tolerance);
            toe ??= ContinuingChild(candidate, a, restPosition, isBone, forward);
            if (toe != null)
            {
                found.Toe = toe;
                found.OwnsToe = !isHeld(toe);
                found.ToeInFoot = (restRotation(candidate).Inverse * restRotation(toe)).Normalized;
                if (found.OwnsToe)
                {
                    AddBetween(found.OwnBones, toe, candidate);
                    found.OwnBones.Add(toe);
                }
            }
            found.FootInMapped = (restRotation(foot).Inverse * restRotation(candidate)).Normalized;
            return found;
        }
        return null;
    }

    private static Slot? SkinnedParent(Slot slot, Func<Slot, bool> isBone)
    {
        int guard = 0;
        for (var cursor = slot.Parent; cursor != null && guard++ < 16; cursor = cursor.Parent)
        {
            if (isBone(cursor))
                return cursor;
        }
        return null;
    }

    private static Slot? Nearest(Slot root, float3 point, Func<Slot, float3> restPosition, Func<Slot, bool> isBone, float reach)
    {
        Slot? best = null;
        float bestDistance = reach;
        var stack = new Stack<Slot>();
        foreach (var child in root.Children)
            stack.Push(child);
        int visited = 0;
        while (stack.Count > 0 && visited++ < 512)
        {
            var slot = stack.Pop();
            if (!Alive(slot))
                continue;
            if (isBone(slot))
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

    private static void AddBetween(List<Slot> into, Slot descendant, Slot ancestor)
    {
        var path = new List<Slot>();
        int guard = 0;
        for (var cursor = descendant.Parent; cursor != null && !ReferenceEquals(cursor, ancestor) && guard++ < 16; cursor = cursor.Parent)
            path.Add(cursor);
        path.Reverse();
        into.AddRange(path);
    }

    private static bool IsUnder(Slot slot, Slot ancestor)
    {
        for (var cursor = slot; cursor != null; cursor = cursor.Parent)
        {
            if (ReferenceEquals(cursor, ancestor))
                return true;
        }
        return false;
    }

    private static bool Alive(Slot? slot) => slot != null && !slot.IsDestroyed;
}
