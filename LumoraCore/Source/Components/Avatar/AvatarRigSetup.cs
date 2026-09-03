// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Avatar.IK;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraMeshes = Lumora.Core.Components.Meshes;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Turns a <see cref="HumanoidRig"/> into a grabbable, poseable skeleton: a collider + grab on each bone
/// (grabbing a bone reparents it to the hand, so the limb follows) plus a see-through bone visual, so
/// any rigged model can be posed before it's finalized into an avatar; the <see cref="AvatarStudio"/>
/// strips these handles on Create.
/// </summary>
public static class AvatarRigSetup
{
    private const string HandleName = "_PoseHandle";

    // Where each bone's visual points (the next joint down the chain), for orienting the bone cylinder.
    private static readonly (BodyNode bone, BodyNode child)[] BoneSegments =
    {
        (BodyNode.Hips, BodyNode.Spine),
        (BodyNode.Spine, BodyNode.Head),
        (BodyNode.LeftUpperArm, BodyNode.LeftLowerArm),
        (BodyNode.LeftLowerArm, BodyNode.LeftHand),
        (BodyNode.RightUpperArm, BodyNode.RightLowerArm),
        (BodyNode.RightLowerArm, BodyNode.RightHand),
        (BodyNode.LeftUpperLeg, BodyNode.LeftLowerLeg),
        (BodyNode.LeftLowerLeg, BodyNode.LeftFoot),
        (BodyNode.RightUpperLeg, BodyNode.RightLowerLeg),
        (BodyNode.RightLowerLeg, BodyNode.RightFoot),
    };

    // Every finger joint, in the order its chain runs, so each one can point at the next. Fingers are
    // not part of the required biped, so a rig without them simply has none of these bones and nothing
    // is drawn. Until there is a hand model to check against, these small balls are how you see whether
    // a rig's fingers line up with the fingers on the mesh. -xlinka
    private static readonly (BodyNode bone, BodyNode child)[] FingerSegments = BuildFingerSegments();

    private static (BodyNode, BodyNode)[] BuildFingerSegments()
    {
        var chains = new[]
        {
            (BodyNode.LeftThumb_Metacarpal, BodyNode.LeftThumb_Tip),
            (BodyNode.LeftIndexFinger_Metacarpal, BodyNode.LeftIndexFinger_Tip),
            (BodyNode.LeftMiddleFinger_Metacarpal, BodyNode.LeftMiddleFinger_Tip),
            (BodyNode.LeftRingFinger_Metacarpal, BodyNode.LeftRingFinger_Tip),
            (BodyNode.LeftPinky_Metacarpal, BodyNode.LeftPinky_Tip),
            (BodyNode.RightThumb_Metacarpal, BodyNode.RightThumb_Tip),
            (BodyNode.RightIndexFinger_Metacarpal, BodyNode.RightIndexFinger_Tip),
            (BodyNode.RightMiddleFinger_Metacarpal, BodyNode.RightMiddleFinger_Tip),
            (BodyNode.RightRingFinger_Metacarpal, BodyNode.RightRingFinger_Tip),
            (BodyNode.RightPinky_Metacarpal, BodyNode.RightPinky_Tip),
        };
        var pairs = new List<(BodyNode, BodyNode)>();
        foreach (var (start, end) in chains)
            for (var node = start; node < end; node++)
                pairs.Add((node, node + 1));
        return pairs.ToArray();
    }

    // A joint ball sized to the bone it sits on: a knee and a knuckle should not be the same size, and
    // one fixed radius turned a hand into a clump. Clamped so a tiny bone still has something you can
    // see and grab, and a long one does not grow a beach ball. -xlinka
    private static float JointRadius(float boneLength)
        => System.Math.Clamp(boneLength * 0.16f, 0.006f, 0.03f);

    /// <summary>Attach grab + visual handles to each minimal-biped bone. Idempotent. Returns the count added.</summary>
    public static int SetupPoseHandles(HumanoidRig rig)
    {
        if (rig == null || rig.IsDestroyed)
            return 0;

        var childOf = new Dictionary<BodyNode, BodyNode>();
        foreach (var segment in BoneSegments)
            childOf[segment.bone] = segment.child;

        int count = 0;
        foreach (var node in HumanoidRig.RequiredBones)
            count += AddHandle(rig, node, childOf, finger: false) ? 1 : 0;

        var fingerChildOf = new Dictionary<BodyNode, BodyNode>();
        foreach (var segment in FingerSegments)
            fingerChildOf[segment.bone] = segment.child;
        foreach (var node in fingerChildOf.Keys)
            count += AddHandle(rig, node, fingerChildOf, finger: true) ? 1 : 0;

        return count;
    }

    private static bool AddHandle(HumanoidRig rig, BodyNode node, Dictionary<BodyNode, BodyNode> childOf, bool finger)
    {
        var bone = rig.TryGetBone(node);
        if (bone == null || bone.IsDestroyed)
            return false;

        float radius = JointRadius(BoneLength(rig, bone, node, childOf));

        // Grab + pose: the collider matches the ball you can see, so what you aim at is what you get.
        // A finger sits inside its hand's reach, so it outranks the hand: hovering a knuckle should
        // pose that knuckle rather than swing the whole arm. -xlinka
        if (bone.GetComponent<Grabbable>() == null)
        {
            bone.AttachComponent<SphereCollider>().Radius.Value = radius;
            var grab = bone.AttachComponent<Grabbable>();
            grab.FollowRotation.Value = true;
            grab.GrabPriority.Value = finger ? 6 : 5;        // beat a whole-model grab when hovering a bone
            grab.InteractionPriority.Value = finger ? 6 : 5;
        }

        if (bone.FindChild(HandleName, recursive: false) == null)
            AddBoneVisual(rig, bone, node, childOf, radius);

        return true;
    }

    private static float BoneLength(HumanoidRig rig, Slot bone, BodyNode node, Dictionary<BodyNode, BodyNode> childOf)
    {
        if (!childOf.TryGetValue(node, out var childNode))
            return 0.12f;
        var child = rig.TryGetBone(childNode);
        if (child == null || child.IsDestroyed)
            return 0.12f;
        float length = bone.GlobalPointToLocal(child.GlobalPosition).Length;
        return length > 0.0005f ? length : 0.12f;
    }

    /// <summary>Remove the grab + visual handles (called when finalizing the avatar).</summary>
    public static void RemovePoseHandles(HumanoidRig rig)
    {
        if (rig == null || rig.IsDestroyed)
            return;

        var nodes = new List<BodyNode>(HumanoidRig.RequiredBones);
        foreach (var segment in FingerSegments)
        {
            nodes.Add(segment.bone);
            nodes.Add(segment.child);
        }
        foreach (var node in nodes)
        {
            var bone = rig.TryGetBone(node);
            if (bone == null || bone.IsDestroyed)
                continue;
            bone.GetComponent<Grabbable>()?.Destroy();
            bone.GetComponent<SphereCollider>()?.Destroy();
            bone.FindChild(HandleName, recursive: false)?.Destroy();
        }
    }

    // A see-through bone: a ball at the joint (so the skeleton reads as connected - knees, elbows, etc.)
    // plus a shaft cylinder to the child joint. Overlay material, so it all shows through the skin.
    private static void AddBoneVisual(HumanoidRig rig, Slot bone, BodyNode node, Dictionary<BodyNode, BodyNode> childOf, float radius)
    {
        var handle = bone.AddSlot(HandleName);   // sits at the bone origin = the joint

        var material = handle.AttachComponent<OverlayUnlitMaterial>();
        var color = BoneColor(node);
        material.FrontTintColor.Value = color;
        material.BehindTintColor.Value = color;
        material.UseVertexColor.Value = false;
        material.BlendMode.Value = BlendMode.Alpha;

        // Joint ball.
        var joint = handle.AttachComponent<LumoraMeshes.SphereMesh>();
        joint.Radius.Value = radius;
        joint.Segments.Value = radius < 0.012f ? 8 : 12;
        joint.Rings.Value = radius < 0.012f ? 6 : 8;
        var jointRenderer = handle.AttachComponent<MeshRenderer>();
        jointRenderer.Mesh.Target = joint;
        jointRenderer.Material.Target = material;

        // Shaft to the next joint (leaf bones - hands/feet/head ends - get just the ball).
        Slot childBone = childOf.TryGetValue(node, out var childNode) ? rig.TryGetBone(childNode) : null!;
        if (childBone == null || childBone.IsDestroyed)
            return;

        float3 tipLocal = bone.GlobalPointToLocal(childBone.GlobalPosition);
        float length = tipLocal.Length;
        if (length <= 0.001f)
            return;

        var shaft = handle.AddSlot("Shaft");
        shaft.LocalPosition.Value = tipLocal * 0.5f;
        shaft.LocalRotation.Value = FabrikSolver.FromToRotation(float3.Up, tipLocal.Normalized);
        var cylinder = shaft.AttachComponent<LumoraMeshes.CylinderMesh>();
        // Thinner than the ball, so joints read as joints and the shaft as the bone between them.
        cylinder.Radius.Value = radius * 0.55f;
        cylinder.Height.Value = length;
        cylinder.Segments.Value = 8;
        var shaftRenderer = shaft.AttachComponent<MeshRenderer>();
        shaftRenderer.Mesh.Target = cylinder;
        shaftRenderer.Material.Target = material;
    }

    // Left limbs cyan, right limbs orange, the spine/head/hips chain green - quick at-a-glance sides.
    private static colorHDR BoneColor(BodyNode node)
    {
        var name = node.ToString();
        if (name.StartsWith("Left"))
            return new colorHDR(0.25f, 0.7f, 1f, 0.8f);
        if (name.StartsWith("Right"))
            return new colorHDR(1f, 0.45f, 0.3f, 0.8f);
        return new colorHDR(0.6f, 0.85f, 0.55f, 0.8f);
    }
}
