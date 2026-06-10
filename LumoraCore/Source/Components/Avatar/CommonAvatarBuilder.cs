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
    }

    // ===== IAvatarBuilder =====

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
        BuildHeadNode(bodyNodes, user);
        BuildHandNode(bodyNodes, user, Chirality.Left);
        BuildHandNode(bodyNodes, user, Chirality.Right);

        if (SetupCollider.Value)
        {
            var collider = userSlot.AttachComponent<CapsuleCollider>();
            collider.Type.Value = ColliderType.CharacterController;
            collider.Height.Value = 1.8f;
            collider.Radius.Value = 0.3f;
            collider.Offset.Value = new float3(0, 0.9f, 0);
            userSlot.AttachComponent<CharacterController>();
        }

        if (SetupLocomotion.Value)
            userSlot.AttachComponent<LocomotionController>();

        if (SetupHeadOutput.Value)
            userSlot.AttachComponent<HeadOutput>();

        if (SetupNameplate.Value && user != null)
        {
            var nameplateSlot = userRoot.HeadSlot?.AddSlot("Nameplate");
            if (nameplateSlot != null)
            {
                nameplateSlot.LocalPosition.Value = float3.Zero;
                nameplateSlot.LocalRotation.Value = floatQ.Identity;
                nameplateSlot.AttachComponent<Nameplate>().Initialize(user);
            }
        }

        var avatarSlot = userSlot.AddSlot("Avatar");
        var avatarManager = avatarSlot.AttachComponent<AvatarManager>();
        avatarManager.UserRoot.Target = userRoot;
        if (FillEmptySlots.Value)
        {
            avatarManager.EmptySlotHandler.Target = this;
            avatarManager.FillEmptySlots();
        }

        OnAvatarSetupFinish?.Invoke(user!, avatarSlot!);
        LumoraLogger.Log($"CommonAvatarBuilder: Built avatar for '{user?.UserName?.Value ?? "(null)"}'");
    }

    private void BuildHeadNode(Slot bodyNodes, User user)
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
    }

    private void BuildHandNode(Slot bodyNodes, User user, Chirality side)
    {
        bool isLeft = side == Chirality.Left;
        var name = isLeft ? "LeftHand" : "RightHand";
        var hand = bodyNodes.AddSlot(name);
        hand.LocalPosition.Value = new float3(isLeft ? -0.25f : 0.25f, 1.0f, 0f);
        hand.LocalRotation.Value = isLeft
            ? floatQ.Euler(MathF.PI / 2f, -MathF.PI / 2f, 0f)
            : floatQ.Euler(-MathF.PI / 2f, -MathF.PI / 2f, 0f);

        var sourceNode = isLeft ? BodyNode.LeftController : BodyNode.RightController;
        var positioner = hand.AttachComponent<TrackedDevicePositioner>();
        positioner.AutoBodyNode.Value = sourceNode;

        if (user != null)
        {
            var stream = hand.AttachComponent<TransformStreamDriver>();
            user.GetTrackingStreams(sourceNode, out var posStream, out var rotStream);
            stream.PositionStream.Target = posStream;
            stream.RotationStream.Target = rotStream;
        }

        hand.AttachComponent<ControllerHandVisual>().HandSide.Value = side;

        if (SetupHandTools.Value)
        {
            var toolSlot = hand.AddSlot("HandTool");
            var tool = toolSlot.AttachComponent<HandTool>();
            tool.Side.Value = side;
            if (!isLeft)
                tool.EquipNewToolItem<DevToolItem>("DevTool");
        }
    }

    // ===== IEmptyAvatarSlotHandler =====

    public Task FillEmptySlot(BodyNode node, AvatarManager manager, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || manager == null || manager.IsDestroyed)
            return Task.CompletedTask;

        // Only the head gets a default visual. Hands already carry a
        // ControllerHandVisual finger skeleton from BuildHandNode. - xlinka
        if (node != BodyNode.Head)
            return Task.CompletedTask;

        var piece = manager.Slot.AddSlot("BasicHead");
        piece.Persistent.Value = false;

        piece.AttachComponent<AvatarPoseNode>().Node.Value = BodyNode.Head;

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

        if (cancellationToken.IsCancellationRequested)
        {
            piece.Destroy();
            return Task.CompletedTask;
        }

        if (!manager.Equip(piece, isManualEquip: false, forceDestroyOld: false, isFillingEmptySlot: true))
        {
            LumoraLogger.Warn("CommonAvatarBuilder: head fill equip failed");
            piece.Destroy();
        }
        return Task.CompletedTask;
    }
}
