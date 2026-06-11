// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

// The single per-world avatar builder. BuildAvatar(userRoot) wires up a
// joining user (body-node tracking slots, collider, character controller,
// locomotion, head output, nameplate, avatar manager). Also serves as the
// AvatarManager.EmptySlotHandler, supplying default head/hand visuals when
// no custom avatar covers a body node. All behavior is gated by toggles so
// a world can opt parts in or out. Replaces the old inline DefaultAVI-style
// spawn. - xlinka
[ComponentCategory("Users/Common Avatar System")]
public class CommonAvatarBuilder : Component, IAvatarBuilder, IEmptyAvatarSlotHandler
{
    public const float DefaultHeight = 1.7f;
    public const float HeadSize = 0.2f;
    public const float HandSize = 0.08f;

    public readonly Sync<bool> SetupLocomotion = new();
    public readonly Sync<bool> SetupCollider = new();
    public readonly Sync<bool> SetupHeadOutput = new();
    public readonly Sync<bool> SetupNameplate = new();
    public readonly Sync<bool> SetupHandTools = new();
    public readonly Sync<bool> FillEmptySlots = new();

    public readonly Sync<colorHDR> DefaultTint = new();
    public readonly Sync<bool> HideHeadFromLocalUser = new();

    // Grip offset from the tracked controller pose to the hand body node.
    // Hands are a separate body node from controllers so avatar hand meshes
    // equip with a correct wrist pose instead of raw controller pose.
    public readonly Sync<float3> HandGripPositionOffset = new();
    public readonly Sync<floatQ> HandGripRotationOffset = new();

    // Fired after a user's avatar setup completes. (user, avatarSlot)
    public event Action<User, Slot> OnAvatarSetupFinish = null!;

    public override void OnInit()
    {
        base.OnInit();
        SetupLocomotion.Value = true;
        SetupCollider.Value = true;
        SetupHeadOutput.Value = true;
        SetupNameplate.Value = true;
        SetupHandTools.Value = true;
        FillEmptySlots.Value = true;
        DefaultTint.Value = new colorHDR(0.8f, 0.8f, 0.8f, 1f);
        HideHeadFromLocalUser.Value = true;
        HandGripPositionOffset.Value = float3.Zero;
        HandGripRotationOffset.Value = floatQ.Identity;
    }

    // IAvatarBuilder

    public void BuildAvatar(UserRoot userRoot)
    {
        if (userRoot == null || userRoot.Slot == null)
        {
            LumoraLogger.Error("CommonAvatarBuilder: BuildAvatar called with null user root");
            return;
        }

        var user = userRoot.ActiveUser;
        var userSlot = userRoot.Slot;

        var bodyNodes = userSlot.AddSlot("Body Nodes");
        var headSlot = BuildHeadNode(bodyNodes, user);
        BuildControllerNode(bodyNodes, user, Chirality.Left);
        BuildControllerNode(bodyNodes, user, Chirality.Right);

        if (SetupCollider.Value)
        {
            var collider = userSlot.AttachComponent<CapsuleCollider>();
            collider.Type.Value = ColliderType.CharacterController;
            collider.Height.Value = 1.8f;
            collider.Radius.Value = 0.3f;
            collider.Offset.Value = new float3(0, 0.9f, 0);
            var characterController = userSlot.AttachComponent<CharacterController>();
            // Body follows the head's ground projection so the collider stays
            // under the player in room-scale, not at the root origin.
            characterController.HeadReference.Target = headSlot;
        }

        if (SetupLocomotion.Value)
            userSlot.AttachComponent<LocomotionController>();

        if (SetupHeadOutput.Value)
            userSlot.AttachComponent<HeadOutput>();

        // Per-user radial context menu + the avatar equip/dequip actions it
        // offers when pointing at an avatar.
        var menuSlot = userSlot.AddSlot("Context Menu");
        menuSlot.AttachComponent<UI.ContextMenuSystem>();
        menuSlot.AttachComponent<AvatarContextActions>();
        menuSlot.AttachComponent<UI.DashboardContextAction>();

        var avatarSlot = userSlot.AddSlot("Avatar");
        var avatarManager = avatarSlot.AttachComponent<AvatarManager>();
        avatarManager.UserRoot.Target = userRoot;
        // Name badge is composed by AvatarManager (mesh text + assigner)  - 
        // the manager just needs the display data and the toggle.
        avatarManager.AutoAddNameBadge.Value = SetupNameplate.Value;
        if (user != null)
            avatarManager.NameTagText.Value = user.UserName.Value ?? string.Empty;
        if (FillEmptySlots.Value)
        {
            avatarManager.EmptySlotHandler.Target = this;
            avatarManager.FillEmptySlots();
        }

        OnAvatarSetupFinish?.Invoke(user!, avatarSlot!);
        LumoraLogger.Log($"CommonAvatarBuilder: Built avatar for '{user?.UserName?.Value ?? "(null)"}'");
    }

    private Slot BuildHeadNode(Slot bodyNodes, User user)
    {
        var headSlot = bodyNodes.AddSlot("Head");
        headSlot.LocalPosition.Value = new float3(0f, DefaultHeight, 0f);

        var positioner = headSlot.AttachComponent<TrackedDevicePositioner>();
        positioner.AutoBodyNode.Value = BodyNode.Head;

        if (user != null)
        {
            var stream = headSlot.AttachComponent<TransformStreamDriver>();
            user.GetTrackingStreams(BodyNode.Head, out var posStream, out var rotStream);
            stream.PositionStream.Target = posStream;
            stream.RotationStream.Target = rotStream;
        }

        return headSlot;
    }

    // Controllers and hands are distinct body nodes. The controller slot is
    // device-tracked (positioner + streams) and carries the interaction tool.
    // The hand slot rides under it with a grip offset and owns its own
    // AvatarObjectSlot, so avatar hand meshes equip a wrist pose - and a
    // full-hand piece can declare the controller node mutually exclusive to
    // suppress controller visuals. - xlinka
    private void BuildControllerNode(Slot bodyNodes, User user, Chirality side)
    {
        bool isLeft = side == Chirality.Left;
        var controllerNode = isLeft ? BodyNode.LeftController : BodyNode.RightController;
        var handNode = isLeft ? BodyNode.LeftHand : BodyNode.RightHand;

        var controller = bodyNodes.AddSlot(isLeft ? "LeftController" : "RightController");
        controller.LocalPosition.Value = new float3(isLeft ? -0.25f : 0.25f, 1.0f, 0f);
        controller.LocalRotation.Value = isLeft
            ? floatQ.Euler(MathF.PI / 2f, -MathF.PI / 2f, 0f)
            : floatQ.Euler(-MathF.PI / 2f, -MathF.PI / 2f, 0f);

        var positioner = controller.AttachComponent<TrackedDevicePositioner>();
        positioner.AutoBodyNode.Value = controllerNode;

        if (user != null)
        {
            var stream = controller.AttachComponent<TransformStreamDriver>();
            user.GetTrackingStreams(controllerNode, out var posStream, out var rotStream);
            stream.PositionStream.Target = posStream;
            stream.RotationStream.Target = rotStream;
        }

        var hand = controller.AddSlot(isLeft ? "LeftHand" : "RightHand");
        hand.LocalPosition.Value = HandGripPositionOffset.Value;
        hand.LocalRotation.Value = HandGripRotationOffset.Value;
        var handObjectSlot = hand.AttachComponent<AvatarObjectSlot>();
        handObjectSlot.Node.Value = handNode;

        if (SetupHandTools.Value)
        {
            var toolSlot = controller.AddSlot("HandTool");
            var tool = toolSlot.AttachComponent<HandTool>();
            tool.Side.Value = side;
            if (!isLeft)
                tool.EquipNewToolItem<DevToolItem>("DevTool");
        }
    }

    // IEmptyAvatarSlotHandler

    // Default pieces are EQUIPPED avatar objects (not permanent fixtures) so
    // equipping a real avatar dequips them and AvatarDestroyOnDequip removes
    // the visuals automatically. - xlinka
    public Task FillEmptySlot(BodyNode node, AvatarManager manager, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || manager == null || manager.IsDestroyed)
            return Task.CompletedTask;

        Slot piece;
        switch (node)
        {
            case BodyNode.Head:
                piece = BuildDefaultHead(manager);
                break;
            case BodyNode.LeftHand:
            case BodyNode.RightHand:
                piece = BuildDefaultHand(manager, node);
                break;
            default:
                return Task.CompletedTask;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            piece.Destroy();
            return Task.CompletedTask;
        }

        if (!manager.Equip(piece, isManualEquip: false, forceDestroyOld: false, isFillingEmptySlot: true))
        {
            LumoraLogger.Warn($"CommonAvatarBuilder: default {node} fill equip failed");
            piece.Destroy();
        }
        return Task.CompletedTask;
    }

    // Pieces are built OUTSIDE the user root: CanEquip rejects objects that
    // already sit under a user, and Equip reparents the piece in afterwards.
    private Slot BuildDefaultHead(AvatarManager manager)
    {
        var piece = manager.World.RootSlot.AddSlot("BasicHead");
        piece.Persistent.Value = false;

        piece.AttachComponent<AvatarPoseNode>().Node.Value = BodyNode.Head;
        piece.AttachComponent<AvatarDestroyOnDequip>();

        var visual = piece.AddSlot("Visual");
        var mesh = visual.AttachComponent<SphereMesh>();
        mesh.Radius.Value = HeadSize * 0.5f;
        mesh.Segments.Value = 24;
        mesh.Rings.Value = 16;
        mesh.DualSided.Value = true;

        var renderer = visual.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        var material = visual.AttachComponent<UnlitMaterial>();
        material.TintColor.Value = DefaultTint.Value;
        renderer.Material.Target = material;

        if (HideHeadFromLocalUser.Value)
        {
            var localView = visual.AttachComponent<LocalViewOverride>();
            localView.Context.Value = RenderingContext.UserView;
            localView.HasScaleOverride.Value = true;
            localView.ScaleOverride.Value = float3.Zero;
        }

        return piece;
    }

    private static Slot BuildDefaultHand(AvatarManager manager, BodyNode node)
    {
        bool isLeft = node == BodyNode.LeftHand;
        var piece = manager.World.RootSlot.AddSlot(isLeft ? "BasicLeftHand" : "BasicRightHand");
        piece.Persistent.Value = false;

        piece.AttachComponent<AvatarPoseNode>().Node.Value = node;
        piece.AttachComponent<AvatarDestroyOnDequip>();
        piece.AttachComponent<ControllerHandVisual>().HandSide.Value =
            isLeft ? Chirality.Left : Chirality.Right;

        return piece;
    }
}
