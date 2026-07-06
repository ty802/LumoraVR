// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Component that stores bone slot references for a biped humanoid rig.
/// Used by the IK and other avatar systems to drive skeleton bones.
/// </summary>
[ComponentCategory("Rendering")]
public class HumanoidRig : Component
{
    public readonly struct LimbPoseGuide
    {
        public readonly bool HasEndDirection;
        public readonly float3 EndDirectionBody;
        public readonly bool HasBendDirection;
        public readonly float3 BendDirectionBody;

        public LimbPoseGuide(bool hasEndDirection, float3 endDirectionBody, bool hasBendDirection, float3 bendDirectionBody)
        {
            HasEndDirection = hasEndDirection;
            EndDirectionBody = endDirectionBody;
            HasBendDirection = hasBendDirection;
            BendDirectionBody = bendDirectionBody;
        }
    }

    /// <summary>
    /// Geometric forward axis of the rig in world space, flattened to the horizontal plane. Guessed ONCE from
    /// the rig geometry (shoulder/hand line crossed with hips->head) rather than read from a single bone's
    /// authored rotation, so it is correct even for imports whose hips/spine bones are authored facing the
    /// opposite way to the body. Null until <see cref="GuessForwardFlipped"/> runs. - xlinka
    /// </summary>
    public readonly Sync<float3?> ForwardAxis = new();

    /// <summary>
    /// True when the rig's authored hips-forward points opposite the model's geometric front. This is diagnostic
    /// state for import/setup tools; runtime body facing uses <see cref="ForwardAxis"/> instead. - xlinka
    /// </summary>
    public readonly Sync<bool> ForwardFlipped = new();

    /// <summary>
    /// Maps each rig BodyNode to its bone Slot. A real synced collection: each entry is a sub-worker
    /// with its own RefID, so the map replicates and survives slot duplication (the old local dictionary
    /// came back empty on remote peers / after a duplicate). Declared as a readonly field so the worker's
    /// member discovery finds and Initialize()s it. -xlinka
    /// </summary>
    public readonly SyncObjectDictionary<BodyNode, SyncRef<Slot>> Bones = new();

    /// <summary>
    /// Minimal set of bones required for a valid biped rig.
    /// </summary>
    public static readonly BodyNode[] RequiredBones = new BodyNode[]
    {
        BodyNode.Hips,
        BodyNode.Spine,
        BodyNode.Head,
        BodyNode.LeftUpperArm,
        BodyNode.LeftLowerArm,
        BodyNode.LeftHand,
        BodyNode.RightUpperArm,
        BodyNode.RightLowerArm,
        BodyNode.RightHand,
        BodyNode.LeftUpperLeg,
        BodyNode.LeftLowerLeg,
        BodyNode.LeftFoot,
        BodyNode.RightUpperLeg,
        BodyNode.RightLowerLeg,
        BodyNode.RightFoot
    };

    // An ordered run of body nodes that form one limb/column (hips->head, shoulder->hand, finger
    // metacarpal->tip, ...). canSkipFirst marks chains whose lead bone is commonly absent in real rigs
    // (no clavicle, no finger metacarpal); when present the chain still validates without it. The enum
    // is laid out so a chain is just the contiguous integer run [start..end]. -xlinka
    private sealed class Chain
    {
        public readonly List<BodyNode> Nodes;
        public readonly bool CanSkipFirst;

        public Chain(BodyNode start, BodyNode end, bool canSkipFirst)
        {
            CanSkipFirst = canSkipFirst;
            Nodes = new List<BodyNode>();
            for (var n = start; n <= end; n++)
                Nodes.Add(n);
        }
    }

    // The standard biped chain set. Built once. Spine column, neck->jaw, each arm, each leg, and every
    // finger. Used to disambiguate name matches by topology and to validate a classified bone against the
    // neighbors its chain requires.
    private static readonly List<Chain> BoneChains = new()
    {
        new Chain(BodyNode.Hips, BodyNode.Head, canSkipFirst: false),                                   // spine column
        new Chain(BodyNode.Neck, BodyNode.Jaw, canSkipFirst: false),                                    // neck->head->jaw
        new Chain(BodyNode.LeftShoulder, BodyNode.LeftHand, canSkipFirst: true),                        // left arm (clavicle optional)
        new Chain(BodyNode.RightShoulder, BodyNode.RightHand, canSkipFirst: true),                      // right arm
        new Chain(BodyNode.LeftUpperLeg, BodyNode.LeftToes, canSkipFirst: false),                       // left leg
        new Chain(BodyNode.RightUpperLeg, BodyNode.RightToes, canSkipFirst: false),                     // right leg
        new Chain(BodyNode.LeftThumb_Metacarpal, BodyNode.LeftThumb_Tip, canSkipFirst: false),          // thumb has its metacarpal
        new Chain(BodyNode.LeftIndexFinger_Metacarpal, BodyNode.LeftIndexFinger_Tip, canSkipFirst: true),
        new Chain(BodyNode.LeftMiddleFinger_Metacarpal, BodyNode.LeftMiddleFinger_Tip, canSkipFirst: true),
        new Chain(BodyNode.LeftRingFinger_Metacarpal, BodyNode.LeftRingFinger_Tip, canSkipFirst: true),
        new Chain(BodyNode.LeftPinky_Metacarpal, BodyNode.LeftPinky_Tip, canSkipFirst: true),
        new Chain(BodyNode.RightThumb_Metacarpal, BodyNode.RightThumb_Tip, canSkipFirst: false),
        new Chain(BodyNode.RightIndexFinger_Metacarpal, BodyNode.RightIndexFinger_Tip, canSkipFirst: true),
        new Chain(BodyNode.RightMiddleFinger_Metacarpal, BodyNode.RightMiddleFinger_Tip, canSkipFirst: true),
        new Chain(BodyNode.RightRingFinger_Metacarpal, BodyNode.RightRingFinger_Tip, canSkipFirst: true),
        new Chain(BodyNode.RightPinky_Metacarpal, BodyNode.RightPinky_Tip, canSkipFirst: true),
    };

    private readonly Dictionary<BodyNode, LimbPoseGuide> _limbPoseGuides = new();

    /// <summary>
    /// Get or set a bone slot by body node type.
    /// </summary>
    public Slot this[BodyNode boneType]
    {
        get => TryGetBone(boneType);
        set
        {
            if (value != null)
                SetBone(boneType, value);
            else
                Bones.Remove(boneType);
        }
    }

    // Point a node's entry at a bone slot, creating the entry if absent. Wraps the get-or-create-then-set
    // shape the synced map exposes (entry values are SyncRef sub-workers, not plain slots).
    private void SetBone(BodyNode boneType, Slot bone) => Bones.GetOrAdd(boneType).Target = bone;

    public bool TryGetLimbPoseGuide(BodyNode rootNode, out LimbPoseGuide guide)
        => _limbPoseGuides.TryGetValue(rootNode, out guide);

    /// <summary>
    /// Whether this rig has all minimal biped bones.
    /// </summary>
    public bool IsHumanoid
    {
        get
        {
            foreach (var node in RequiredBones)
            {
                if (!Bones.ContainsKey(node))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Whether this rig has left hand finger bones.
    /// </summary>
    public bool HasLeftFingerBones => HasMinimalHand(Chirality.Left);

    /// <summary>
    /// Whether this rig has right hand finger bones.
    /// </summary>
    public bool HasRightFingerBones => HasMinimalHand(Chirality.Right);

    // Bones is a discovered worker member (readonly field) - no OnAwake construction needed.
    // ForwardAxis default is null (C# default for float3?, skip OnInit)

    /// <summary>
    /// Try to get a bone slot for a body node type.
    /// </summary>
    public Slot TryGetBone(BodyNode boneType)
    {
        if (Bones.TryGetValue(boneType, out var reference) && reference.Target != null)
            return reference.Target;
        return null!;
    }

    /// <summary>
    /// Get the body node type for a given bone slot.
    /// </summary>
    public BodyNode GetBoneType(Slot bone)
    {
        foreach (var entry in Bones)
        {
            if (entry.Value.Target == bone)
                return entry.Key.Value;
        }
        return BodyNode.NONE;
    }

    /// <summary>
    /// Check if a body node is a required bone.
    /// </summary>
    public static bool IsRequiredBone(BodyNode node)
    {
        foreach (var required in RequiredBones)
        {
            if (required == node)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if this rig has minimal hand bones for a chirality.
    /// </summary>
    public bool HasMinimalHand(Chirality chirality)
    {
        // Need at least 2 thumb segments
        if (FingerSegmentCount(FingerType.Thumb, chirality, excludeMetacarpal: false) < 2)
            return false;

        // Need at least 2 other fingers with 2+ segments
        int fingerCount = 0;
        for (var finger = FingerType.Index; finger <= FingerType.Pinky; finger++)
        {
            if (FingerSegmentCount(finger, chirality, excludeMetacarpal: true) >= 2)
                fingerCount++;
        }

        return fingerCount >= 2;
    }

    /// <summary>
    /// Count segments for a finger.
    /// </summary>
    public int FingerSegmentCount(FingerType finger, Chirality chirality, bool excludeMetacarpal)
    {
        int count = 0;
        foreach (var entry in Bones)
        {
            var node = entry.Key.Value;
            if (!node.IsFinger())
                continue;
            if (node.GetChirality() != chirality)
                continue;
            if (excludeMetacarpal && node.GetFingerSegmentType() == FingerSegmentType.Metacarpal)
                continue;
            if (node.GetFingerType() == finger)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Get list of missing bones for a valid biped rig.
    /// </summary>
    public void GetMissingBipedBones(List<BodyNode> list)
    {
        foreach (var node in RequiredBones)
        {
            if (!Bones.ContainsKey(node))
                list.Add(node);
        }
    }

    /// <summary>
    /// Populate bones from a SkeletonBuilder by matching bone names, using the slot hierarchy to resolve
    /// names the string heuristic alone can't (an "arm" with no upper/lower, a duplicated spine/chest).
    /// Two passes: (1) name-classify every bone, recording whether the match was ambiguous; (2) walk each
    /// bone root-to-tip, disambiguate an ambiguous match against its already-resolved parent's chain, drop
    /// a bone whose chain demands neighbors that aren't present, and assign. Fingers are name-classified
    /// here and refined geometrically by <see cref="DetectHandRigs"/> afterward. -xlinka
    /// </summary>
    public void PopulateFromSkeleton(SkeletonBuilder skeleton)
    {
        if (skeleton == null || !skeleton.IsBuilt.Value)
        {
            LumoraLogger.Warn("HumanoidRig: Cannot populate from null or unbuilt skeleton");
            return;
        }

        _limbPoseGuides.Clear();

        // Pass 1: name-classify each bone slot, remembering ambiguity so pass 2 can override it.
        var slots = new List<Slot>();
        var classified = new Dictionary<Slot, BodyNode>();
        var ambiguous = new Dictionary<Slot, bool>();
        for (int i = 0; i < skeleton.BoneCount; i++)
        {
            var boneSlot = skeleton.BoneSlots[i];
            if (boneSlot == null)
                continue;
            var node = ClassifyBoneName(skeleton.BoneNames[i], out bool amb);
            slots.Add(boneSlot);
            classified[boneSlot] = node;
            ambiguous[boneSlot] = amb;
        }

        // Pass 2: resolve ambiguity by topology, then assign root-to-tip so a parent type is settled
        // before its children consult it. Sort by hierarchy depth so parents come first.
        slots.Sort((a, b) => SlotDepth(a).CompareTo(SlotDepth(b)));

        foreach (var slot in slots)
        {
            var node = classified[slot];
            if (node == BodyNode.NONE)
                continue;

            if (ambiguous[slot])
            {
                var parentNode = NearestClassifiedAncestor(slot, classified);
                node = FixChain(node, parentNode);
                classified[slot] = node;   // store the corrected type for descendants to read
                if (node == BodyNode.NONE)
                    continue;
            }

            // Don't let a second match for an already-filled node clobber the first (handles a leftover
            // duplicate the chain fix didn't move). Required-neighbor validation rejects a stray.
            if (Bones.ContainsKey(node))
                continue;
            if (!ChainNeighborsSatisfied(slot, node, classified))
                continue;

            SetBone(node, slot);
        }

        DetectHandRigs();
        CaptureLimbPoseGuides(overwrite: true);

        LumoraLogger.Log($"HumanoidRig: Populated {Bones.Count} bones from skeleton, IsHumanoid={IsHumanoid}");
    }

    // Depth of a slot below the skeleton root (parent-walk length). Used to order classification so a
    // bone's parent is resolved first.
    private static int SlotDepth(Slot slot)
    {
        int depth = 0;
        var p = slot.Parent;
        while (p != null && depth < 256)
        {
            depth++;
            p = p.Parent;
        }
        return depth;
    }

    // Walk up the slot hierarchy and return the body type of the nearest ancestor that pass 1/2 classified
    // to something real. NONE when no classified ancestor exists.
    private static BodyNode NearestClassifiedAncestor(Slot slot, Dictionary<Slot, BodyNode> classified)
    {
        var p = slot.Parent;
        int guard = 0;
        while (p != null && guard++ < 256)
        {
            if (classified.TryGetValue(p, out var node) && node != BodyNode.NONE)
                return node;
            p = p.Parent;
        }
        return BodyNode.NONE;
    }

    // Reject a classified bone when its chain requires a REQUIRED neighbor (e.g. a lower-arm needs an
    // upper-arm above it) that no ancestor/descendant provides. Optional bones (shoulder, toes, metacarpal,
    // neck, chest) never gate. Keeps a stray "hand"-named prop from registering as a real wrist.
    private bool ChainNeighborsSatisfied(Slot slot, BodyNode node, Dictionary<Slot, BodyNode> classified)
    {
        var chain = GetChain(node, out int index);
        if (chain == null)
            return true;

        // Every required predecessor in the chain must appear on the ancestor path.
        for (int i = index - 1; i >= 0; i--)
        {
            var need = chain.Nodes[i];
            if (IsRequiredBone(need) && !AncestorHasType(slot, need, classified))
                return false;
        }
        return true;
    }

    private static bool AncestorHasType(Slot slot, BodyNode type, Dictionary<Slot, BodyNode> classified)
    {
        var p = slot.Parent;
        int guard = 0;
        while (p != null && guard++ < 256)
        {
            if (classified.TryGetValue(p, out var node) && node == type)
                return true;
            p = p.Parent;
        }
        return false;
    }

    // Find the chain that contains a body node, plus the node's index within it. Returns null when the
    // node belongs to no chain (e.g. View/Root/eyes).
    private static Chain? GetChain(BodyNode node, out int index)
    {
        foreach (var chain in BoneChains)
        {
            index = chain.Nodes.IndexOf(node);
            if (index >= 0)
                return chain;
        }
        index = -1;
        return null;
    }

    /// <summary>
    /// Correct an ambiguous match by walking the chain past the parent. Given a child whose name only said
    /// "arm" (assumed upper) but whose parent is already an upper-arm, the child must be the next link
    /// (lower-arm). If the parent isn't in the child's chain, the child resets to the chain's first real
    /// bone. Returns NONE when the chain is exhausted (deeper than its tip).
    /// </summary>
    public static BodyNode FixChain(BodyNode childNode, BodyNode parentNode)
    {
        var chain = GetChain(childNode, out int childIndex);
        if (chain == null)
            return childNode;

        int parentIndex = chain.Nodes.IndexOf(parentNode);
        if (parentIndex < 0)
        {
            // Parent isn't on this chain: the child must be the chain's lead bone (or the second, when the
            // lead is the optional skip bone).
            return chain.Nodes[chain.CanSkipFirst ? 1 : 0];
        }

        // Child can't sit at or above its parent in the same chain; advance it past the parent.
        if (childIndex <= parentIndex)
            childIndex = parentIndex + 1;
        if (childIndex >= chain.Nodes.Count)
            return BodyNode.NONE;
        return chain.Nodes[childIndex];
    }

    /// <summary>
    /// Guess the rig's forward axis GEOMETRICALLY and store it in <see cref="ForwardAxis"/> (world space,
    /// flattened). Forward is the shoulder/hand left->right line crossed with the hips->head up line, picked
    /// once from the rig's current pose so it can't drift with the solve. The sign comes purely from the bone
    /// POSITIONS (left/right line x hips->head), never from a bone's authored rotation. This is what makes the
    /// body-facing robust across rigs whose hips/spine bones are authored backward (common for imported anthro
    /// models) - the old code read the hips bone's rotation directly and turned the whole body 180 deg when that
    /// bone faced the wrong way. -xlinka
    /// </summary>
    public void GuessForwardFlipped()
    {
        if (!IsHumanoid)
            return;
        var fwd = GuessForwardAxis();
        if (!fwd.HasValue)
            return;
        ForwardAxis.Value = fwd.Value;

        // Back-authored when the hips bone's authored forward points opposite the true geometric front. Store the
        // sign for diagnostics, but keep runtime facing driven by geometry instead of trusting this bone. - xlinka
        var hips = TryGetBone(BodyNode.Hips);
        if (hips == null)
            return;
        float3 hipsFwd = hips.GlobalRotation * float3.Backward;
        hipsFwd.y = 0f;
        if (hipsFwd.LengthSquared > 1e-6f)
            ForwardFlipped.Value = float3.Dot(fwd.Value, hipsFwd.Normalized) < 0f;
    }

    /// <summary>
    /// Compute the geometric forward axis (world space, flattened, normalized) without storing it. Right is the
    /// body left->right line (upper-arm roots, else shoulders, else hands); up is hips->head. forward = up x
    /// right (right-handed: the solver uses right = forward x up, which inverts to forward = up x right). The
    /// sign is fully determined by the rig's bone POSITIONS and its classified left/right bone assignment, so
    /// it never reads a bone's authored ROTATION - that is the whole point: it stays correct on imports whose
    /// hips/spine/head bones are authored facing backward. Returns null when the rig is too incomplete to
    /// measure (caller falls back). -xlinka
    /// </summary>
    public float3? GuessForwardAxis()
    {
        var hips = TryGetBone(BodyNode.Hips);
        var head = TryGetBone(BodyNode.Head);
        if (hips == null || head == null)
            return null;

        // Left->right line: prefer the upper-arm roots (stable, always spread), then the shoulders, then the
        // hands (which can sit together at rest and read poorly).
        if (!TryRightLine(BodyNode.LeftUpperArm, BodyNode.RightUpperArm, out float3 right)
            && !TryRightLine(BodyNode.LeftShoulder, BodyNode.RightShoulder, out right)
            && !TryRightLine(BodyNode.LeftHand, BodyNode.RightHand, out right))
            return null;

        float3 up = head.GlobalPosition - hips.GlobalPosition;
        if (up.LengthSquared < 1e-8f)
            up = float3.Up;
        up = up.Normalized;

        // forward = up x right. Flatten to the horizontal plane (body facing is a yaw). The cross of two real
        // position-derived lines has a deterministic sign - no rotation-based tiebreak needed (and none wanted:
        // consulting an authored bone rotation here would reintroduce the backward-bone bug). -xlinka
        float3 forward = float3.Cross(up, right);
        forward.y = 0f;
        if (forward.LengthSquared < 1e-8f)
            return null;
        forward = forward.Normalized;

        // Resolve the forward SIGN against the toes: foot->toe points to the avatar's real front and is
        // unaffected by the arm L/R labels, so it disambiguates the Cross(up, right) sign. A rig whose
        // left/right arm bones are authored on the swapped physical side (the fox) gives a BACKWARD
        // Cross(up, right); the toe direction catches and flips it. No-op for correctly-built rigs (the label
        // forward already agrees with the toes) and for toeless rigs (falls through, runtime view-anchoring
        // still corrects the solver). -xlinka
        if (TryToeForward(out float3 toeForward) && float3.Dot(forward, toeForward) < 0f)
            forward = -forward;

        return SnapForwardToRootAxis(forward);
    }

    // Horizontal foot->toe direction, averaged over both feet. The toes point to the avatar's geometric front
    // regardless of which physical side each leg is labeled, so this is a label-swap-proof forward-sign anchor.
    private bool TryToeForward(out float3 dir)
    {
        dir = float3.Zero;
        float3 sum = float3.Zero;
        Accumulate(BodyNode.LeftFoot, BodyNode.LeftToes);
        Accumulate(BodyNode.RightFoot, BodyNode.RightToes);
        sum.y = 0f;
        if (sum.LengthSquared < 1e-8f)
            return false;
        dir = sum.Normalized;
        return true;

        void Accumulate(BodyNode footNode, BodyNode toeNode)
        {
            var foot = TryGetBone(footNode);
            var toe = TryGetBone(toeNode);
            if (foot == null || toe == null || foot.IsDestroyed || toe.IsDestroyed)
                return;
            float3 d = toe.GlobalPosition - foot.GlobalPosition;
            d.y = 0f;
            if (d.LengthSquared > 1e-8f)
                sum += d.Normalized;
        }
    }

    private float3 SnapForwardToRootAxis(float3 forward)
    {
        if (Slot == null || Slot.IsDestroyed || forward.LengthSquared < 1e-8f)
            return forward;

        float3 best = forward;
        float bestDot = -1f;
        TryAxis(float3.Forward);
        TryAxis(float3.Backward);
        TryAxis(float3.Left);
        TryAxis(float3.Right);

        // Only snap when the geometric front is already close to a root-cardinal axis. Otherwise keep the measured
        // direction so unusual-but-valid diagonal rigs do not get forced sideways.
        return bestDot > 0.82f ? best : forward;

        void TryAxis(float3 localAxis)
        {
            float3 axis = Slot.LocalDirectionToGlobal(localAxis);
            axis.y = 0f;
            if (axis.LengthSquared < 1e-8f)
                return;
            axis = axis.Normalized;
            float dot = float3.Dot(axis, forward);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = axis;
            }
        }
    }

    // Horizontal-flattened normalized line from the left node's bone to the right node's bone, or false when
    // either bone is missing / the points coincide.
    private bool TryRightLine(BodyNode left, BodyNode right, out float3 dir)
    {
        dir = float3.Right;
        var l = TryGetBone(left);
        var r = TryGetBone(right);
        if (l == null || r == null)
            return false;
        float3 line = r.GlobalPosition - l.GlobalPosition;
        line.y = 0f;
        if (line.LengthSquared < 1e-8f)
            return false;
        dir = line.Normalized;
        return true;
    }

    /// <summary>
    /// Force the rig into a canonical T-pose: arms straight out to the sides, legs straight down. Rotates each
    /// limb bone so the direction to its child matches the target, processing root-to-tip so a parent's rotation
    /// carries into its children before they're adjusted. Run this before IK captures the rest pose so calibration
    /// starts from a known pose no matter how the model was authored (A-pose, relaxed, etc.). -xlinka
    /// </summary>
    public void MakeTPose()
    {
        CaptureLimbPoseGuides(overwrite: false);

        var fwd = ForwardAxis.Value ?? GuessForwardAxis() ?? float3.Backward;
        fwd.y = 0f;
        fwd = fwd.LengthSquared > 1e-6f ? fwd.Normalized : float3.Backward;

        var right = float3.Cross(fwd, float3.Up);
        right = right.LengthSquared > 1e-6f ? right.Normalized : float3.Right;
        var left = -right;
        var down = new float3(0f, -1f, 0f);

        AlignBone(BodyNode.LeftUpperArm, BodyNode.LeftLowerArm, left);
        AlignBone(BodyNode.LeftLowerArm, BodyNode.LeftHand, left);
        AlignBone(BodyNode.RightUpperArm, BodyNode.RightLowerArm, right);
        AlignBone(BodyNode.RightLowerArm, BodyNode.RightHand, right);
        AlignBone(BodyNode.LeftUpperLeg, BodyNode.LeftLowerLeg, down);
        AlignBone(BodyNode.LeftLowerLeg, BodyNode.LeftFoot, down);
        AlignBone(BodyNode.RightUpperLeg, BodyNode.RightLowerLeg, down);
        AlignBone(BodyNode.RightLowerLeg, BodyNode.RightFoot, down);

        LumoraLogger.Log("HumanoidRig: Applied T-pose normalization.");
    }

    private void CaptureLimbPoseGuides(bool overwrite)
    {
        if (!overwrite && _limbPoseGuides.Count > 0)
            return;

        var fwd = ForwardAxis.Value ?? GuessForwardAxis();
        if (!fwd.HasValue || fwd.Value.LengthSquared < 1e-6f)
            return;

        float3 forward = fwd.Value;
        forward.y = 0f;
        if (forward.LengthSquared < 1e-6f)
            return;
        forward = forward.Normalized;

        float3 right = float3.Cross(forward, float3.Up);
        if (right.LengthSquared < 1e-6f)
            return;
        right = right.Normalized;

        CaptureLimbPoseGuide(BodyNode.LeftUpperArm, BodyNode.LeftLowerArm, BodyNode.LeftHand, forward, right);
        CaptureLimbPoseGuide(BodyNode.RightUpperArm, BodyNode.RightLowerArm, BodyNode.RightHand, forward, right);
        CaptureLimbPoseGuide(BodyNode.LeftUpperLeg, BodyNode.LeftLowerLeg, BodyNode.LeftFoot, forward, right);
        CaptureLimbPoseGuide(BodyNode.RightUpperLeg, BodyNode.RightLowerLeg, BodyNode.RightFoot, forward, right);
    }

    private void CaptureLimbPoseGuide(BodyNode rootNode, BodyNode midNode, BodyNode endNode, float3 forward, float3 right)
    {
        var root = TryGetBone(rootNode);
        var mid = TryGetBone(midNode);
        var end = TryGetBone(endNode);
        if (root == null || mid == null || end == null)
            return;

        float3 limb = end.GlobalPosition - root.GlobalPosition;
        if (limb.LengthSquared < 1e-8f)
            return;

        float limbLen = limb.Length;
        float3 limbDir = limb / limbLen;
        float3 endBody = ToBodyDirection(limbDir, forward, right);

        float3 bend = mid.GlobalPosition - root.GlobalPosition;
        bend -= limbDir * float3.Dot(bend, limbDir);

        float minBend = System.MathF.Max(limbLen * 0.015f, 0.003f);
        bool hasBend = bend.LengthSquared > minBend * minBend;
        float3 bendBody = hasBend ? ToBodyDirection(bend.Normalized, forward, right) : float3.Zero;

        _limbPoseGuides[rootNode] = new LimbPoseGuide(true, endBody, hasBend, bendBody);
        LumoraLogger.Log(
            $"[IK-RIG-GUIDE] root={rootNode} mid={midNode} end={endNode} " +
            $"rootPos={root.GlobalPosition} midPos={mid.GlobalPosition} endPos={end.GlobalPosition} " +
            $"bodyFwd={forward} bodyRight={right} limbWorld={limbDir} limbBody={endBody} " +
            $"bendWorld={(hasBend ? bend.Normalized : float3.Zero)} bendBody={bendBody} hasBend={hasBend} " +
            $"limbLen={limbLen:F4} bendLen={bend.Length:F4} minBend={minBend:F4}");
    }

    private static float3 ToBodyDirection(float3 worldDir, float3 forward, float3 right)
        => new float3(float3.Dot(worldDir, forward), float3.Dot(worldDir, right), float3.Dot(worldDir, float3.Up));

    // Rotate the `parent` bone so the direction to its `child` points along targetWorldDir (world space). No-op
    // when either bone is missing or the segment is degenerate. Mutating the parent's global rotation also moves
    // the child slot, so callers process root-to-tip.
    private void AlignBone(BodyNode parent, BodyNode child, float3 targetWorldDir)
    {
        var p = TryGetBone(parent);
        var c = TryGetBone(child);
        if (p == null || c == null) return;

        float3 current = c.GlobalPosition - p.GlobalPosition;
        if (current.LengthSquared < 1e-8f) return;
        var delta = FromTo(current.Normalized, targetWorldDir.Normalized);
        p.GlobalRotation = delta * p.GlobalRotation;
    }

    // Shortest-arc rotation taking unit vector `from` onto unit vector `to`.
    private static floatQ FromTo(float3 from, float3 to)
    {
        float d = float3.Dot(from, to);
        if (d >= 0.99999f) return floatQ.Identity;
        if (d <= -0.99999f)
        {
            // Antiparallel: rotate 180 degrees about any perpendicular axis.
            float3 axis = float3.Cross(float3.Up, from);
            if (axis.LengthSquared < 1e-6f) axis = float3.Cross(float3.Right, from);
            return floatQ.AxisAngle(axis.Normalized, System.MathF.PI); // AxisAngle is RADIANS, not degrees
        }
        float3 c = float3.Cross(from, to).Normalized;
        float angleRad = (float)System.Math.Acos(System.Math.Clamp(d, -1f, 1f));
        return floatQ.AxisAngleRad(c, angleRad);
    }

    /// <summary>
    /// Map a bone name to its body node by heuristic, so real rigs work regardless of naming convention.
    /// </summary>
    public static BodyNode ClassifyBoneName(string name) => ClassifyBoneName(name, out _);

    /// <summary>
    /// Name-classify a bone, also reporting whether the match was ambiguous (a bare "arm"/"leg" with no
    /// upper/lower qualifier, or a spine/chest that could be any segment of the column). Ambiguous matches
    /// are re-resolved by topology in <see cref="PopulateFromSkeleton"/>. Side is detected from the name
    /// (Left/Right, _L/_R, L_/R_) and the base name picks the node (UpperArm/Bicep, ForeArm/LowerArm/Elbow,
    /// Hand/Wrist, Palm, Jaw, Thigh/UpLeg, Calf/Shin/Knee, Foot, Toe, ...). Returns
    /// <see cref="BodyNode.NONE"/> for bones it can't place. -xlinka
    /// </summary>
    public static BodyNode ClassifyBoneName(string name, out bool ambiguous)
    {
        ambiguous = false;
        if (string.IsNullOrEmpty(name))
            return BodyNode.NONE;

        var names = SplitName(name);
        if (names.Contains("twist"))
            return BodyNode.NONE;   // twist/roll helper bones aren't body nodes

        string t = name.ToLowerInvariant().Replace("armature", "");

        // Center chain - no side needed.
        if (t.Contains("hips") || t.Contains("pelvis"))
            return BodyNode.Hips;
        if (t.Contains("upperchest"))
            return BodyNode.UpperChest;
        if (t.Contains("chest") || t.Contains("ribcage"))
            return BodyNode.Chest;
        if (t.Contains("spine") || t.Contains("torso"))
        {
            // A bare "spine"/"torso" could be any segment of the hips->head column on a multi-segment rig;
            // topology decides which one. (Spine_01/02/03 all match here.)
            ambiguous = true;
            return BodyNode.Spine;
        }
        if (t.Contains("jaw") || t.Contains("mandible"))
            return BodyNode.Jaw;
        if (t.Contains("neck") && !t.Contains("necklace"))
            return BodyNode.Neck;
        if (t.Contains("head"))
            return BodyNode.Head;

        var chirality = DetectChirality(names);

        // Eyes (need a side, exclude brows/lids/blink shapes).
        bool eyeish = t.Contains("eye") || names.Contains("eye");
        if (eyeish && !t.Contains("eyebrow") && !t.Contains("eyelid")
            && !names.Contains("brow") && !names.Contains("lid") && !names.Contains("blink"))
        {
            return chirality == Chirality.Right ? BodyNode.RightEye
                : chirality == Chirality.Left ? BodyNode.LeftEye
                : BodyNode.NONE;
        }

        if (chirality == null)
            return BodyNode.NONE;   // limbs need a side
        bool right = chirality == Chirality.Right;

        // Fingers (need a side). Checked BEFORE the hand branch - finger bones are commonly named like
        // "LeftHandThumb1", which contains "hand" and would otherwise register as the wrist. Match a finger
        // keyword + a segment (named: proximal/intermediate/distal/metacarpal/tip, or numbered: 1/2/3/4).
        // ComposeFinger normalizes the thumb (which has no intermediate segment). -xlinka
        var fingerType = DetectFingerType(t);
        if (fingerType.HasValue)
            return fingerType.Value.ComposeFinger(DetectFingerSegment(t, names, fingerType.Value), chirality.Value);

        if (t.Contains("shoulder") || t.Contains("clavicle") || t.Contains("collar"))
            return right ? BodyNode.RightShoulder : BodyNode.LeftShoulder;
        if (t.Contains("upperarm") || t.Contains("uparm") || t.Contains("uarm") || t.Contains("bicep"))
            return right ? BodyNode.RightUpperArm : BodyNode.LeftUpperArm;
        if (t.Contains("forearm") || t.Contains("lowerarm") || t.Contains("lowarm") || t.Contains("elbow"))
            return right ? BodyNode.RightLowerArm : BodyNode.LeftLowerArm;
        // Palm sits between wrist and fingers; check before the broad hand/wrist match.
        if (t.Contains("palm"))
            return right ? BodyNode.RightPalm : BodyNode.LeftPalm;
        if (t.Contains("hand") || t.Contains("wrist"))
            return right ? BodyNode.RightHand : BodyNode.LeftHand;
        if (t.Contains("thigh") || t.Contains("upleg") || t.Contains("uleg") || t.Contains("upperleg")
            || (t.Contains("hip") && !t.Contains("hips")))
            return right ? BodyNode.RightUpperLeg : BodyNode.LeftUpperLeg;
        if (t.Contains("calf") || t.Contains("lowerleg") || t.Contains("lowleg") || t.Contains("knee") || t.Contains("shin"))
            return right ? BodyNode.RightLowerLeg : BodyNode.LeftLowerLeg;
        if (t.Contains("foot") || t.Contains("ankle") || names.Contains("feet"))
            return right ? BodyNode.RightFoot : BodyNode.LeftFoot;
        if (t.Contains("toe") || (t.Contains("ball") && !eyeish))
            return right ? BodyNode.RightToes : BodyNode.LeftToes;

        // Ambiguous bare "arm"/"leg" - assume the upper segment; topology corrects it.
        if (t.Contains("arm"))
        {
            ambiguous = true;
            return right ? BodyNode.RightUpperArm : BodyNode.LeftUpperArm;
        }
        if (t.Contains("leg"))
        {
            ambiguous = true;
            return right ? BodyNode.RightUpperLeg : BodyNode.LeftUpperLeg;
        }

        return BodyNode.NONE;
    }

    // Split a bone name into lowercase word segments, breaking on non-letters and camelCase boundaries
    // ("UpperArm_L" -> upper, arm, l). Used for side detection and keyword matching.
    private static List<string> SplitName(string name)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        char prev = '\0';
        foreach (var c in name)
        {
            bool boundary = !char.IsLetter(c) || (char.IsUpper(c) && char.IsLower(prev));
            if (boundary)
                Flush(segments, current);
            if (char.IsLetter(c))
                current.Append(c);
            prev = c;
        }
        Flush(segments, current);
        return segments;
    }

    private static void Flush(List<string> segments, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
            return;
        var segment = current.ToString().ToLowerInvariant();
        if (!segments.Contains(segment))
            segments.Add(segment);
        current.Clear();
    }

    // Left/Right from explicit words or isolated L/R segments (covers Left/Right, _L/_R, L_/R_).
    private static Chirality? DetectChirality(List<string> names)
    {
        bool left = names.Contains("left") || names.Contains("l");
        bool right = names.Contains("right") || names.Contains("r");
        if (left ^ right)
            return left ? Chirality.Left : Chirality.Right;
        return null;
    }

    // Map a finger keyword in the lowercased name to a finger type, or null when none is present.
    private static FingerType? DetectFingerType(string t)
    {
        if (t.Contains("thumb")) return FingerType.Thumb;
        if (t.Contains("index") || t.Contains("point")) return FingerType.Index;
        if (t.Contains("middle")) return FingerType.Middle;
        if (t.Contains("ring")) return FingerType.Ring;
        if (t.Contains("pinky") || t.Contains("pinkie") || t.Contains("little")) return FingerType.Pinky;
        return null;
    }

    // Map a finger segment from a named keyword, else from a trailing number. The number convention differs by
    // finger: the thumb's first joint is its metacarpal, where other fingers start at the proximal. Defaults to
    // Proximal so a bare "Thumb"/"Index" still lands on a real segment.
    private static FingerSegmentType DetectFingerSegment(string t, List<string> names, FingerType finger)
    {
        if (t.Contains("metacarpal") || t.Contains("meta")) return FingerSegmentType.Metacarpal;
        if (t.Contains("proximal")) return FingerSegmentType.Proximal;
        if (t.Contains("intermediate")) return FingerSegmentType.Intermediate;
        if (t.Contains("distal")) return FingerSegmentType.Distal;
        if (t.Contains("tip") || names.Contains("end")) return FingerSegmentType.Tip;

        int num = LastNumber(t);
        if (finger == FingerType.Thumb)
            return num switch
            {
                1 => FingerSegmentType.Metacarpal,
                2 => FingerSegmentType.Proximal,
                3 => FingerSegmentType.Distal,
                >= 4 => FingerSegmentType.Tip,
                _ => FingerSegmentType.Proximal,
            };
        return num switch
        {
            1 => FingerSegmentType.Proximal,
            2 => FingerSegmentType.Intermediate,
            3 => FingerSegmentType.Distal,
            >= 4 => FingerSegmentType.Tip,
            _ => FingerSegmentType.Proximal,
        };
    }

    // Last digit-run anywhere in the string (handles "Thumb1", "Thumb1_L", "thumb_01"). 0 when there's none.
    private static int LastNumber(string t)
    {
        int end = -1;
        for (int i = t.Length - 1; i >= 0; i--)
            if (char.IsDigit(t[i])) { end = i; break; }
        if (end < 0) return 0;
        int start = end;
        while (start - 1 >= 0 && char.IsDigit(t[start - 1])) start--;
        return int.TryParse(t.Substring(start, end - start + 1), out var n) ? n : 0;
    }

    // ----- GEOMETRIC FINGER DETECTION -------------------------------------------------------------------

    /// <summary>
    /// Refine finger bones on both hands. Name-based classification (run during populate) is the primary
    /// path; this re-detects any hand whose fingers the names couldn't resolve, using only geometry.
    /// </summary>
    public void DetectHandRigs()
    {
        var left = TryGetBone(BodyNode.LeftHand);
        var right = TryGetBone(BodyNode.RightHand);
        if (left != null && !HasLeftFingerBones)
            DetectHandRig(left, Chirality.Left);
        if (right != null && !HasRightFingerBones)
            DetectHandRig(right, Chirality.Right);
    }

    /// <summary>
    /// Geometrically detect the five fingers under a hand when names didn't. Finds the single-child chains
    /// descending from the hand (or its palm), picks the thumb (by name, else the shortest/most-divergent
    /// chain), sorts the remaining four by their root's distance to the thumb to assign Index->Pinky, then
    /// walks each chain assigning Metacarpal/Proximal/Intermediate/Distal/Tip (the thumb skips Intermediate).
    /// </summary>
    public bool DetectHandRig(Slot hand, Chirality chirality)
    {
        // Clear any partially/incorrectly named finger bones for this side before re-detecting.
        var start = BodyNode.LeftThumb_Metacarpal.GetSide(chirality);
        var end = BodyNode.LeftPinky_Tip.GetSide(chirality);
        for (var n = start; n <= end; n++)
            Bones.Remove(n);

        var roots = FindFingerRoots(hand);
        if (roots == null || roots.Count < 3)
            return false;

        // Build a candidate finger per root.
        var fingers = new List<FingerCandidate>();
        foreach (var root in roots)
            fingers.Add(new FingerCandidate(hand, root));

        // Identify the thumb: prefer a name hit; otherwise the shortest chain (the thumb is the stubbiest
        // and most laterally offset finger).
        FingerCandidate? thumb = null;
        foreach (var f in fingers)
            if (f.NamedType == FingerType.Thumb) { thumb = f; break; }
        if (thumb == null)
        {
            float min = float.MaxValue;
            foreach (var f in fingers)
                if (f.Length < min) { min = f.Length; thumb = f; }
        }
        if (thumb == null)
            return false;
        thumb.Type = FingerType.Thumb;

        // Sort the rest by distance-from-thumb-root: nearest is the index, farthest the pinky.
        var others = new List<FingerCandidate>();
        foreach (var f in fingers)
            if (f != thumb) others.Add(f);
        var thumbRoot = thumb.Root.GlobalPosition;
        others.Sort((a, b) =>
            float3.Distance(a.Root.GlobalPosition, thumbRoot)
                .CompareTo(float3.Distance(b.Root.GlobalPosition, thumbRoot)));

        var order = new[] { FingerType.Index, FingerType.Middle, FingerType.Ring, FingerType.Pinky };
        for (int i = 0; i < others.Count && i < order.Length; i++)
            others[i].Type = order[i];

        // Assign segments per candidate.
        thumb.Assign(this, chirality);
        foreach (var f in others)
            f.Assign(this, chirality);
        return true;
    }

    // Find the set of finger-root slots under a hand: a node whose children are all single-child chains of
    // depth >= 2 (i.e. real fingers, not stub helper bones). Recurses past a palm/wrapper node. Returns
    // null when no plausible fan of fingers is found.
    private static List<Slot>? FindFingerRoots(Slot hand)
    {
        // A hand (or palm) with at least 3 finger-like children is the fan root.
        if (hand.Children.Count >= 3)
        {
            var candidates = new List<Slot>();
            foreach (var child in hand.Children)
                if (IsChainOfDepth(child, 2))
                    candidates.Add(child);
            if (candidates.Count >= 3)
                return candidates;
        }

        // Otherwise descend through a single wrapper (e.g. a palm slot) to find the fan.
        foreach (var child in hand.Children)
        {
            var found = FindFingerRoots(child);
            if (found != null)
                return found;
        }
        return null;
    }

    // True when `root` begins a single-child chain at least `depth` segments long (each step has exactly
    // one continuing child). Distinguishes a finger from a leaf or branching helper.
    private static bool IsChainOfDepth(Slot root, int depth)
    {
        var node = root;
        for (int i = 0; i < depth; i++)
        {
            var next = NextChainSegment(node);
            if (next == null)
                return false;
            node = next;
        }
        return true;
    }

    // The single continuing child of a slot, or null if it has none or branches. Lets us follow one finger.
    private static Slot? NextChainSegment(Slot slot)
    {
        if (slot.Children.Count == 1)
            return slot.Children[0];
        return null;
    }

    // A finger candidate built by following the single-child chain from a root under the hand. Carries the
    // total length (to spot the thumb), the per-segment slots, and any name hint on the root.
    private sealed class FingerCandidate
    {
        public readonly Slot Root;
        public readonly List<Slot> Segments = new();
        public readonly FingerType? NamedType;
        public float Length;
        public FingerType Type;

        public FingerCandidate(Slot hand, Slot root)
        {
            Root = root;
            NamedType = DetectFingerType(root.Name.Value.ToLowerInvariant());

            var prev = hand;
            Slot? node = root;
            int guard = 0;
            while (node != null && guard++ < 8)
            {
                float step = float3.Distance(prev.GlobalPosition, node.GlobalPosition);
                if (step > 1e-5f)
                {
                    Segments.Add(node);
                    Length += step;
                    prev = node;
                }
                node = NextChainSegment(node);
            }
        }

        // Assign this candidate's segments to body nodes. A thumb has no intermediate, so its 3-4 segments
        // map metacarpal/proximal/distal/tip; other fingers map metacarpal/proximal/intermediate/distal/tip.
        // When a chain has only 3-4 segments (no metacarpal authored) the leading metacarpal is dropped.
        public void Assign(HumanoidRig rig, Chirality chirality)
        {
            FingerSegmentType[] layout = Type == FingerType.Thumb
                ? new[] { FingerSegmentType.Metacarpal, FingerSegmentType.Proximal, FingerSegmentType.Distal, FingerSegmentType.Tip }
                : new[] { FingerSegmentType.Metacarpal, FingerSegmentType.Proximal, FingerSegmentType.Intermediate, FingerSegmentType.Distal, FingerSegmentType.Tip };

            // Most rigs omit the metacarpal for the four fingers; if this chain is one short of the full
            // layout, skip the leading metacarpal so proximal lines up with the first real joint.
            int offset = 0;
            if (Segments.Count == layout.Length - 1)
                offset = 1;

            for (int i = 0; i < Segments.Count && i + offset < layout.Length; i++)
            {
                var node = Type.ComposeFinger(layout[i + offset], chirality);
                if (!rig.Bones.ContainsKey(node))
                    rig.SetBone(node, Segments[i]);
            }
        }
    }

    /// <summary>
    /// Log diagnostic info about this rig.
    /// </summary>
    public void LogDiagnosticInfo()
    {
        LumoraLogger.Log($"HumanoidRig Diagnostic Info:");
        LumoraLogger.Log($"  Total bones: {Bones.Count}");
        LumoraLogger.Log($"  IsHumanoid: {IsHumanoid}");
        LumoraLogger.Log($"  HasLeftFingerBones: {HasLeftFingerBones}");
        LumoraLogger.Log($"  HasRightFingerBones: {HasRightFingerBones}");

        var missing = new List<BodyNode>();
        GetMissingBipedBones(missing);
        if (missing.Count > 0)
        {
            LumoraLogger.Log($"  Missing bones: {string.Join(", ", missing)}");
        }

        foreach (var entry in Bones)
        {
            LumoraLogger.Log($"  {entry.Key.Value}: {entry.Value?.Target?.SlotName.Value ?? "null"}");
        }
    }
}
