// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Avatar.IK;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraMeshes = Lumora.Core.Components.Meshes;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// In-world avatar setup tool. Spawns a standing figure of grabbable markers - head, hands, feet,
/// pelvis - that you scale and slide over an imported, rigged model, plus a small canvas panel with
/// the controls. Create finds the model's rig by overlapping each marker with the world's colliders,
/// attaches the avatar runtime (IK + face drivers + root), makes it click-to-equip, then removes
/// itself.
///
/// Uses a marker + overlap-detection model, adapted to our self-describing avatar: the avatar runtime
/// auto-calibrates from the rig and the user's tracking, so the markers are an authoring/affordance +
/// detection surface rather than explicit IK targets.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public sealed class AvatarCreator : Component
{
    /// <summary>Also detect + calibrate the feet (shows the foot markers).</summary>
    public readonly Sync<bool> CalibrateFeet = new();

    /// <summary>Also detect + calibrate the pelvis (shows the pelvis marker).</summary>
    public readonly Sync<bool> CalibratePelvis = new();

    /// <summary>Attach blink + eye-look drivers when the avatar has eye bones.</summary>
    public readonly Sync<bool> SetupEyes = new();

    // How far each marker reaches to find the model's colliders.
    private const float HeadDetectionRadius = 0.2f;
    private const float LimbDetectionRadius = 0.15f;

    // Grab core radius (must stay near the laser's fixed grab-hover radius so the small solid ball is
    // what you grab) and the world->canvas scale for the control panel (1 canvas unit ~= 1.1mm).
    private const float CoreRadius = 0.05f;
    private const float CanvasScale = 0.0011f;

    private static readonly colorHDR HeadFill = new(1f, 1f, 1f, 0.12f);
    private static readonly colorHDR LeftFill = new(0.2f, 0.7f, 1f, 0.15f);
    private static readonly colorHDR RightFill = new(1f, 0.3f, 0.3f, 0.15f);
    private static readonly colorHDR PelvisFill = new(0.7f, 0.4f, 1f, 0.12f);

    private static readonly color PanelBg = new(0.10f, 0.10f, 0.16f, 0.96f);
    private static readonly color TextPrimary = new(0.93f, 0.93f, 0.97f, 1f);
    private static readonly color TextDim = new(0.72f, 0.72f, 0.80f, 1f);
    private static readonly color Accent = new(0.62f, 0.55f, 0.95f, 1f);
    private static readonly color ControlFill = new(0.30f, 0.29f, 0.42f, 1f);
    private static readonly color CreateFill = new(0.28f, 0.62f, 0.40f, 1f);
    private static readonly color CancelFill = new(0.60f, 0.28f, 0.30f, 1f);
    private static readonly color AlignFill = new(0.30f, 0.45f, 0.70f, 1f);

    private readonly SyncRef<Slot> _headProxy = new();
    private readonly SyncRef<Slot> _leftHandProxy = new();
    private readonly SyncRef<Slot> _rightHandProxy = new();
    private readonly SyncRef<Slot> _leftFootProxy = new();
    private readonly SyncRef<Slot> _rightFootProxy = new();
    private readonly SyncRef<Slot> _pelvisProxy = new();

    private bool _built;

    public override void OnInit()
    {
        base.OnInit();
        CalibrateFeet.Value = false;
        CalibratePelvis.Value = false;
        SetupEyes.Value = true;
    }

    public override void OnStart()
    {
        base.OnStart();

        AnchorToUser();
        BuildMarkers();
        BuildControlPanel();
        SetupWholeToolGrab();

        CalibrateFeet.OnChanged += _ => RefreshOptionalMarkers();
        CalibratePelvis.OnChanged += _ => RefreshOptionalMarkers();
        RefreshOptionalMarkers();
    }

    // Stand the figure on the floor in front of the local user, facing them, so the markers overlay an
    // avatar imported in the same spot. Local marker offsets are then rotated into place by the slot.
    private void AnchorToUser()
    {
        var user = World?.LocalUser?.Root;
        if (user == null)
            return;

        var forward = user.HeadRotation * float3.Forward;
        forward.y = 0f;
        forward = forward.LengthSquared > 1e-5f ? forward.Normalized : float3.Forward;

        Slot.GlobalPosition = user.FeetPosition + forward * 1.2f;
        // Face the user: the figure's local forward points back toward them (so left/right read as the
        // avatar's sides). FromToRotation, not floatQ.LookRotation - that returns the inverse.
        Slot.GlobalRotation = FabrikSolver.FromToRotation(float3.Forward, -forward);
    }

    // MARKERS

    private void BuildMarkers()
    {
        if (_built)
            return;
        _built = true;

        _headProxy.Target = AddMarker("Headset", new float3(0f, 1.6f, 0f), HeadDetectionRadius, HeadFill);
        AddEyeBalls(_headProxy.Target);
        _leftHandProxy.Target = AddMarker("LeftHand", new float3(-0.35f, 1.0f, 0f), LimbDetectionRadius, LeftFill);
        _rightHandProxy.Target = AddMarker("RightHand", new float3(0.35f, 1.0f, 0f), LimbDetectionRadius, RightFill);
        _pelvisProxy.Target = AddMarker("Pelvis", new float3(0f, 0.9f, 0f), LimbDetectionRadius, PelvisFill);
        _leftFootProxy.Target = AddMarker("LeftFoot", new float3(-0.12f, 0.1f, 0f), LimbDetectionRadius, LeftFill);
        _rightFootProxy.Target = AddMarker("RightFoot", new float3(0.12f, 0.1f, 0f), LimbDetectionRadius, RightFill);
    }

    private Slot AddMarker(string name, float3 localPos, float detectionRadius, colorHDR fill)
    {
        var slot = Slot.AddSlot(name);
        slot.LocalPosition.Value = localPos;

        // Grab core: a small solid ball that IS the grab handle. NO collider - the laser grabs via its
        // fixed hover radius at the slot center, and a collider bigger than that radius would block the
        // ray before the grab sphere and make the marker ungrabbable (InteractionLaser hover/block model).
        var core = slot.AttachComponent<LumoraMeshes.SphereMesh>();
        core.Radius.Value = CoreRadius;
        core.Segments.Value = 16;
        core.Rings.Value = 12;
        var coreRenderer = slot.AttachComponent<MeshRenderer>();
        coreRenderer.Mesh.Target = core;
        var coreMaterial = slot.AttachComponent<UnlitMaterial>();
        coreMaterial.Color = new colorHDR(fill.r, fill.g, fill.b, 1f);
        coreRenderer.Material.Target = coreMaterial;

        var grab = slot.AttachComponent<Grabbable>();
        grab.FollowRotation.Value = true;
        grab.Scalable.Value = true;
        grab.GrabPriority.Value = 20;        // beat the imported model's per-bone pose grabs (priority 5)
        grab.InteractionPriority.Value = 20;

        // Detection halo: a big translucent sphere showing how far Create reaches. NO collider (so it
        // never blocks the grab ray) - purely a cue. Create overlaps at this marker's center.
        var halo = slot.AddSlot("Detection");
        var haloMesh = halo.AttachComponent<LumoraMeshes.SphereMesh>();
        haloMesh.Radius.Value = detectionRadius;
        haloMesh.Segments.Value = 20;
        haloMesh.Rings.Value = 14;
        var haloRenderer = halo.AttachComponent<MeshRenderer>();
        haloRenderer.Mesh.Target = haloMesh;
        var haloMaterial = halo.AttachComponent<UnlitMaterial>();
        haloMaterial.Color = fill;
        haloMaterial.BlendMode.Value = BlendMode.Transparent;
        haloRenderer.Material.Target = haloMaterial;

        return slot;
    }

    private void RefreshOptionalMarkers()
    {
        SetActive(_leftFootProxy.Target, CalibrateFeet.Value);
        SetActive(_rightFootProxy.Target, CalibrateFeet.Value);
        SetActive(_pelvisProxy.Target, CalibratePelvis.Value);
    }

    private static void SetActive(Slot slot, bool active)
    {
        if (slot != null && !slot.IsDestroyed)
            slot.ActiveSelf.Value = active;
    }

    // Grab the whole tool (figure + panel) by the panel's title bar. The root grab out-prioritizes the
    // title-bar handle (so aiming at the bar promotes up to it and moves everything) but sits below the
    // markers (priority 20), so aiming at a marker still moves just that marker.
    private void SetupWholeToolGrab()
    {
        var grab = Slot.GetComponent<Grabbable>() ?? Slot.AttachComponent<Grabbable>();
        grab.FollowRotation.Value = true;
        grab.Scalable.Value = true;
        grab.GrabPriority.Value = 10;
        grab.InteractionPriority.Value = 10;
    }

    // Two small eye markers on the headset, showing where the eyes sit and which way they face -
    // a visible cue for the eye drivers.
    private static void AddEyeBalls(Slot head)
    {
        if (head == null || head.IsDestroyed)
            return;
        AddEyeBall(head, -0.032f);
        AddEyeBall(head, 0.032f);
    }

    private static void AddEyeBall(Slot head, float xOffset)
    {
        var eye = head.AddSlot("Eye");
        eye.LocalPosition.Value = new float3(xOffset, 0.02f, 0.06f);   // just in front of the head marker
        var mesh = eye.AttachComponent<LumoraMeshes.SphereMesh>();
        mesh.Radius.Value = 0.014f;
        mesh.Segments.Value = 12;
        mesh.Rings.Value = 8;
        var renderer = eye.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        var material = eye.AttachComponent<UnlitMaterial>();
        material.Color = new colorHDR(1f, 1f, 1f, 1f);
        renderer.Material.Target = material;
    }

    // CONTROL PANEL (Helio canvas)

    private void BuildControlPanel()
    {
        var panelSlot = Slot.AddSlot("Controls");
        panelSlot.LocalPosition.Value = new float3(0.7f, 1.15f, 0f);

        var body = panelSlot.AddSlot("Body");
        body.LocalScale.Value = float3.One * CanvasScale;

        var size = new float2(360f, 470f);
        var rect = body.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(-size.x * 0.5f, -size.y * 0.5f);
        rect.OffsetMax.Value = new float2(size.x * 0.5f, size.y * 0.5f);
        body.AttachComponent<Canvas>();

        var background = body.AttachComponent<Image>();
        background.Tint.Value = PanelBg;

        var font = EnsureFont(panelSlot);

        var b = new UIBuilder(body);
        b.Font(font)
            .TextColor(TextPrimary)
            .ForegroundColor(Accent)
            .BackgroundColor(ControlFill);

        var layout = b.VerticalLayout(10f, 18f);
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;
        FillToParent(b.Current);

        SetRowHeight(b, 42f);
        var title = b.Text("Avatar Creator", 28f);
        title.HorizontalAlignment.Value = TextHorizontalAlignment.Center;

        SetRowHeight(b, 58f);
        var info = b.Text("Put the head marker on the avatar's head,\nthen Auto Align - or place each marker by hand.", 15f, TextDim);
        info.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        info.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        SetRowHeight(b, 46f);
        b.Button("Auto Align", (_, _) => AlignMarkersToRig(), AlignFill);

        SetRowHeight(b, 46f);
        b.Button("Create", (_, _) => RunCreate(), CreateFill);

        AddToggleRow(b, "Calibrate Feet", CalibrateFeet);
        AddToggleRow(b, "Calibrate Pelvis", CalibratePelvis);
        AddToggleRow(b, "Setup Eyes", SetupEyes);

        SetRowHeight(b, 46f);
        b.Button("Cancel", (_, _) => Slot.Destroy(), CancelFill);

        AddTitleBarHandle(panelSlot);
    }

    // A draggable title bar above the panel. It only needs to register a laser hit; the grab promotes
    // up the parent chain to the root Grabbable (SetupWholeToolGrab), so dragging it moves everything.
    private void AddTitleBarHandle(Slot panelSlot)
    {
        var handle = panelSlot.AddSlot("TitleBar");
        handle.LocalPosition.Value = new float3(0f, 0.3f, 0f);

        var box = handle.AttachComponent<LumoraMeshes.BoxMesh>();
        box.Size.Value = new float3(0.42f, 0.05f, 0.02f);
        var renderer = handle.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = box;
        var material = handle.AttachComponent<UnlitMaterial>();
        material.Color = new colorHDR(Accent.r, Accent.g, Accent.b, 0.9f);
        renderer.Material.Target = material;

        var grab = handle.AttachComponent<Grabbable>();
        grab.GrabPriority.Value = 1;          // below the root grab, so the whole tool moves
        grab.InteractionPriority.Value = 1;
    }

    // "Put the head on, align the rest": snap every marker onto its matching bone of the rig the markers
    // are over, turning on feet/pelvis when the rig has those bones.
    private void AlignMarkersToRig()
    {
        var rig = FindRig();
        if (rig == null || rig.IsDestroyed)
        {
            LumoraLogger.Warn("AvatarCreator: nothing to align to - put the head marker over the avatar's head first");
            return;
        }

        if (rig.TryGetBone(BodyNode.LeftFoot) != null && rig.TryGetBone(BodyNode.RightFoot) != null)
            CalibrateFeet.Value = true;
        if (rig.TryGetBone(BodyNode.Hips) != null)
            CalibratePelvis.Value = true;

        AlignMarker(_headProxy.Target, rig.TryGetBone(BodyNode.Head));
        AlignMarker(_leftHandProxy.Target, rig.TryGetBone(BodyNode.LeftHand));
        AlignMarker(_rightHandProxy.Target, rig.TryGetBone(BodyNode.RightHand));
        AlignMarker(_leftFootProxy.Target, rig.TryGetBone(BodyNode.LeftFoot));
        AlignMarker(_rightFootProxy.Target, rig.TryGetBone(BodyNode.RightFoot));
        AlignMarker(_pelvisProxy.Target, rig.TryGetBone(BodyNode.Hips));
    }

    private static void AlignMarker(Slot marker, Slot bone)
    {
        if (marker == null || marker.IsDestroyed || bone == null || bone.IsDestroyed)
            return;
        marker.GlobalPosition = bone.GlobalPosition;
    }

    private void AddToggleRow(UIBuilder b, string label, Sync<bool> state)
    {
        SetRowHeight(b, 40f);
        b.HorizontalElementWithLabel(label, 0.72f,
            () => b.Checkbox(state.Value, (_, on) => state.Value = on, ControlFill));
    }

    private static void SetRowHeight(UIBuilder b, float height)
    {
        b.MinHeight(height).PreferredHeight(height).FlexibleHeight(0f);
    }

    private static void FillToParent(Slot slot)
    {
        var rect = slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private static FontProvider EnsureFont(Slot panelSlot)
    {
        var fontSlot = panelSlot.AddSlot("Font");
        var provider = fontSlot.AttachComponent<FontProvider>();
        if (ImportDialog.DefaultFontUrl != null)
        {
            provider.URL.Value = ImportDialog.DefaultFontUrl;
            provider.FallbackURLs.Add(ImportDialog.DefaultFontUrl);
        }
        return provider;
    }

    // CREATE

    /// <summary>Find the rigged model under the markers, wire up the avatar runtime, click-to-equip.</summary>
    public void RunCreate()
    {
        var rig = FindRig();
        if (rig == null || rig.IsDestroyed)
        {
            LumoraLogger.Warn("AvatarCreator: no rigged model found under the markers - line them up and try again");
            return;
        }

        var avatar = ResolveObjectRoot(rig.Slot);
        if (avatar == null || avatar.IsDestroyed)
            return;

        var skeleton = avatar.GetComponentInChildren<SkeletonBuilder>() ?? rig.Slot.GetComponentInChildren<SkeletonBuilder>();
        if (skeleton == null)
        {
            LumoraLogger.Warn("AvatarCreator: rig has no skeleton; cannot create");
            return;
        }

        // Strip the import-time authoring helpers: per-bone pose handles (grab + collider + visual),
        // any remaining whole-model grab, and object roots. The finished avatar gets a single root
        // grab via the equip path instead.
        AvatarRigSetup.RemovePoseHandles(rig);
        foreach (var grab in avatar.GetComponentsInChildren<Grabbable>())
            grab.Destroy();
        foreach (var objectRoot in avatar.GetComponentsInChildren<ObjectRoot>())
            objectRoot.Destroy();

        if (avatar.GetComponent<AvatarRoot>() == null)
            avatar.AttachComponent<AvatarRoot>();

        var avatarIk = avatar.GetComponent<AvatarIK>() ?? avatar.AttachComponent<AvatarIK>();
        avatarIk.Skeleton.Target = skeleton;
        avatarIk.Rig.Target = rig;

        // Calibrate from the rig: build the View/hand/foot reference points AvatarIK reads. WITHOUT
        // these the view offset is invalid and the head bone is driven to the raw camera pose - the
        // head won't track right and the body faces the wrong way. The View frame faces the body's
        // computed forward, so the avatar ends up facing where the camera looks.
        AvatarCalibration.AutoPlaceReferences(avatar, rig, CalibrateFeet.Value, CalibratePelvis.Value);

        if (SetupEyes.Value && (rig.TryGetBone(BodyNode.LeftEye) != null || rig.TryGetBone(BodyNode.RightEye) != null))
        {
            if (avatar.GetComponent<BlinkDriver>() == null)
                avatar.AttachComponent<BlinkDriver>();
            if (avatar.GetComponent<EyeRotationDriver>() == null)
                avatar.AttachComponent<EyeRotationDriver>();
        }

        if (avatar.GetComponent<VisemeAnalyzer>() == null)
            avatar.AttachComponent<VisemeAnalyzer>();
        if (avatar.GetComponent<DirectVisemeDriver>() == null)
            avatar.AttachComponent<DirectVisemeDriver>();

        EnsureEquipTarget(avatar);
        LumoraLogger.Log($"AvatarCreator: created avatar '{avatar.SlotName.Value}' - click it to equip");
        Slot.Destroy();
    }

    // Find the rig the markers are lined up over. A transform scan, NOT a physics overlap: the
    // collision query backend (Godot/Jolt) only sees registered bodies, and an imported avatar's bone
    // colliders aren't necessarily in it - so we match bone slot positions directly. Any skinned model
    // gets a BipedRig built on demand (a plain import has a SkeletonBuilder but no rig), so Create works
    // on any imported humanoid, not just avatar-flagged ones.
    private BipedRig FindRig()
    {
        var world = World;
        if (world?.RootSlot == null)
            return null!;

        var rigs = new List<BipedRig>();
        foreach (var skeleton in world.RootSlot.GetComponentsInChildren<SkeletonBuilder>())
        {
            if (!IsCandidate(skeleton?.Slot))
                continue;
            var rig = EnsureRig(skeleton!);
            if (rig != null && !rigs.Contains(rig))
                rigs.Add(rig);
        }
        foreach (var rig in world.RootSlot.GetComponentsInChildren<BipedRig>())
        {
            if (rig == null || rig.IsDestroyed || !IsCandidate(rig.Slot))
                continue;
            if (!rigs.Contains(rig))
                rigs.Add(rig);
        }

        if (rigs.Count == 0)
        {
            LumoraLogger.Warn("AvatarCreator: no rigged/skinned model in the world - import a humanoid model first");
            return null!;
        }

        BipedRig? best = null;
        int bestScore = 0;
        foreach (var rig in rigs)
        {
            int score = ScoreRig(rig);
            if (score > bestScore)
            {
                bestScore = score;
                best = rig;
            }
        }
        if (best != null)
            return best;

        // Nothing lined up under the markers, but there's exactly one candidate - the user clearly
        // spawned the creator to set up the one avatar present, so use it.
        if (rigs.Count == 1)
            return rigs[0];

        LumoraLogger.Warn("AvatarCreator: markers aren't over any of the rigged models - line them up over the avatar");
        return null!;
    }

    private bool IsCandidate(Slot? slot)
        => slot != null && !slot.IsDestroyed && !slot.IsDescendantOf(Slot) && slot.ActiveUserRoot == null;

    // Get the model's rig, building one from its skeleton if it doesn't have one yet (mirrors what the
    // avatar-import path does). Returns null if no usable bones could be mapped.
    private static BipedRig? EnsureRig(SkeletonBuilder skeleton)
    {
        var rig = skeleton.Slot.GetComponent<BipedRig>();
        if (rig == null)
        {
            rig = skeleton.Slot.AttachComponent<BipedRig>();
            rig.PopulateFromSkeleton(skeleton);
            LumoraLogger.Log($"AvatarCreator: built a BipedRig from '{skeleton.Slot.SlotName.Value}' (IsBiped={rig.IsBiped}, {rig.Bones.Count} bones)");
        }
        if (rig.Bones.Count > 0)
            AvatarRigSetup.SetupPoseHandles(rig);   // make its bones grabbable/poseable if they aren't already
        return rig.Bones.Count > 0 ? rig : null;
    }

    // +1 per marker sitting over its matching bone. Feet/pelvis only count when their toggle is on.
    private int ScoreRig(BipedRig rig)
    {
        int score = 0;
        score += MarkerOverBone(_headProxy.Target, rig.TryGetBone(BodyNode.Head), HeadDetectionRadius) ? 1 : 0;
        score += MarkerOverBone(_leftHandProxy.Target, rig.TryGetBone(BodyNode.LeftHand), LimbDetectionRadius) ? 1 : 0;
        score += MarkerOverBone(_rightHandProxy.Target, rig.TryGetBone(BodyNode.RightHand), LimbDetectionRadius) ? 1 : 0;
        if (CalibrateFeet.Value)
        {
            score += MarkerOverBone(_leftFootProxy.Target, rig.TryGetBone(BodyNode.LeftFoot), LimbDetectionRadius) ? 1 : 0;
            score += MarkerOverBone(_rightFootProxy.Target, rig.TryGetBone(BodyNode.RightFoot), LimbDetectionRadius) ? 1 : 0;
        }
        if (CalibratePelvis.Value)
            score += MarkerOverBone(_pelvisProxy.Target, rig.TryGetBone(BodyNode.Hips), LimbDetectionRadius) ? 1 : 0;
        return score;
    }

    // The detection radius tracks the visible halo, which scales with the marker (so scaling the figure
    // up keeps the cue and the test in sync).
    private static bool MarkerOverBone(Slot marker, Slot bone, float radius)
    {
        if (marker == null || marker.IsDestroyed || bone == null || bone.IsDestroyed)
            return false;
        float scaled = radius * MathF.Max(marker.GlobalScale.x, 0.0001f);
        return (marker.GlobalPosition - bone.GlobalPosition).LengthSquared <= scaled * scaled;
    }

    // Walk up from the rig to the model's logical root: the highest ObjectRoot ancestor, or failing
    // that the topmost non-world ancestor (the import root). That's where the avatar runtime lives.
    private static Slot ResolveObjectRoot(Slot from)
    {
        Slot best = from;
        Slot withRoot = null!;
        for (var s = from; s != null && !s.IsRootSlot; s = s.Parent)
        {
            best = s;
            if (s.GetComponent<ObjectRoot>() != null)
                withRoot = s;
        }
        return withRoot ?? best;
    }

    // CLICK-TO-EQUIP

    private static void EnsureEquipTarget(Slot avatar)
    {
        if (avatar.GetComponent<RayTarget>() != null)
            return;
        var target = avatar.AttachComponent<RayTarget>();
        target.HoverRadius.Value = 0.5f;
        target.Activated += _ => TryEquip(avatar);
    }

    private static void TryEquip(Slot avatar)
    {
        var userRoot = avatar.World?.LocalUser?.Root;
        if (userRoot == null)
        {
            LumoraLogger.Warn("AvatarCreator: no local user root to equip onto");
            return;
        }
        var manager = userRoot.Slot.GetComponent<AvatarManager>() ?? userRoot.Slot.AttachComponent<AvatarManager>();
        if (manager.UserRoot.Target == null)
            manager.UserRoot.Target = userRoot;
        manager.EquipAvatar(avatar);
    }
}
