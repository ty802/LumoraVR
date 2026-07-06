// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Blends two finger-pose sources A and B by a 0..1 weight. In wrist-local
/// position space the blend is a straight per-node lerp of the two positions, so
/// it composes trivially. A side is reported tracking only where both inputs track.
/// </summary>
// Position-space blending is exact here: each node's wrist-local point is
// independent, so lerping the points interpolates the pose with no quaternion
// double-cover or shortest-path concerns. A node contributes only if BOTH sources
// supply it, so a partial source can't drag missing nodes toward the origin. The
// result tracks a side only when both inputs track it - blending a tracked hand
// with an untracked one would otherwise leak the untracked rest pose in. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class HandPoseBlend : HandPoseProcessor
{
    /// <summary>First source (used at weight 0).</summary>
    public readonly SyncRef<IHandPoseSourceComponent> A = null!;

    /// <summary>Second source (used at weight 1).</summary>
    public readonly SyncRef<IHandPoseSourceComponent> B = null!;

    /// <summary>Blend factor: 0 = all A, 1 = all B.</summary>
    public readonly Sync<float> Weight = new();

    // Minimum finger nodes a side must supply to be considered usable for the blend
    // (proximal + at least one outer joint per finger is well above this floor).
    private const int MinNodesPerSide = 10;

    protected override void Evaluate()
    {
        var a = A?.Target;
        var b = B?.Target;
        if (a == null || b == null)
            return;

        float t = System.Math.Clamp(Weight.Value, 0f, 1f);
        bool carriesMetacarpals = a.TracksMetacarpals && b.TracksMetacarpals;
        SetTracksMetacarpals(carriesMetacarpals);

        foreach (var side in Sides)
        {
            // Both sources must report tracking AND actually carry enough nodes for
            // the side - a source can flag tracking while providing a partial set
            // (e.g. dropped frames). Require a sane minimum so the blend isn't run
            // off a near-empty hand.
            bool tracking = a.IsHandTracked(side) && b.IsHandTracked(side)
                && CountProvidedNodes(a, side) >= MinNodesPerSide
                && CountProvidedNodes(b, side) >= MinNodesPerSide;
            SetTracking(side, tracking);
            if (!tracking)
                continue;

            foreach (var node in HandPoseNodes.NodesOf(side))
            {
                if (a.TryGetFingerPosition(node, out var pa) &&
                    b.TryGetFingerPosition(node, out var pb))
                    Set(node, float3.Lerp(pa, pb, t));
            }
        }
    }
}
