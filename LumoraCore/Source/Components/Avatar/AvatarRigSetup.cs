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
/// Turns a <see cref="BipedRig"/> into a grabbable, poseable skeleton: a collider + grab on each bone
/// (grabbing a bone reparents it to the hand, so the limb follows) plus a see-through bone visual, so
/// any rigged model can be posed before it's finalized into an avatar; the <see cref="AvatarCreator"/>
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

    /// <summary>Attach grab + visual handles to each minimal-biped bone. Idempotent. Returns the count added.</summary>
    public static int SetupPoseHandles(BipedRig rig)
    {
        if (rig == null || rig.IsDestroyed)
            return 0;

        var childOf = new Dictionary<BodyNode, BodyNode>();
        foreach (var segment in BoneSegments)
            childOf[segment.bone] = segment.child;

        int count = 0;
        foreach (var node in BipedRig.MinimalBiped)
        {
            var bone = rig.TryGetBone(node);
            if (bone == null || bone.IsDestroyed)
                continue;

            // Grab + pose: a small collider (smaller than the laser's grab-hover radius so it doesn't
            // block the grab) and a Grabbable on the bone slot - grabbing reparents the bone to the
            // hand, so the limb follows on release.
            if (bone.GetComponent<Grabbable>() == null)
            {
                bone.AttachComponent<SphereCollider>().Radius.Value = 0.04f;
                var grab = bone.AttachComponent<Grabbable>();
                grab.FollowRotation.Value = true;
                grab.GrabPriority.Value = 5;        // beat a whole-model grab when hovering a bone
                grab.InteractionPriority.Value = 5;
            }

            if (bone.FindChild(HandleName, recursive: false) == null)
                AddBoneVisual(rig, bone, node, childOf);

            count++;
        }
        return count;
    }

    /// <summary>Remove the grab + visual handles (called when finalizing the avatar).</summary>
    public static void RemovePoseHandles(BipedRig rig)
    {
        if (rig == null || rig.IsDestroyed)
            return;

        foreach (var node in BipedRig.MinimalBiped)
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
    private static void AddBoneVisual(BipedRig rig, Slot bone, BodyNode node, Dictionary<BodyNode, BodyNode> childOf)
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
        joint.Radius.Value = 0.028f;
        joint.Segments.Value = 12;
        joint.Rings.Value = 8;
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
        cylinder.Radius.Value = 0.018f;
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
