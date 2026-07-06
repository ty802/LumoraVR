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
[ComponentCategory("Users/Avatar")]
public sealed class AvatarStudio : Component
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

        ShowDirections.Value = true; // forward arrows on by default so orientation is visible while building

        AnchorToUser();
        BuildMarkers();
        BuildControlPanel();

        CalibrateFeet.OnChanged += _ => RefreshOptionalMarkers();
        CalibratePelvis.OnChanged += _ => RefreshOptionalMarkers();
        ShowDirections.OnChanged += _ => RefreshDirectionArrows();

        // The creator is opened for an avatar that's ALREADY in the world, so snap the markers straight onto its
        // bones now - the head marker lands on the real head bone instead of the guessed 1.8m default, killing
        // the "head too low / wrong height" mismatch for short rigs like the fox. Optional markers toggle from
        // whatever bones the rig actually has, so refresh AFTER aligning. -xlinka
        TryAutoAlignToRig();
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
        // Face the user: the figure's forward is its local -Z (float3.Backward, our view/forward convention -
        // same axis the camera, HeadFacingDirection and the green arrows use), so point THAT back at the user
        // (-forward = figure->user). Using float3.Forward (+Z) here pointed the figure's real front the same way
        // the user was looking, so you spawned staring at the avatar's back and the arrows pointed away. With
        // -Z toward the user the figure faces you and left/right mirror correctly (their right = your left).
        // FromToRotation, not floatQ.LookRotation - that returns the inverse. - xlinka
        Slot.GlobalRotation = FabrikSolver.FromToRotation(float3.Backward, -forward);
    }

    // MARKERS

    private void BuildMarkers()
    {
        if (_built)
            return;
        _built = true;

        // Standing layout for an average humanoid (head ~1.8m); TryAutoAlignToRig then snaps every marker onto
        // the imported model's bones so short/tall rigs land right. The arrow direction is per-part: head/feet/
        // pelvis show their FORWARD (local -Z, the facing axis); hands show their OUTWARD reach (away from the
        // body) since a hand's useful axis runs down the arm, not where the palm faces.
        _headProxy.Target = AddMarker("Headset", new float3(0f, 1.8f, 0f), HeadDetectionRadius, HeadFill, float3.Backward);
        AddEyeBalls(_headProxy.Target);
        _leftHandProxy.Target = AddMarker("LeftHand", new float3(-0.35f, 1.0f, 0f), LimbDetectionRadius, LeftFill, float3.Left);
        _rightHandProxy.Target = AddMarker("RightHand", new float3(0.35f, 1.0f, 0f), LimbDetectionRadius, RightFill, float3.Right);
        _pelvisProxy.Target = AddMarker("Pelvis", new float3(0f, 1.0f, 0f), LimbDetectionRadius, PelvisFill, float3.Backward);
        _leftFootProxy.Target = AddMarker("LeftFoot", new float3(-0.12f, 0.1f, 0f), LimbDetectionRadius, LeftFill, float3.Backward);
        _rightFootProxy.Target = AddMarker("RightFoot", new float3(0.12f, 0.1f, 0f), LimbDetectionRadius, RightFill, float3.Backward);
    }

    private Slot AddMarker(string name, float3 localPos, float detectionRadius, colorHDR fill, float3 arrowDir)
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

        BuildAxisGizmo(slot, arrowDir);
        return slot;
    }

    // One GREEN direction arrow per marker, pointing along the part's chosen local axis (head/feet/pelvis =
    // forward/-Z; hands = outward along the arm). It shows which way that orb is oriented while aligning: if a
    // marker's arrow points somewhere the part clearly shouldn't, that bone is authored off and needs the Align
    // buttons. Toggled by ShowDirections. - xlinka
    private static void BuildAxisGizmo(Slot marker, float3 dir)
    {
        var axes = marker.AddSlot("Axes");

        var green = new colorHDR(0.15f, 1f, 0.25f, 1f);
        var mat = axes.AttachComponent<UnlitMaterial>();
        mat.Color = green;

        const float len = 0.22f;
        const float shaftLen = len * 0.74f;
        const float headLen = len - shaftLen;
        const float r = 0.01f;
        dir = dir.LengthSquared > 1e-6f ? dir.Normalized : float3.Backward;

        var shaft = axes.AddSlot("Shaft");
        shaft.LocalRotation.Value = FabrikSolver.FromToRotation(float3.Up, dir);
        shaft.LocalPosition.Value = dir * (shaftLen * 0.5f);
        var cyl = shaft.AttachComponent<LumoraMeshes.CylinderMesh>();
        cyl.Radius.Value = r;
        cyl.Height.Value = shaftLen;
        var sr = shaft.AttachComponent<MeshRenderer>();
        sr.Mesh.Target = cyl;
        sr.Material.Target = mat;

        var head = axes.AddSlot("Head");
        head.LocalRotation.Value = FabrikSolver.FromToRotation(float3.Up, dir);
        head.LocalPosition.Value = dir * (shaftLen + headLen * 0.5f);
        var cone = head.AttachComponent<LumoraMeshes.ConeMesh>();
        cone.RadiusBase.Value = r * 2.8f;
        cone.RadiusTop.Value = 0f;
        cone.Height.Value = headLen;
        var hr = head.AttachComponent<MeshRenderer>();
        hr.Mesh.Target = cone;
        hr.Material.Target = mat;
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
        eye.LocalPosition.Value = new float3(xOffset, 0.02f, -0.06f);   // local -Z is view/front
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
        // The tool root faces the user (its -Z points at them so the figure looks back), and the canvas reads
        // along its +Z, so without this it would face the same way as the figure - away from the user. Yaw it
        // 180 so the controls face you, with a slight downward tilt for reading at chest height. Our floatQ.Euler
        // takes RADIANS in (yaw, pitch, roll) order, not degrees. -xlinka
        const float deg = MathF.PI / 180f;
        panelSlot.LocalRotation.Value = floatQ.Euler(180f * deg, 10f * deg, 0f);

        var theme = panelSlot.AttachComponent<UITheme>();

        var panel = panelSlot.AttachComponent<PanelShell>();
        panel.Title.Value = "Avatar Studio";
        // Tall enough for the info line + 8 action buttons + 4 toggles without compressing or overflowing.
        panel.Size.Value = new float2(380f, 760f);
        panel.TitleTextSize.Value = 20f;
        panel.HeaderHeight.Value = 50f;
        // The panel grabs by its OWN title bar and moves ONLY itself - the markers are independent siblings, so
        // repositioning the controls never drags the orbs or rotates their direction arrows. There is no
        // whole-tool grab: you place each marker on the model individually, like the avatar's own parts.
        panel.AllowGrab.Value = true;
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
        var info = b.Text("Drop the head marker on the model's head,\nalign, then Create.", 14f, theme.TextDim.Value);
        info.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        info.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        // Head frame: Center Head snaps the marker onto the head bone; the three Align buttons snap the marker's
        // view axes (forward/up/right) onto the head bone's NEAREST axis, so a skull authored facing any
        // direction still produces a clean upright view frame (the imported roll never reaches the camera).
        SetRowHeight(b, 42f);
        b.Button("Center Head", (_, _) => AlignHeadPosition(), theme.ButtonFill.Value);

        SetRowHeight(b, 42f);
        b.Button("Align Forward", (_, _) => AlignHeadAxis(forward: true, up: false, right: false), theme.ButtonFill.Value);

        SetRowHeight(b, 42f);
        b.Button("Align Up", (_, _) => AlignHeadAxis(forward: false, up: true, right: false), theme.ButtonFill.Value);

        SetRowHeight(b, 42f);
        b.Button("Align Right", (_, _) => AlignHeadAxis(forward: false, up: false, right: true), theme.ButtonFill.Value);

        // Limb markers snap onto the matching bones (hands + pelvis carry rotation; feet are position-only).
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

    // Best-effort snap of every marker onto the computed avatar reference frames at spawn (the avatar already
    // exists when the creator opens). This keeps imported bone roll out of the markers: head/pelvis/feet use
    // geometric body forward, hands use the forearm chain instead of raw wrist rotation. -xlinka
    private void TryAutoAlignToRig()
    {
        var rig = FindRig();
        if (rig == null || rig.IsDestroyed)
            return;

        var avatar = ResolveAvatarRoot(rig);
        if (avatar == null || avatar.IsDestroyed)
            return;

        AlignMarkersToComputedReferences(avatar, rig);
    }

    // "Align All": snap every marker onto the same computed reference frames Create will write.
    private void AlignMarkersToRig()
    {
        var rig = RequireRig();
        if (rig == null) return;
        var avatar = ResolveAvatarRoot(rig);
        if (avatar == null || avatar.IsDestroyed)
            return;
        AlignMarkersToComputedReferences(avatar, rig);
    }

    // Snap the headset/view marker onto the model's head bone POSITION only. Orientation is handled by the
    // Align Forward/Up/Right buttons, so you can recentre without losing a hand-tuned facing and vice-versa.
    private void AlignHeadPosition()
    {
        var rig = RequireRig();
        if (rig == null) return;
        AlignMarker(_headProxy.Target, rig.TryGetBone(BodyNode.Head));
    }

    // Snap the head marker's chosen view axis onto the head bone's NEAREST axis. The marker is the View/HMD
    // frame the avatar calibrates against, so this is what makes a back- or sideways-authored skull read
    // upright: GetClosestAxis ignores the bone's authored roll and just picks the local axis nearest where
    // you've aimed the marker, then we rotate that one axis onto it. Off-axis tilt is flattened (forward and
    // right stay level, up stays vertical) so aligning one axis never pitches the others. -xlinka
    private void AlignHeadAxis(bool forward, bool up, bool right)
    {
        var rig = RequireRig();
        if (rig == null) return;
        var head = _headProxy.Target;
        var bone = rig.TryGetBone(BodyNode.Head);
        if (head == null || head.IsDestroyed || bone == null || bone.IsDestroyed)
            return;

        if (forward)
            SnapMarkerAxis(head, bone, head.Backward, flattenLocalY: true);  // view forward is local -Z; keep level
        if (up)
            SnapMarkerAxis(head, bone, head.Up, flattenLocalZ: true);        // up stays out of the forward plane
        if (right)
            SnapMarkerAxis(head, bone, head.Right, flattenLocalY: true);     // right stays level
    }

    // Rotate `marker` so its current `fromWorld` axis points along the bone axis nearest to it, optionally
    // flattening the chosen direction in the marker's OWN frame (drop local Y to keep it level, or local Z to
    // keep it out of the forward plane) so the snap doesn't pitch/roll the marker's other axes.
    private static void SnapMarkerAxis(Slot marker, Slot bone, float3 fromWorld, bool flattenLocalY = false, bool flattenLocalZ = false)
    {
        if (fromWorld.LengthSquared < 1e-8f)
            return;
        fromWorld = fromWorld.Normalized;
        float3 axis = GetClosestAxis(bone, fromWorld);
        float3 local = marker.GlobalDirectionToLocal(axis);
        if (flattenLocalY) local.y = 0f;
        if (flattenLocalZ) local.z = 0f;
        float3 toWorld = marker.LocalDirectionToGlobal(local);
        if (toWorld.LengthSquared < 1e-8f)
            return;
        marker.GlobalRotation = FabrikSolver.FromToRotation(fromWorld, toWorld.Normalized) * marker.GlobalRotation;
    }

    private static readonly float3[] LocalAxes = [float3.Right, float3.Up, float3.Forward];

    // The bone's local axis (one of +-X/+-Y/+-Z, returned in world space) that best lines up with worldDir.
    // Lets the head frame lock onto however the skull bone was actually authored instead of assuming a
    // convention. -xlinka
    private static float3 GetClosestAxis(Slot slot, float3 worldDir)
    {
        floatQ rot = slot.GlobalRotation;
        float3 best = worldDir;
        float bestAbs = -1f;
        foreach (var local in LocalAxes)
        {
            float3 world = rot * local;
            float d = float3.Dot(world, worldDir);
            float a = d < 0f ? -d : d;
            if (a > bestAbs)
            {
                bestAbs = a;
                best = d < 0f ? -world : world;
            }
        }
        return best.LengthSquared > 1e-8f ? best.Normalized : worldDir;
    }

    // Snap both hand markers onto computed grip frames. Raw imported hand-bone rotations are often rolled
    // sideways, so the grip axes come from the forearm chain instead. -xlinka
    private void AlignHands()
    {
        var rig = RequireRig();
        if (rig == null) return;
        var avatar = ResolveAvatarRoot(rig);
        if (avatar == null || avatar.IsDestroyed)
            return;
        ApplyMarkerPose(_leftHandProxy.Target, avatar, AvatarCalibration.ComputeHandGrip(avatar, rig, rightSide: false));
        ApplyMarkerPose(_rightHandProxy.Target, avatar, AvatarCalibration.ComputeHandGrip(avatar, rig, rightSide: true));
    }

    // Snap pelvis + feet markers from geometric body-forward frames, turning on those calibration options when
    // the rig has the bones.
    private void AlignBody()
    {
        var rig = RequireRig();
        if (rig == null) return;
        var avatar = ResolveAvatarRoot(rig);
        if (avatar == null || avatar.IsDestroyed)
            return;
        if (rig.TryGetBone(BodyNode.LeftFoot) != null && rig.TryGetBone(BodyNode.RightFoot) != null)
            CalibrateFeet.Value = true;
        if (rig.TryGetBone(BodyNode.Hips) != null)
            CalibratePelvis.Value = true;
        ApplyMarkerPose(_pelvisProxy.Target, avatar, AvatarCalibration.ComputePelvis(avatar, rig));
        ApplyMarkerPose(_leftFootProxy.Target, avatar, AvatarCalibration.ComputeFoot(avatar, rig, rightSide: false));
        ApplyMarkerPose(_rightFootProxy.Target, avatar, AvatarCalibration.ComputeFoot(avatar, rig, rightSide: true));
    }

    private void AlignMarkersToComputedReferences(Slot avatar, HumanoidRig rig)
    {
        // Root frame onto the body front (world-invariant, nothing visibly moves) so the references and the
        // equip facing agree. Heals avatars set up before this existed; no-op when already aligned.
        AvatarCalibration.AlignAvatarFacing(avatar, rig);

        bool hasFeet = rig.TryGetBone(BodyNode.LeftFoot) != null && rig.TryGetBone(BodyNode.RightFoot) != null;
        bool hasPelvis = rig.TryGetBone(BodyNode.Hips) != null;
        if (hasFeet)
            CalibrateFeet.Value = true;
        if (hasPelvis)
            CalibratePelvis.Value = true;

        ApplyMarkerPose(_headProxy.Target, avatar, AvatarCalibration.ComputeView(avatar, rig));
        ApplyMarkerPose(_leftHandProxy.Target, avatar, AvatarCalibration.ComputeHandGrip(avatar, rig, rightSide: false));
        ApplyMarkerPose(_rightHandProxy.Target, avatar, AvatarCalibration.ComputeHandGrip(avatar, rig, rightSide: true));
        if (hasFeet)
        {
            ApplyMarkerPose(_leftFootProxy.Target, avatar, AvatarCalibration.ComputeFoot(avatar, rig, rightSide: false));
            ApplyMarkerPose(_rightFootProxy.Target, avatar, AvatarCalibration.ComputeFoot(avatar, rig, rightSide: true));
        }
        if (hasPelvis)
            ApplyMarkerPose(_pelvisProxy.Target, avatar, AvatarCalibration.ComputePelvis(avatar, rig));

        RefreshOptionalMarkers();
        RefreshDirectionArrows();
        LumoraLogger.Log($"AvatarStudio: auto-aligned markers from computed references on '{avatar.SlotName.Value}'");
    }

    private static void ApplyMarkerPose(Slot marker, Slot avatar, in AvatarCalibration.RefPose pose)
    {
        if (!pose.Valid || marker == null || marker.IsDestroyed || avatar == null || avatar.IsDestroyed)
            return;
        marker.GlobalPosition = avatar.LocalPointToGlobal(pose.LocalPosition);
        marker.GlobalRotation = avatar.GlobalRotation * pose.LocalRotation;
    }

    // Yaw the pelvis marker so its forward (local -Z) points along the rig's GEOMETRIC forward - the body
    // facing measured from bone POSITIONS (shoulder line x hips->head), never a bone's authored rotation. This
    // is the fix for "hips wrong way round": the hips bone on imported anthro rigs is often authored facing
    // behind the body, so copying its rotation spun the marker and its arrow backwards. The geometric forward
    // can't be fooled by that. Stays upright (horizontal yaw only). -xlinka
    private void FacePelvisForward(HumanoidRig rig)
    {
        var pelvis = _pelvisProxy.Target;
        if (pelvis == null || pelvis.IsDestroyed)
            return;
        float3? fwd = rig.ForwardAxis.Value ?? rig.GuessForwardAxis();
        if (!fwd.HasValue || fwd.Value.LengthSquared < 1e-8f)
            return;
        pelvis.GlobalRotation = FabrikSolver.FromToRotation(pelvis.Backward, fwd.Value.Normalized) * pelvis.GlobalRotation;
    }

    private HumanoidRig RequireRig()
    {
        var rig = FindRig();
        if (rig == null || rig.IsDestroyed)
        {
            LumoraLogger.Warn("AvatarStudio: nothing to align to - put the head marker over the avatar's head first");
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
            LumoraLogger.Warn("AvatarStudio: no rigged model found under the markers - line them up and try again");
            return;
        }

        var avatar = ResolveAvatarRoot(rig);
        if (avatar == null || avatar.IsDestroyed)
            return;

        // The model import flow already builds a FULL avatar (AvatarForm + AvatarIK + references) on the model slot
        // when the rig is a biped. ResolveObjectRoot returns the OBJECT-ROOT wrapper, which is a different slot, so
        // attaching here would leave TWO AvatarIK driving the same rig - they fight every frame and contort/float the
        // body. Reuse the import-built avatar's slot as the canonical root, then sweep any stray duplicate
        // AvatarIK/AvatarForm off other slots in the subtree so exactly one solver remains. -xlinka
        foreach (var strayIk in new List<AvatarIK>(avatar.GetComponentsInChildren<AvatarIK>()))
            if (strayIk.Slot != avatar)
                strayIk.Destroy();
        foreach (var strayRoot in new List<AvatarForm>(avatar.GetComponentsInChildren<AvatarForm>()))
            if (strayRoot.Slot != avatar)
                strayRoot.Destroy();

        var skeleton = avatar.GetComponentInChildren<SkeletonBuilder>() ?? rig.Slot.GetComponentInChildren<SkeletonBuilder>();
        if (skeleton == null)
        {
            LumoraLogger.Warn("AvatarStudio: rig has no skeleton; cannot create");
            return;
        }

        // Strip the import-time authoring helpers: per-bone pose handles (grab + collider + visual),
        // any remaining whole-model grab, and object roots. The finished avatar gets a single root
        // grab via the equip path instead.
        AvatarRigSetup.RemovePoseHandles(rig);
        foreach (var grab in new List<Grabbable>(avatar.GetComponentsInChildren<Grabbable>()))
            grab.Destroy();
        foreach (var objectRoot in new List<ObjectRoot>(avatar.GetComponentsInChildren<ObjectRoot>()))
            objectRoot.Destroy();

        if (avatar.GetComponent<AvatarForm>() == null)
            avatar.AttachComponent<AvatarForm>();

        var avatarIk = avatar.GetComponent<AvatarIK>() ?? avatar.AttachComponent<AvatarIK>();
        avatarIk.Skeleton.Target = skeleton;
        avatarIk.Rig.Target = rig;

        // The root frame must face the body before references bake, since equip resets the root to identity.
        // World-invariant, so manually-adjusted markers stay exactly where the user put them.
        AvatarCalibration.AlignAvatarFacing(avatar, rig);

        // Write creator marker transforms as the avatar references. The
        // headset marker is the View/HMD frame; AvatarIK captures the head
        // bone relative to that frame, so arbitrary imported skull rotations
        // do not get copied directly onto the live camera rotation.
        if (!BuildReferencesFromMarkers(avatar))
            AvatarCalibration.AutoPlaceReferences(avatar, rig, CalibrateFeet.Value, CalibratePelvis.Value);

        // Make the finished (unworn) avatar grabbable: coarse body colliders to hit +
        // a single root Grabbable. BlockWhenWorn keeps it pickable on the ground / passable, but ungrabbable
        // once equipped under a user root. (Equipping is still its own action via the equip target/menu.)
        avatarIk.GenerateBodyColliders();
        var avatarGrab = avatar.GetComponent<Grabbable>() ?? avatar.AttachComponent<Grabbable>();
        avatarGrab.BlockWhenWorn.Value = true;

        if (SetupEyes.Value && (rig.TryGetBone(BodyNode.LeftEye) != null || rig.TryGetBone(BodyNode.RightEye) != null))
        {
            if (avatar.GetComponent<BlinkDriver>() == null)
                avatar.AttachComponent<BlinkDriver>();
            if (avatar.GetComponent<EyeGazeDriver>() == null)
                avatar.AttachComponent<EyeGazeDriver>();
            // Pupil dilation (procedural + tracked) plus widen/squint/eye-frown.
            if (avatar.GetComponent<EyeExpressionDriver>() == null)
                avatar.AttachComponent<EyeExpressionDriver>();
        }

        // Mouth/lip blendshapes driven from replicated face tracking (rests when nothing is tracking).
        if (avatar.GetComponent<MouthExpressionDriver>() == null)
            avatar.AttachComponent<MouthExpressionDriver>();

        if (avatar.GetComponent<LipSyncAnalyzer>() == null)
            avatar.AttachComponent<LipSyncAnalyzer>();
        if (avatar.GetComponent<VisemeWeightDriver>() == null)
            avatar.AttachComponent<VisemeWeightDriver>();
        if (avatar.GetComponent<BreathingDriver>() == null)
            avatar.AttachComponent<BreathingDriver>();

        EnsureEquipTarget(avatar);
        LumoraLogger.Log($"AvatarStudio: created avatar '{avatar.SlotName.Value}' - click it to equip");
        Slot.Destroy();
    }

    private bool BuildReferencesFromMarkers(Slot avatar)
    {
        if (avatar == null || avatar.IsDestroyed || _headProxy.Target == null || _headProxy.Target.IsDestroyed)
            return false;

        var existing = avatar.FindChild("AvatarReferences", recursive: false);
        if (existing != null && !existing.IsDestroyed)
            existing.Destroy();

        var root = avatar.AddSlot("AvatarReferences");
        root.LocalPosition.Value = float3.Zero;
        root.LocalRotation.Value = floatQ.Identity;

        PlaceReferenceFromMarker(root, avatar, _headProxy.Target, AvatarReferenceKind.View, "View");
        PlaceReferenceFromMarker(root, avatar, _leftHandProxy.Target, AvatarReferenceKind.LeftHandGrip, "LeftHandGrip");
        PlaceReferenceFromMarker(root, avatar, _rightHandProxy.Target, AvatarReferenceKind.RightHandGrip, "RightHandGrip");
        if (CalibrateFeet.Value)
        {
            PlaceReferenceFromMarker(root, avatar, _leftFootProxy.Target, AvatarReferenceKind.LeftFoot, "LeftFoot");
            PlaceReferenceFromMarker(root, avatar, _rightFootProxy.Target, AvatarReferenceKind.RightFoot, "RightFoot");
        }
        if (CalibratePelvis.Value)
            PlaceReferenceFromMarker(root, avatar, _pelvisProxy.Target, AvatarReferenceKind.Pelvis, "Pelvis");

        return true;
    }

    private static void PlaceReferenceFromMarker(Slot referenceRoot, Slot avatar, Slot marker, AvatarReferenceKind kind, string name)
    {
        if (referenceRoot == null || avatar == null || marker == null || marker.IsDestroyed)
            return;

        var slot = referenceRoot.AddSlot(name);
        slot.LocalPosition.Value = avatar.GlobalPointToLocal(marker.GlobalPosition);
        slot.LocalRotation.Value = avatar.GlobalRotation.Inverse * marker.GlobalRotation;
        slot.AttachComponent<AvatarReferencePoint>().Kind.Value = kind;
    }

    // Find the rig the markers are lined up over. A transform scan, NOT a physics overlap: the
    // collision query backend (Godot/Jolt) only sees registered bodies, and an imported avatar's bone
    // colliders aren't necessarily in it - so we match bone slot positions directly. Any skinned model
    // gets a HumanoidRig built on demand (a plain import has a SkeletonBuilder but no rig), so Create works
    // on any imported humanoid, not just avatar-flagged ones.
    private HumanoidRig FindRig()
    {
        var world = World;
        if (world?.RootSlot == null)
            return null!;

        var rigs = new List<HumanoidRig>();
        foreach (var skeleton in new List<SkeletonBuilder>(world.RootSlot.GetComponentsInChildren<SkeletonBuilder>()))
        {
            if (!IsCandidate(skeleton?.Slot))
                continue;
            var rig = EnsureRig(skeleton!);
            if (rig != null && !rigs.Contains(rig))
                rigs.Add(rig);
        }
        foreach (var rig in new List<HumanoidRig>(world.RootSlot.GetComponentsInChildren<HumanoidRig>()))
        {
            if (rig == null || rig.IsDestroyed || !IsCandidate(rig.Slot))
                continue;
            if (!rigs.Contains(rig))
                rigs.Add(rig);
        }

        if (rigs.Count == 0)
        {
            LumoraLogger.Warn("AvatarStudio: no rigged/skinned model in the world - import a humanoid model first");
            return null!;
        }

        HumanoidRig? best = null;
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

        LumoraLogger.Warn("AvatarStudio: markers aren't over any of the rigged models - line them up over the avatar");
        return null!;
    }

    private bool IsCandidate(Slot? slot)
        => slot != null && !slot.IsDestroyed && !slot.IsDescendantOf(Slot) && slot.ActiveUserRoot == null;

    // Get the model's rig, building one from its skeleton if it doesn't have one yet (mirrors what the
    // avatar-import path does). Returns null if no usable bones could be mapped.
    private static HumanoidRig? EnsureRig(SkeletonBuilder skeleton)
    {
        var rig = skeleton.Slot.GetComponent<HumanoidRig>();
        if (rig == null)
        {
            rig = skeleton.Slot.AttachComponent<HumanoidRig>();
            rig.PopulateFromSkeleton(skeleton);
            LumoraLogger.Log($"AvatarStudio: built a HumanoidRig from '{skeleton.Slot.SlotName.Value}' (IsHumanoid={rig.IsHumanoid}, {rig.Bones.Count} bones)");
        }
        if (rig.Bones.Count > 0)
            AvatarRigSetup.SetupPoseHandles(rig);   // make its bones grabbable/poseable if they aren't already
        return rig.Bones.Count > 0 ? rig : null;
    }

    // +1 per marker sitting over its matching bone. Feet/pelvis only count when their toggle is on.
    private int ScoreRig(HumanoidRig rig)
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

    private static Slot ResolveAvatarRoot(HumanoidRig rig)
    {
        if (rig == null || rig.IsDestroyed)
            return null!;
        var avatar = ResolveObjectRoot(rig.Slot);
        var existingIk = avatar.GetComponentInChildren<AvatarIK>();
        return existingIk != null && !existingIk.IsDestroyed ? existingIk.Slot : avatar;
    }

    // CLICK-TO-EQUIP

    private static void EnsureEquipTarget(Slot avatar)
    {
        if (avatar.GetComponent<RayTarget>() != null)
            return;
        var target = avatar.AttachComponent<RayTarget>();
        target.HoverRadius.Value = 0.5f;
        // Beat the root Grabbable for the laser's hovered target so a LEFT-click (use/interact) lands on this
        // RayTarget. Grab is GRIP/right-click and resolves the Grabbable by walking parents, so it's unaffected.
        // The avatar is touchable: left-click pops an equip confirm. - xlinka
        target.InteractionPriority.Value = 10;
        target.Activated += _ => ConfirmEquip(avatar);
    }

    // Left-click (use) on the avatar pops a small "Equip Avatar / Cancel" confirm, then equips - touch-to-equip.
    // Falls back to a direct equip if no context menu is available. - xlinka
    private static void ConfirmEquip(Slot avatar)
    {
        var userRootSlot = avatar.World?.LocalUser?.Root?.Slot;
        var menu = userRootSlot?.GetComponentInChildren<Lumora.Core.Components.UI.ContextMenuSystem>();
        if (userRootSlot == null || menu == null)
        {
            TryEquip(avatar);
            return;
        }

        // Idempotent: if a menu is already open, don't re-open. The activation can fire repeatedly while the laser
        // sits on the avatar, and re-opening rebuilds the whole menu visual every frame (the spam in the log). -xlinka
        if (menu.IsOpen.Value)
            return;

        // Anchor the confirm to the hand that OWNS the menu, so the camera-freeze / mouse-aim AND the opening-press
        // guard engage (both key off context.Side, and the desktop aim only runs for the owner hand). On DESKTOP the
        // menu is right-hand-owned (HandTool.ProcessMenuKey is Right-only - the same hand the working radial menu
        // uses); in VR it's the hand whose laser is on the avatar. Matching the wrong/left hand made the camera not
        // freeze, so moving the mouse turned the view and the menu edge-closed. - xlinka
        bool vr = Engine.Current?.InputInterface?.IsVRActive == true;
        var rayTarget = avatar.GetComponent<RayTarget>();
        var ctx = new Lumora.Core.Components.UI.ContextMenuContext { Target = avatar };
        foreach (var hand in userRootSlot.GetComponentsInChildren<HandTool>())
        {
            bool isOwner = vr
                ? (hand.Laser != null && ReferenceEquals(hand.Laser.CurrentRayTarget, rayTarget))
                : hand.Side.Value == Lumora.Core.Input.Chirality.Right;
            if (!isOwner)
                continue;
            ctx.Pointer = hand.Laser?.Slot;
            ctx.Side = hand.Side.Value;
            break;
        }

        menu.OpenConfirm("Equip Avatar?", "Equip Avatar", new[] { 0.14f, 0.30f, 0.18f, 0.92f }, () => TryEquip(avatar), ctx);
    }

    private static void TryEquip(Slot avatar)
    {
        var userRoot = avatar.World?.LocalUser?.Root;
        if (userRoot == null)
        {
            LumoraLogger.Warn("AvatarStudio: no local user root to equip onto");
            return;
        }
        var manager = userRoot.Slot.GetComponent<AvatarEquipManager>() ?? userRoot.Slot.AttachComponent<AvatarEquipManager>();
        if (manager.UserRoot.Target == null)
            manager.UserRoot.Target = userRoot;
        manager.EquipAvatar(avatar);
    }
}
