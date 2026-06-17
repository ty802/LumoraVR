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
using Lumora.Core.Components.UI;
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

    /// <summary>Show per-marker direction arrows (local X=red/right, Y=green/up, Z=blue/forward).</summary>
    public readonly Sync<bool> ShowDirections = new();

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

    // Checkbox box fill - bright enough to read against the dark panel (the check mark is lighter still).
    private static readonly color ControlFill = new(0.50f, 0.45f, 0.70f, 1f);

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
        ShowDirections.OnChanged += _ => RefreshDirectionArrows();
        RefreshOptionalMarkers();
        RefreshDirectionArrows();
    }

    // Stand the figure on the floor in front of the local user, facing them, so the markers overlay an
    // avatar imported in the same spot. Local marker offsets are then rotated into place by the slot.
    private void AnchorToUser()
    {
        var user = World?.LocalUser?.Root;
        if (user == null)
            return;

        // View direction is the head's LOCAL -Z (Godot camera convention) = float3.Backward; float3.Forward
        // (+Z) points behind the user, which is why the creator used to spawn behind them. - xlinka
        var forward = user.HeadRotation * float3.Backward;
        forward.y = 0f;
        forward = forward.LengthSquared > 1e-5f ? forward.Normalized : float3.Backward;

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

        BuildAxisGizmo(slot);
        return slot;
    }

    // Three thin bars on the marker's local axes (X=red/right, Y=green/up, Z=blue/forward) so you can
    // see which way a marker is oriented before aligning. Toggled by ShowDirections. - xlinka
    private static void BuildAxisGizmo(Slot marker)
    {
        var axes = marker.AddSlot("Axes");
        axes.ActiveSelf.Value = false;
        AddAxisBar(axes, "X", float3.Right, new colorHDR(1f, 0.25f, 0.25f, 1f));
        AddAxisBar(axes, "Y", float3.Up, new colorHDR(0.3f, 1f, 0.35f, 1f));
        AddAxisBar(axes, "Z", float3.Backward, new colorHDR(0.35f, 0.55f, 1f, 1f)); // -Z is our view/forward
    }

    private static void AddAxisBar(Slot parent, string name, float3 dir, colorHDR color)
    {
        const float length = 0.18f;
        const float thickness = 0.012f;
        var bar = parent.AddSlot(name);
        bar.LocalPosition.Value = dir * (length * 0.5f);
        bar.LocalRotation.Value = FabrikSolver.FromToRotation(float3.Up, dir);

        var mesh = bar.AttachComponent<LumoraMeshes.BoxMesh>();
        mesh.Size.Value = new float3(thickness, length, thickness);
        var renderer = bar.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        var material = bar.AttachComponent<UnlitMaterial>();
        material.Color = color;
        renderer.Material.Target = material;
    }

    private void RefreshDirectionArrows()
    {
        foreach (var proxy in EnumerateMarkers())
        {
            var axes = proxy?.FindChild("Axes");
            if (axes != null && !axes.IsDestroyed)
                axes.ActiveSelf.Value = ShowDirections.Value;
        }
    }

    private IEnumerable<Slot> EnumerateMarkers()
    {
        yield return _headProxy.Target;
        yield return _leftHandProxy.Target;
        yield return _rightHandProxy.Target;
        yield return _leftFootProxy.Target;
        yield return _rightFootProxy.Target;
        yield return _pelvisProxy.Target;
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
        // Proper themed Helio panel (PanelShell): rounded, purple, with a header + close button -
        // not a bare canvas with a mesh bar. UITheme carries the dashboard palette + rounded sprite.
        var panelSlot = Slot.AddSlot("Controls");
        panelSlot.LocalPosition.Value = new float3(0.7f, 1.15f, 0f);
        panelSlot.LocalScale.Value = float3.One * CanvasScale;

        var theme = panelSlot.AttachComponent<UITheme>();

        var panel = panelSlot.AttachComponent<PanelShell>();
        panel.Title.Value = "Avatar Creator";
        // Tall enough for the info line + 5 action buttons + 4 toggles without compressing or overflowing.
        panel.Size.Value = new float2(380f, 610f);
        panel.TitleTextSize.Value = 20f;
        panel.HeaderHeight.Value = 50f;
        // The whole-tool root grab (SetupWholeToolGrab) moves the panel; no separate panel-only grab.
        panel.AllowGrab.Value = false;
        panel.Scalable.Value = false;
        theme.ApplyTo(panel);
        panel.CloseRequested += _ => Slot.Destroy();

        var content = panel.ContentSlot!;
        var b = new UIBuilder(content);
        b.Font(theme.ThemeFont)
            .TextColor(theme.TextPrimary.Value)
            .ForegroundColor(theme.Accent.Value)
            .BackgroundColor(theme.ButtonFill.Value)
            .RoundedSprite(theme.RoundedSprite);   // rounded button corners, matching the panel

        var layout = b.VerticalLayout(8f, 4f);
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;
        layout.PaddingTop.Value = 16f;             // breathing room under the header
        FillToParent(b.Current);

        SetRowHeight(b, 44f);
        var info = b.Text("Place the head marker on the model,\nthen align and Create.", 14f, theme.TextDim.Value);
        info.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        info.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        // Per-part align: place a marker on the model, then snap it to the matching bone.
        SetRowHeight(b, 42f);
        b.Button("Center Head", (_, _) => AlignHead(), theme.ButtonFill.Value);

        SetRowHeight(b, 42f);
        b.Button("Align Hands", (_, _) => AlignHands(), theme.ButtonFill.Value);

        SetRowHeight(b, 42f);
        b.Button("Align Body", (_, _) => AlignBody(), theme.ButtonFill.Value);

        SetRowHeight(b, 42f);
        b.Button("Align All", (_, _) => AlignMarkersToRig(), theme.Accent.Value);

        SetRowHeight(b, 48f);
        b.Button("Create", (_, _) => RunCreate(), theme.PositiveFill.Value);

        AddToggleRow(b, "Calibrate Feet", CalibrateFeet);
        AddToggleRow(b, "Calibrate Pelvis", CalibratePelvis);
        AddToggleRow(b, "Setup Eyes", SetupEyes);
        AddToggleRow(b, "Show Directions", ShowDirections);
    }

    // "Align All": snap every marker onto its matching bone in one click (each step finds the rig).
    private void AlignMarkersToRig()
    {
        AlignHead();
        AlignHands();
        AlignBody();
    }

    // Snap the head marker onto the model's head bone (position + orientation) - the "Center Head"
    // action. Find the rig fresh each click so you can place the marker then align. - xlinka
    private void AlignHead()
    {
        var rig = RequireRig();
        if (rig == null) return;
        AlignMarker(_headProxy.Target, rig.TryGetBone(BodyNode.Head), copyRotation: true);
    }

    // Snap both hand markers onto the hand bones WITH orientation, so the IK wrists line up with the
    // model's hands (position-only left the palms rolled wrong - "get the hands right"). - xlinka
    private void AlignHands()
    {
        var rig = RequireRig();
        if (rig == null) return;
        AlignMarker(_leftHandProxy.Target, rig.TryGetBone(BodyNode.LeftHand), copyRotation: true);
        AlignMarker(_rightHandProxy.Target, rig.TryGetBone(BodyNode.RightHand), copyRotation: true);
    }

    // Snap pelvis + feet markers, turning on those calibration options when the rig has the bones.
    private void AlignBody()
    {
        var rig = RequireRig();
        if (rig == null) return;
        if (rig.TryGetBone(BodyNode.LeftFoot) != null && rig.TryGetBone(BodyNode.RightFoot) != null)
            CalibrateFeet.Value = true;
        if (rig.TryGetBone(BodyNode.Hips) != null)
            CalibratePelvis.Value = true;
        AlignMarker(_pelvisProxy.Target, rig.TryGetBone(BodyNode.Hips), copyRotation: true);
        AlignMarker(_leftFootProxy.Target, rig.TryGetBone(BodyNode.LeftFoot));
        AlignMarker(_rightFootProxy.Target, rig.TryGetBone(BodyNode.RightFoot));
    }

    private BipedRig RequireRig()
    {
        var rig = FindRig();
        if (rig == null || rig.IsDestroyed)
        {
            LumoraLogger.Warn("AvatarCreator: nothing to align to - put the head marker over the avatar's head first");
            return null!;
        }
        return rig;
    }

    private static void AlignMarker(Slot marker, Slot bone, bool copyRotation = false)
    {
        if (marker == null || marker.IsDestroyed || bone == null || bone.IsDestroyed)
            return;
        marker.GlobalPosition = bone.GlobalPosition;
        if (copyRotation)
            marker.GlobalRotation = bone.GlobalRotation;
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
