// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.UI;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraMeshes = Lumora.Core.Components.Meshes;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

// Body tracker calibration: the avatar freezes in a T-pose where you stand, a ball sits on every joint a
// tracker can drive, a ball follows every puck, you line them up and pull both triggers. Nobody assigns a
// role to anything: each puck is paired with the nearest unclaimed joint, closest pair first, and the
// stored offset is what turns that puck's pose into that joint bone's pose from then on.
//
// A per-limb wizard was rejected on purpose. It asks the user to know what a "left lower leg" is and to
// hold a menu open with a foot in the air. Proximity needs neither. -xlinka
[ComponentCategory("Users/Avatar")]
public sealed class TrackerCalibration : Component
{
    [NonPersistent]
    public readonly Sync<bool> Active = new();

    public struct PairResult
    {
        public string PublicID;
        public string Label;
        public BodyNode Node;
        public float Distance;
        public bool Mapped;
        public string Reason;
    }

    public IReadOnlyList<PairResult> LastResults => _lastResults;

    public IReadOnlyList<PairResult> Preview => _preview;

    public string Message => _message;

    public const string SlotName = "TrackerCalibration";

    private const string HostSlotName = "Body Calibration";

    private const string NeedsHeadset = "Needs a headset";
    private const string NeedsTracker = "Connect a tracker first";
    private const string NeedsHumanoid = "Needs a humanoid avatar";

    private const string Instructions = "Match the pose. Line up the balls. Pull both triggers.\nMenu: Cancel Calibration";
    private const string NothingLinedUp = "Nothing lined up, try again";

    private const float PressPoint = 0.7f;
    private const float ReleasePoint = 0.3f;

    // Metres at tracking scale 1. A puck strapped to a shin or a belt sits well inside this of its joint
    // even on an avatar whose proportions are off; a puck a metre from everything was left on a desk.
    private const float MaxPairDistance = 0.4f;

    // When two candidate pairs are this close in distance, the one matching the runtime's role
    // suggestion wins. Small enough that it never overrides where the user actually put the puck.
    private const float RoleTieBreak = 0.01f;

    private const float HandReach = 0.08f;

    private const float JointRadius = 0.03f;
    private const float TrackerRadius = 0.04f;
    private const float ControllerRadius = 0.015f;

    private const float MessageAhead = 0.6f;
    private const float MessageBelowEyes = 0.3f;
    private const float MessageSize = 0.025f;

    private const float ResultSeconds = 3f;
    private const float RetrySeconds = 3f;
    private const float SecondBuzzDelay = 0.15f;

    private static readonly colorHDR LeftFill = new(0.2f, 0.7f, 1f);
    private static readonly colorHDR RightFill = new(1f, 0.3f, 0.3f);
    private static readonly colorHDR CentreFill = new(0.7f, 0.4f, 1f);
    private static readonly colorHDR TrackerIdleFill = new(1f, 1f, 1f, 1f);
    private static readonly colorHDR TrackerLostFill = new(0.45f, 0.45f, 0.45f, 0.8f);

    private struct BoneState
    {
        public Slot Bone;
        public float3 LocalPosition;
        public floatQ LocalRotation;
    }

    private sealed class Ball
    {
        public Slot Slot = null!;
        public OverlayUnlitMaterial Material = null!;
        public colorHDR Shown;
    }

    private sealed class JointBall
    {
        public BodyNode Node;
        public Slot Bone = null!;
        public Ball Ball = null!;
        public bool Valid;
        public float3 Position;
        public bool Claimed;
    }

    private sealed class HandGuide
    {
        public Chirality Side;
        public Slot Bone = null!;
        public Ball Guide = null!;
        public Ball Controller = null!;
    }

    private sealed class TrackerBall
    {
        public Ball Ball = null!;
        public bool Seen;
        public bool Placed;
    }

    private struct TrackerSample
    {
        public ITracker Tracker;
        public bool Tracking;
        public float3 Position;
        public BodyNode Suggested;
    }

    private struct Pairing
    {
        public int Tracker;
        public int Joint;
        public float Distance;
    }

    private struct Candidate
    {
        public float Key;
        public float Distance;
        public int Tracker;
        public int Joint;
    }

    private static readonly Comparison<Candidate> ByKey = (a, b) =>
    {
        int c = a.Key.CompareTo(b.Key);
        if (c != 0) return c;
        c = a.Tracker.CompareTo(b.Tracker);
        return c != 0 ? c : a.Joint.CompareTo(b.Joint);
    };

    private static readonly Comparison<Pairing> ByJoint = (a, b) => a.Joint.CompareTo(b.Joint);

    private readonly List<PairResult> _lastResults = new();
    private readonly List<PairResult> _preview = new();
    private readonly List<BoneState> _boneStates = new();
    private readonly List<JointBall> _joints = new();
    private readonly List<HandGuide> _hands = new();
    private readonly List<ITracker> _trackers = new();
    private readonly List<TrackerSample> _samples = new();
    private readonly List<Pairing> _pairs = new();
    private readonly List<Candidate> _candidates = new();
    private readonly Dictionary<string, TrackerBall> _trackerBalls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    private UserRoot? _userRoot;
    private AvatarEquipManager? _equip;
    private UserInputState? _inputState;
    private Slot? _avatarRoot;
    private Slot? _avatarParent;
    private HumanoidRig? _rig;
    private AvatarIK? _ik;
    private bool _ikWasEnabled;
    private bool _rootMoved;
    private float3 _rootLocalPosition;
    private floatQ _rootLocalRotation;
    private Slot? _markerRoot;
    private TextRenderer? _text;
    private World.WorldFocus _startFocus;
    private string _message = string.Empty;
    private bool _armed;
    private float _closeIn;
    private float _messageFor;
    private float _secondBuzzIn;

    public static bool TrackersReady(out string reason)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null || !input.IsVRActive)
        {
            reason = NeedsHeadset;
            return false;
        }
        if (input.GetDevice<ITracker>(t => t.IsDeviceActive) == null)
        {
            reason = NeedsTracker;
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public static TrackerCalibration? FindActive(World world)
    {
        var calibration = Find(world);
        return calibration != null && calibration.Active.Value ? calibration : null;
    }

    private static TrackerCalibration? Find(World? world)
    {
        var root = world?.LocalUser?.Root;
        if (root == null || root.IsDestroyed || root.Slot == null || root.Slot.IsDestroyed)
            return null;
        var calibration = root.Slot.FindChild(HostSlotName)?.GetComponent<TrackerCalibration>();
        return calibration is { IsDestroyed: false } ? calibration : null;
    }

    // Front door. Puts the component on a local, non-persistent slot under the local user's root so it
    // never replicates and never saves, then starts. The reason string is for whatever UI called this.
    public static bool TryBeginForLocalUser(World world, out string reason)
    {
        if (!TrackersReady(out reason) || !TryFindAvatar(world, out var userRoot, out _, out _, out reason))
        {
            LumoraLogger.Log($"TrackerCalibration: not started: {reason}");
            return false;
        }

        var existing = Find(world);
        if (existing != null)
            return existing.TryBegin(out reason);

        var slot = userRoot.Slot.AddLocalSlot(HostSlotName);
        slot.Persistent.Value = false;
        var calibration = slot.AttachComponent<TrackerCalibration>();
        if (calibration.TryBegin(out reason))
            return true;
        slot.Destroy();
        return false;
    }

    private static bool TryFindAvatar(World? world, out UserRoot userRoot, out Slot avatar, out HumanoidRig rig, out string reason)
    {
        userRoot = null!;
        avatar = null!;
        rig = null!;
        reason = NeedsHumanoid;

        var root = world?.LocalUser?.Root;
        if (root == null || root.IsDestroyed || root.Slot == null || root.Slot.IsDestroyed)
            return false;

        var worn = root.GetRegisteredComponent<AvatarEquipManager>()?.CurrentAvatar.Target;
        if (worn == null || worn.IsDestroyed)
            return false;

        // A four-legged body keeps its biped rig around but solves with QuadrupedIK, which nothing here
        // switches off and no tracker socket feeds.
        if (worn.GetComponentInChildren<QuadrupedIK>() is { IsDestroyed: false }
            || worn.GetComponentInChildren<QuadrupedRig>() is { IsDestroyed: false, IsQuadruped: true })
            return false;

        var humanoid = worn.GetComponentInChildren<HumanoidRig>();
        if (humanoid == null || humanoid.IsDestroyed || humanoid.Bones.Count == 0)
            return false;

        bool anyJoint = false;
        foreach (var node in TrackerRoles.Mappable)
        {
            if (humanoid.TryGetBone(node) is { IsDestroyed: false })
            {
                anyJoint = true;
                break;
            }
        }
        if (!anyJoint)
            return false;

        userRoot = root;
        avatar = worn;
        rig = humanoid;
        reason = string.Empty;
        return true;
    }

    [SyncMethod]
    public void Begin()
    {
        if (!TryBegin(out var reason))
            LumoraLogger.Warn($"TrackerCalibration: refused: {reason}");
    }

    public bool TryBegin(out string reason)
    {
        if (Active.Value)
        {
            reason = "Already calibrating";
            return false;
        }
        if (!TrackersReady(out reason))
            return false;

        var world = World;
        if (world == null || world.RootSlot == null)
        {
            reason = NeedsHumanoid;
            return false;
        }
        if (!TryFindAvatar(world, out var userRoot, out var avatar, out var rig, out reason))
            return false;

        _userRoot = userRoot;
        _equip = userRoot.GetRegisteredComponent<AvatarEquipManager>();
        _avatarRoot = avatar;
        _rig = rig;
        _ik = avatar.GetComponent<AvatarIK>();
        _lastResults.Clear();
        _preview.Clear();
        _armed = false;
        _closeIn = 0f;
        _messageFor = 0f;
        _secondBuzzIn = 0f;
        _startFocus = world.Focus;

        LockAvatar(userRoot);
        BuildMarkers(world, userRoot);
        HoldInput(true);
        if (_equip != null)
            _equip.OnAvatarChanged += OnAvatarChanged;

        Active.Value = true;
        SetMessage(Instructions);
        LumoraLogger.Debug($"TrackerCalibration: started with {_joints.Count} joint(s)");
        return true;
    }

    [SyncMethod]
    public void Cancel()
    {
        if (!Active.Value)
            return;
        Finish();
        LumoraLogger.Debug("TrackerCalibration: cancelled, avatar restored");
    }

    // What the two-trigger click does, for a panel button or for driving this without controllers.
    [SyncMethod]
    public void Confirm()
    {
        if (!Active.Value || _closeIn > 0f)
            return;

        var input = Engine.Current?.InputInterface;
        if (input == null)
        {
            Finish();
            return;
        }

        float scale = RootScale();
        Sample(input);
        PairUp(_samples, _joints, MaxPairDistance * scale, RoleTieBreak * scale, _pairs, _candidates);
        _lastResults.Clear();

        if (_pairs.Count == 0)
        {
            ReportUnpaired(scale, disable: false);
            Buzz(input, 0.5f, 0.06f);
            _secondBuzzIn = SecondBuzzDelay;
            ShowFor(NothingLinedUp, RetrySeconds);
            LumoraLogger.Debug($"TrackerCalibration: nothing lined up ({_samples.Count} tracker(s))");
            return;
        }

        _pairs.Sort(ByJoint);
        var names = new List<string>(_pairs.Count);
        for (int i = 0; i < _pairs.Count; i++)
        {
            var pair = _pairs[i];
            var joint = _joints[pair.Joint];
            WriteMapping(_samples[pair.Tracker].Tracker, joint.Node, joint.Bone.GlobalPosition, joint.Bone.GlobalRotation, scale, pair.Distance);
            names.Add(NodeLabel(joint.Node));
        }
        ReportUnpaired(scale, disable: true);
        ReleaseClaimedJoints();

        Buzz(input, 1f, 0.25f);
        SetMessage(string.Join(", ", names) + " set");
        _messageFor = 0f;
        _closeIn = ResultSeconds;
        LumoraLogger.Debug($"TrackerCalibration: mapped {_pairs.Count} of {_samples.Count} tracker(s)");
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!Active.Value)
            return;

        if (_avatarRoot == null || _avatarRoot.IsDestroyed || _rig == null || _rig.IsDestroyed
            || (_equip != null && !_equip.IsDestroyed && _equip.CurrentAvatar.Target != _avatarRoot))
        {
            Finish();
            LumoraLogger.Warn("TrackerCalibration: avatar changed mid-calibration, cancelled");
            return;
        }

        var world = World;
        if (world != null && world.Focus != _startFocus && world.Focus != World.WorldFocus.Focused)
        {
            Finish();
            LumoraLogger.Debug("TrackerCalibration: world lost focus, cancelled");
            return;
        }

        // Out of VR nothing can click and the held input would lock the desktop camera, so the pose lets go
        // by itself.
        var input = Engine.Current?.InputInterface;
        if (input == null || !input.IsVRActive)
        {
            Finish();
            LumoraLogger.Debug("TrackerCalibration: headset gone, cancelled");
            return;
        }

        if (_secondBuzzIn > 0f)
        {
            _secondBuzzIn -= delta;
            if (_secondBuzzIn <= 0f)
                Buzz(input, 0.5f, 0.06f);
        }

        if (_closeIn > 0f)
        {
            _closeIn -= delta;
            if (_closeIn <= 0f)
                Finish();
            return;
        }

        if (_messageFor > 0f)
        {
            _messageFor -= delta;
            if (_messageFor <= 0f)
                SetMessage(Instructions);
        }

        float scale = RootScale();
        Sample(input);
        PairUp(_samples, _joints, MaxPairDistance * scale, RoleTieBreak * scale, _pairs, _candidates);
        RefreshPreview();
        RefreshBalls(input, scale);
        PollClick(input);
    }

    public override void OnDestroy()
    {
        if (Active.Value)
            Finish();
        base.OnDestroy();
    }

    private void OnAvatarChanged(Slot avatar)
    {
        if (!Active.Value || avatar == _avatarRoot)
            return;
        Finish();
        LumoraLogger.Warn("TrackerCalibration: avatar changed mid-calibration, cancelled");
    }

    // ---- avatar lock / restore ----

    // The avatar has to hold still while the user walks onto it, so the solver is switched off rather
    // than fought. The rig bones go back to their authored rest so the pose is the same every time
    // regardless of what the last solve left behind, then the limbs are straightened into a T. The root
    // is yawed and slid so the figure stands where the head is now: nobody should have to walk back to
    // the play-space origin to find their own avatar. All of it is snapshotted first and put back on
    // exit. -xlinka
    private void LockAvatar(UserRoot userRoot)
    {
        var avatar = _avatarRoot!;
        var rig = _rig!;

        _ikWasEnabled = _ik != null && !_ik.IsDestroyed && _ik.IKEnabled.Value;
        if (_ik != null && !_ik.IsDestroyed)
            _ik.IKEnabled.Value = false;

        _avatarParent = avatar.Parent;
        _rootLocalPosition = avatar.LocalPosition.Value;
        _rootLocalRotation = avatar.LocalRotation.Value;
        _rootMoved = false;

        LumoraLogger.Debug("POSEWRITE TrackerCalibration.LockAvatar: restoring rest and forcing a T-pose");
        SnapshotBones(avatar, rig);
        RestoreAuthoredRest(avatar, rig);
        rig.MakeTPose();
        PlaceUnderHead(userRoot, avatar, rig);
    }

    private void SnapshotBones(Slot avatar, HumanoidRig rig)
    {
        _boneStates.Clear();
        var seen = new HashSet<Slot>();

        foreach (var node in rig.Bones.Keys)
        {
            var bone = rig.TryGetBone(node);
            if (bone != null && !bone.IsDestroyed && seen.Add(bone))
                _boneStates.Add(new BoneState { Bone = bone, LocalPosition = bone.LocalPosition.Value, LocalRotation = bone.LocalRotation.Value });
        }

        var skeleton = avatar.GetComponentInChildren<SkeletonBuilder>();
        if (skeleton == null || skeleton.IsDestroyed)
            return;
        for (int i = 0; i < skeleton.BoneSlots.Count; i++)
        {
            var bone = skeleton.BoneSlots[i];
            if (bone != null && !bone.IsDestroyed && seen.Add(bone))
                _boneStates.Add(new BoneState { Bone = bone, LocalPosition = bone.LocalPosition.Value, LocalRotation = bone.LocalRotation.Value });
        }
    }

    // Only rig bones are reset, and only to what the skeleton recorded at build time. Bones above the hips
    // (the armature root) are left alone: equip re-frames them and a build-time value would undo that.
    private static void RestoreAuthoredRest(Slot avatar, HumanoidRig rig)
    {
        var skeleton = avatar.GetComponentInChildren<SkeletonBuilder>();
        if (skeleton == null || skeleton.IsDestroyed)
            return;

        var rigBones = new HashSet<Slot>();
        foreach (var node in rig.Bones.Keys)
        {
            var bone = rig.TryGetBone(node);
            if (bone != null && !bone.IsDestroyed)
                rigBones.Add(bone);
        }

        int count = System.Math.Min(skeleton.BoneSlots.Count, skeleton.RestPoseTransforms.Count);
        for (int i = 0; i < count; i++)
        {
            var bone = skeleton.BoneSlots[i];
            if (bone == null || bone.IsDestroyed || !rigBones.Contains(bone))
                continue;
            if (bone.LocalPosition.IsDriven || bone.LocalRotation.IsDriven)
                continue;
            if (!TryDecompose(skeleton.RestPoseTransforms[i], out var position, out var rotation))
                continue;
            bone.LocalPosition.Value = position;
            bone.LocalRotation.Value = rotation;
        }
    }

    private void PlaceUnderHead(UserRoot userRoot, Slot avatar, HumanoidRig rig)
    {
        var headBone = rig.TryGetBone(BodyNode.Head);
        var headSlot = userRoot.HeadSlot;
        if (headBone == null || headBone.IsDestroyed || headSlot == null || headSlot.IsDestroyed)
            return;
        if (avatar.LocalPosition.IsDriven || avatar.LocalRotation.IsDriven)
            return;

        float3 want = headSlot.GlobalRotation * float3.Backward;
        want.y = 0f;
        float3 have = AvatarFacing(avatar, rig);
        if (want.LengthSquared > 1e-6f && have.LengthSquared > 1e-6f)
        {
            float delta = MathF.Atan2(want.x, want.z) - MathF.Atan2(have.x, have.z);
            while (delta > MathF.PI) delta -= 2f * MathF.PI;
            while (delta < -MathF.PI) delta += 2f * MathF.PI;
            if (MathF.Abs(delta) > 0.005f)
                avatar.GlobalRotation = floatQ.AxisAngleRad(float3.Up, delta) * avatar.GlobalRotation;
        }

        float3 shift = headSlot.GlobalPosition - headBone.GlobalPosition;
        shift.y = 0f;
        if (shift.LengthSquared > 1e-8f)
            avatar.GlobalPosition = avatar.GlobalPosition + shift;

        _rootMoved = true;
    }

    // An avatar swap can move the old body out from under the manager before this runs; its old local
    // pose means nothing under a new parent.
    private void RestoreAvatar()
    {
        var avatar = _avatarRoot;
        if (avatar != null && !avatar.IsDestroyed)
        {
            for (int i = 0; i < _boneStates.Count; i++)
            {
                var state = _boneStates[i];
                if (state.Bone == null || state.Bone.IsDestroyed)
                    continue;
                if (!state.Bone.LocalPosition.IsDriven)
                    state.Bone.LocalPosition.Value = state.LocalPosition;
                if (!state.Bone.LocalRotation.IsDriven)
                    state.Bone.LocalRotation.Value = state.LocalRotation;
            }

            if (_rootMoved && avatar.Parent == _avatarParent)
            {
                if (!avatar.LocalPosition.IsDriven)
                    avatar.LocalPosition.Value = _rootLocalPosition;
                if (!avatar.LocalRotation.IsDriven)
                    avatar.LocalRotation.Value = _rootLocalRotation;
            }
        }

        if (_ik != null && !_ik.IsDestroyed && _ikWasEnabled)
            _ik.IKEnabled.Value = true;
    }

    // Turning would swing the frozen figure round with the root, so the whole locomotion channel is held,
    // not only translation.
    private void HoldInput(bool hold)
    {
        if (hold)
            _inputState = _userRoot?.Slot?.GetComponent<UserInputState>();

        var state = _inputState;
        if (state != null && !state.IsDestroyed)
        {
            state.SetMovementSuppressed(this, hold);
            state.SetDesktopInputSuppressed(this, hold);
            // The confirming pull is ours: the beams point sideways at whatever is there and must not click it.
            state.SetTriggerCaptured(this, hold);
        }

        if (!hold)
            _inputState = null;
    }

    private void Finish()
    {
        if (_equip != null)
            _equip.OnAvatarChanged -= OnAvatarChanged;
        HoldInput(false);
        DestroyMarkers();
        RestoreAvatar();

        Active.Value = false;
        _message = string.Empty;

        _userRoot = null;
        _equip = null;
        _avatarRoot = null;
        _avatarParent = null;
        _rig = null;
        _ik = null;
        _rootMoved = false;
        _boneStates.Clear();
        _trackers.Clear();
        _samples.Clear();
        _pairs.Clear();
        _candidates.Clear();
        _preview.Clear();
        _labels.Clear();
        _armed = false;
        _closeIn = 0f;
        _messageFor = 0f;
        _secondBuzzIn = 0f;
    }

    // ---- markers ----

    private void BuildMarkers(World world, UserRoot userRoot)
    {
        DestroyMarkers();

        // Local and non-persistent: calibration chrome is for the person calibrating, and it must never
        // end up in a world save or on a peer's screen. It hangs off the world root, not the user: a
        // billboard on your own body turns to face where you look.
        _markerRoot = world.RootSlot.AddLocalSlot(SlotName);
        _markerRoot.Persistent.Value = false;

        float scale = RootScale();
        var rig = _rig!;

        foreach (var node in TrackerRoles.Mappable)
        {
            var bone = rig.TryGetBone(node);
            if (bone == null || bone.IsDestroyed)
                continue;
            var ball = AddBall("Joint " + node, JointRadius * scale, Dim(FillFor(node)));
            ball.Slot.GlobalPosition = bone.GlobalPosition;
            _joints.Add(new JointBall { Node = node, Bone = bone, Ball = ball, Valid = true, Position = bone.GlobalPosition });
        }

        AddHand(Chirality.Left, rig, scale);
        AddHand(Chirality.Right, rig, scale);
        BuildMessage(userRoot, scale);
    }

    private void AddHand(Chirality side, HumanoidRig rig, float scale)
    {
        var node = side.HandNode();
        var bone = rig.TryGetBone(node);
        if (bone == null || bone.IsDestroyed)
            return;

        var fill = side == Chirality.Left ? LeftFill : RightFill;
        var guide = AddBall("Hand " + node, JointRadius * scale, Bright(fill));
        guide.Slot.GlobalPosition = bone.GlobalPosition;
        var controller = AddBall("Controller " + side, ControllerRadius * scale, Dim(fill));
        controller.Slot.ActiveSelf.Value = false;
        _hands.Add(new HandGuide { Side = side, Bone = bone, Guide = guide, Controller = controller });
    }

    private void BuildMessage(UserRoot userRoot, float scale)
    {
        var slot = _markerRoot!.AddSlot("Message");
        var head = userRoot.HeadSlot;
        if (head != null && !head.IsDestroyed)
        {
            float3 ahead = head.GlobalRotation * float3.Backward;
            ahead.y = 0f;
            ahead = ahead.LengthSquared > 1e-6f ? ahead.Normalized : float3.Backward;
            slot.GlobalPosition = head.GlobalPosition + ahead * (MessageAhead * scale) - float3.Up * (MessageBelowEyes * scale);
        }
        slot.AttachComponent<FaceLocalUser>();

        // Text with no font draws nothing.
        var font = slot.AttachComponent<FontProvider>();
        font.URL.Value = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;

        var text = slot.AttachComponent<TextRenderer>();
        text.Font.Target = font;
        text.Size.Value = MessageSize * scale;
        text.Color.Value = new color(1f, 1f, 1f, 1f);
        text.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        text.VerticalAlign.Value = TextVerticalAlignment.Middle;
        text.OutlineColor.Value = new colorHDR(0f, 0f, 0f, 1f);
        text.OutlineThickness.Value = 1.2f;
        _text = text;
    }

    // Overlay material, same as the rig editor's joint balls: these sit inside the avatar's own body and
    // have to read through it.
    private Ball AddBall(string name, float radius, colorHDR fill)
    {
        var slot = _markerRoot!.AddSlot(name);

        var mesh = slot.AttachComponent<LumoraMeshes.SphereMesh>();
        mesh.Radius.Value = radius;
        mesh.Segments.Value = 16;
        mesh.Rings.Value = 12;
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;

        var material = slot.AttachComponent<OverlayUnlitMaterial>();
        material.UseVertexColor.Value = false;
        material.BlendMode.Value = BlendMode.Alpha;
        renderer.Material.Target = material;

        var ball = new Ball { Slot = slot, Material = material };
        Paint(ball, fill);
        return ball;
    }

    private void RefreshBalls(InputInterface input, float scale)
    {
        if (_markerRoot == null || _markerRoot.IsDestroyed)
            return;

        for (int i = 0; i < _joints.Count; i++)
        {
            var joint = _joints[i];
            if (joint.Ball.Slot.IsDestroyed)
                continue;
            Show(joint.Ball.Slot, joint.Valid);
            if (!joint.Valid)
                continue;
            Move(joint.Ball.Slot, joint.Position);
            var fill = FillFor(joint.Node);
            Paint(joint.Ball, joint.Claimed ? Bright(fill) : Dim(fill));
        }

        foreach (var tracker in _trackerBalls.Values)
            tracker.Seen = false;

        for (int t = 0; t < _samples.Count; t++)
        {
            var sample = _samples[t];
            string key = sample.Tracker.UniqueID;
            if (!_trackerBalls.TryGetValue(key, out var tracker) || tracker.Ball.Slot.IsDestroyed)
            {
                // Named by PublicID only. The unique id is a serial and stays out of the scene graph.
                var ball = AddBall("Tracker " + (sample.Tracker.PublicID ?? "?"), TrackerRadius * scale, TrackerIdleFill);
                tracker = new TrackerBall { Ball = ball };
                _trackerBalls[key] = tracker;
            }

            // A puck that has not tracked since calibration began has no place to be drawn yet, and an
            // on-top ball left at the world origin shows through every wall.
            tracker.Seen = true;
            if (!sample.Tracking)
            {
                Show(tracker.Ball.Slot, tracker.Placed);
                Paint(tracker.Ball, TrackerLostFill);
                continue;
            }

            Show(tracker.Ball.Slot, true);
            Move(tracker.Ball.Slot, sample.Position);
            tracker.Placed = true;
            var fill = TrackerIdleFill;
            for (int i = 0; i < _pairs.Count; i++)
            {
                if (_pairs[i].Tracker == t)
                {
                    fill = Bright(FillFor(_joints[_pairs[i].Joint].Node));
                    break;
                }
            }
            Paint(tracker.Ball, fill);
        }

        foreach (var tracker in _trackerBalls.Values)
        {
            if (!tracker.Seen && !tracker.Ball.Slot.IsDestroyed)
                Show(tracker.Ball.Slot, false);
        }

        for (int i = 0; i < _hands.Count; i++)
        {
            var hand = _hands[i];
            if (hand.Guide.Slot.IsDestroyed || hand.Controller.Slot.IsDestroyed)
                continue;

            bool boneValid = hand.Bone != null && !hand.Bone.IsDestroyed;
            Show(hand.Guide.Slot, boneValid);
            if (boneValid)
                Move(hand.Guide.Slot, hand.Bone!.GlobalPosition);

            var controller = hand.Side == Chirality.Left ? input.LeftController : input.RightController;
            var handSlot = hand.Side == Chirality.Left ? _userRoot?.LeftHandSlot : _userRoot?.RightHandSlot;
            bool live = controller != null && controller.IsDeviceActive && handSlot != null && !handSlot.IsDestroyed;
            Show(hand.Controller.Slot, live);
            if (!live)
                continue;

            float3 at = handSlot!.GlobalPosition;
            Move(hand.Controller.Slot, at);
            bool near = boneValid && float3.Distance(at, hand.Bone!.GlobalPosition) <= HandReach * scale;
            var fill = hand.Side == Chirality.Left ? LeftFill : RightFill;
            Paint(hand.Controller, near ? Bright(fill) : Dim(fill));
        }
    }

    private void DestroyMarkers()
    {
        _trackerBalls.Clear();
        _joints.Clear();
        _hands.Clear();
        _text = null;
        if (_markerRoot != null && !_markerRoot.IsDestroyed)
            _markerRoot.Destroy();
        _markerRoot = null;
    }

    private static void Paint(Ball ball, colorHDR fill)
    {
        if (ball.Shown == fill || ball.Material == null || ball.Material.IsDestroyed)
            return;
        ball.Shown = fill;
        ball.Material.FrontTintColor.Value = fill;
        ball.Material.BehindTintColor.Value = fill;
    }

    private static void Move(Slot slot, float3 position)
    {
        if (float3.DistanceSquared(slot.GlobalPosition, position) > 1e-8f)
            slot.GlobalPosition = position;
    }

    private static void Show(Slot slot, bool show)
    {
        if (slot.ActiveSelf.Value != show)
            slot.ActiveSelf.Value = show;
    }

    private static colorHDR Bright(colorHDR fill) => new(fill.r, fill.g, fill.b, 1f);

    private static colorHDR Dim(colorHDR fill) => new(fill.r * 0.4f, fill.g * 0.4f, fill.b * 0.4f, 0.55f);

    // ---- pairing ----

    // Armed only once BOTH triggers have been seen released, so a trigger that was already down when
    // calibration started, or that is still down from the last click, cannot fire it.
    private void PollClick(InputInterface input)
    {
        var left = input.LeftController;
        var right = input.RightController;
        float l = left != null && left.IsDeviceActive ? left.TriggerValue : 0f;
        float r = right != null && right.IsDeviceActive ? right.TriggerValue : 0f;

        if (!_armed)
        {
            if (l <= ReleasePoint && r <= ReleasePoint)
                _armed = true;
            return;
        }

        if (l >= PressPoint && r >= PressPoint)
        {
            _armed = false;
            Confirm();
        }
    }

    private void Sample(InputInterface input)
    {
        _trackers.Clear();
        input.GetDevices(_trackers, t => t.IsDeviceActive);

        _samples.Clear();
        for (int i = 0; i < _trackers.Count; i++)
        {
            var tracker = _trackers[i];
            if (string.IsNullOrEmpty(tracker.UniqueID))
                continue;
            bool tracking = tracker.IsTracking;
            _samples.Add(new TrackerSample
            {
                Tracker = tracker,
                Tracking = tracking,
                Position = tracking ? tracker.Position : float3.Zero,
                Suggested = TrackerRoles.ToBodyNode(tracker.SuggestedRole),
            });
        }

        for (int i = 0; i < _joints.Count; i++)
        {
            var joint = _joints[i];
            joint.Valid = joint.Bone != null && !joint.Bone.IsDestroyed;
            if (joint.Valid)
                joint.Position = joint.Bone!.GlobalPosition;
        }
    }

    // Greedy over every (tracker, joint) pair in reach, closest first; the runtime's role only breaks
    // near-ties. Reads nothing but its arguments, so the live preview and the confirm cannot disagree.
    private static void PairUp(List<TrackerSample> trackers, List<JointBall> joints, float maxDistance, float tieBreak,
        List<Pairing> pairs, List<Candidate> scratch)
    {
        pairs.Clear();
        scratch.Clear();

        for (int t = 0; t < trackers.Count; t++)
        {
            var tracker = trackers[t];
            if (!tracker.Tracking)
                continue;
            for (int j = 0; j < joints.Count; j++)
            {
                var joint = joints[j];
                if (!joint.Valid)
                    continue;
                float distance = float3.Distance(tracker.Position, joint.Position);
                if (distance > maxDistance)
                    continue;
                float key = distance - (tracker.Suggested == joint.Node ? tieBreak : 0f);
                scratch.Add(new Candidate { Key = key, Distance = distance, Tracker = t, Joint = j });
            }
        }
        scratch.Sort(ByKey);

        for (int i = 0; i < scratch.Count; i++)
        {
            var candidate = scratch[i];
            bool taken = false;
            for (int p = 0; p < pairs.Count && !taken; p++)
                taken = pairs[p].Tracker == candidate.Tracker || pairs[p].Joint == candidate.Joint;
            if (!taken)
                pairs.Add(new Pairing { Tracker = candidate.Tracker, Joint = candidate.Joint, Distance = candidate.Distance });
        }
    }

    private void RefreshPreview()
    {
        _preview.Clear();
        for (int i = 0; i < _joints.Count; i++)
            _joints[i].Claimed = false;

        for (int i = 0; i < _pairs.Count; i++)
        {
            var pair = _pairs[i];
            var joint = _joints[pair.Joint];
            joint.Claimed = true;
            var tracker = _samples[pair.Tracker].Tracker;
            _preview.Add(new PairResult
            {
                PublicID = tracker.PublicID ?? string.Empty,
                Label = LabelFor(tracker),
                Node = joint.Node,
                Distance = pair.Distance,
                Mapped = false,
                Reason = string.Empty,
            });
        }
    }

    // A puck that is on and tracking but lined up with nothing was put down somewhere, so it stops driving
    // whatever it drove before. One that is not tracking right now keeps its mapping: it cannot be judged.
    private void ReportUnpaired(float scale, bool disable)
    {
        float maxDistance = MaxPairDistance * scale;
        for (int t = 0; t < _samples.Count; t++)
        {
            bool paired = false;
            for (int p = 0; p < _pairs.Count && !paired; p++)
                paired = _pairs[p].Tracker == t;
            if (paired)
                continue;

            var sample = _samples[t];
            var tracker = sample.Tracker;
            string label = LabelFor(tracker);
            if (!sample.Tracking)
            {
                Report(tracker, label, BodyNode.NONE, -1f, false, "not tracking");
                continue;
            }

            float nearest = float.MaxValue;
            BodyNode nearestNode = BodyNode.NONE;
            for (int j = 0; j < _joints.Count; j++)
            {
                if (!_joints[j].Valid)
                    continue;
                float distance = float3.Distance(sample.Position, _joints[j].Position);
                if (distance < nearest)
                {
                    nearest = distance;
                    nearestNode = _joints[j].Node;
                }
            }

            string reason = nearestNode == BodyNode.NONE ? "no joint available"
                : nearest > maxDistance ? $"nearest joint {NodeLabel(nearestNode)} is {nearest:F2} m away, over the {maxDistance:F2} m limit"
                : $"nearest joint {NodeLabel(nearestNode)} was claimed by a closer tracker";
            if (disable && TrackerMappingStore.SetEnabled(tracker.UniqueID, false))
                reason += ", disabled";
            Report(tracker, label, nearestNode, nearestNode == BodyNode.NONE ? -1f : nearest, false, reason);
            if (disable)
                LumoraLogger.Debug($"TrackerCalibration: '{label}' ({tracker.PublicID}) not mapped: {reason}");
        }
    }

    // A puck that was off or out of sight at the click keeps its mapping, unless its joint just went to
    // another puck. Two pucks on one joint leave the body following whichever socket holds the driver,
    // which can be the dead one.
    private void ReleaseClaimedJoints()
    {
        var stored = new List<TrackerMapping>();
        TrackerMappingStore.GetAll(stored);
        for (int i = 0; i < stored.Count; i++)
        {
            var mapping = stored[i];
            if (!mapping.Enabled)
                continue;

            bool claimed = false;
            bool claimant = false;
            for (int p = 0; p < _pairs.Count; p++)
            {
                if (_joints[_pairs[p].Joint].Node != mapping.Node)
                    continue;
                claimed = true;
                claimant = string.Equals(_samples[_pairs[p].Tracker].Tracker.UniqueID, mapping.UniqueID, StringComparison.Ordinal);
                break;
            }

            if (claimed && !claimant && TrackerMappingStore.SetEnabled(mapping.UniqueID, false))
                LumoraLogger.Debug($"TrackerCalibration: {NodeLabel(mapping.Node)} went to another tracker, older mapping disabled");
        }
    }

    // The stored offset takes the TRACKER pose to the joint BONE's pose in the T-pose, expressed in the
    // tracker's own frame:
    //
    //   rotationOffset = inverse(trackerRot) * boneRot
    //   positionOffset = inverse(trackerRot) * (bonePos - trackerPos) / trackingScale
    //
    // so that afterwards  bonePos = trackerPos + trackerRot * (positionOffset * trackingScale)  and
    // boneRot = trackerRot * rotationOffset. That is the child-transform composition
    // TrackedDevicePositioner applies (BodyNodeRoot.Local = offset under the device slot, whose world pose
    // is the tracker's), and the division by the tracking scale is there because that child's local units
    // are the user root's units, while the poses sampled here are world metres. The rotation is the bone's
    // own, not a facing frame: IK takes a tracked hips or foot as that bone's target pose. The round-trip
    // below is a cheap guard that the maths agrees with itself; anything over a millimetre is logged. -xlinka
    private void WriteMapping(ITracker tracker, BodyNode node, float3 bonePosition, floatQ boneRotation, float rootScale, float distance)
    {
        float3 trackerPosition = tracker.Position;
        floatQ trackerRotation = tracker.Rotation.Normalized;
        floatQ inverse = trackerRotation.Inverse;

        float trackingScale = tracker.TrackingSpace?.Scale ?? rootScale;
        if (trackingScale < 1e-4f || float.IsNaN(trackingScale) || float.IsInfinity(trackingScale))
            trackingScale = 1f;

        float3 positionOffset = (inverse * (bonePosition - trackerPosition)) / trackingScale;
        floatQ rotationOffset = (inverse * boneRotation.Normalized).Normalized;

        float3 check = trackerPosition + trackerRotation * (positionOffset * trackingScale);
        float error = float3.Distance(check, bonePosition);
        if (error > 0.001f)
            LumoraLogger.Warn($"TrackerCalibration: offset round-trip for {node} is off by {error * 1000f:F1} mm");

        string? customName = null;
        if (TrackerMappingStore.TryGet(tracker.UniqueID, out var existing))
            customName = existing.CustomName;

        var mapping = new TrackerMapping
        {
            UniqueID = tracker.UniqueID,
            Node = node,
            PositionOffset = positionOffset,
            RotationOffset = rotationOffset,
            Enabled = true,
            CustomName = customName,
            CalibratedRole = tracker.SuggestedRole,
            CalibratedUtcTicks = DateTime.UtcNow.Ticks,
        };
        TrackerMappingStore.Set(mapping);
        bool applied = TrackerMappingStore.Apply(tracker, mapping);

        string label = LabelFor(tracker);
        Report(tracker, label, node, distance, true, applied ? "set" : "set, not applied to the device");
        LumoraLogger.Debug(
            $"TrackerCalibration: '{label}' ({tracker.PublicID}) -> {node} at {distance:F3} m, " +
            $"posOffset={positionOffset} rotOffset={rotationOffset} live={(applied ? "applied" : "not applied")}");
    }

    private void Report(ITracker tracker, string label, BodyNode node, float distance, bool mapped, string reason)
    {
        _lastResults.Add(new PairResult
        {
            PublicID = tracker.PublicID ?? string.Empty,
            Label = label,
            Node = node,
            Distance = distance,
            Mapped = mapped,
            Reason = reason,
        });
    }

    // ---- feedback ----

    private void SetMessage(string text)
    {
        _message = text;
        if (_text != null && !_text.IsDestroyed && _text.Text.Value != text)
            _text.Text.Value = text;
    }

    private void ShowFor(string text, float seconds)
    {
        SetMessage(text);
        _messageFor = seconds;
    }

    private static void Buzz(InputInterface input, float amplitude, float seconds)
    {
        if (input.LeftController is { IsDeviceActive: true } left)
            left.TriggerHaptic(amplitude, seconds);
        if (input.RightController is { IsDeviceActive: true } right)
            right.TriggerHaptic(amplitude, seconds);
    }

    // ---- frames and helpers ----

    private float3 AvatarFacing(Slot avatar, HumanoidRig rig)
    {
        float3 forward;
        if (!AvatarCalibration.TryComputeBodyAxes(rig, out _, out _, out forward))
            forward = avatar.GlobalRotation * float3.Backward;
        forward.y = 0f;
        return forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;
    }

    private float RootScale()
    {
        var root = (_userRoot ?? World?.LocalUser?.Root)?.Slot;
        if (root == null || root.IsDestroyed)
            return 1f;
        float s = root.GlobalScale.x;
        return s > 1e-4f && !float.IsNaN(s) && !float.IsInfinity(s) ? s : 1f;
    }

    private static colorHDR FillFor(BodyNode node)
    {
        string name = node.ToString();
        if (name.StartsWith("Left", StringComparison.Ordinal)) return LeftFill;
        if (name.StartsWith("Right", StringComparison.Ordinal)) return RightFill;
        return CentreFill;
    }

    private static string NodeLabel(BodyNode node) => node switch
    {
        BodyNode.Hips => "Hips",
        BodyNode.Chest => "Chest",
        BodyNode.LeftFoot => "Left Foot",
        BodyNode.RightFoot => "Right Foot",
        BodyNode.LeftLowerLeg => "Left Knee",
        BodyNode.RightLowerLeg => "Right Knee",
        BodyNode.LeftLowerArm => "Left Elbow",
        BodyNode.RightLowerArm => "Right Elbow",
        BodyNode.LeftShoulder => "Left Shoulder",
        BodyNode.RightShoulder => "Right Shoulder",
        _ => node.ToString(),
    };

    private string LabelFor(ITracker tracker)
    {
        string key = tracker.UniqueID ?? string.Empty;
        if (!_labels.TryGetValue(key, out var label))
        {
            label = TrackerLabel(tracker);
            _labels[key] = label;
        }
        return label;
    }

    private static string TrackerLabel(ITracker tracker)
    {
        if (TrackerMappingStore.TryGet(tracker.UniqueID, out var mapping) && !string.IsNullOrEmpty(mapping.CustomName))
            return mapping.CustomName!;
        if (tracker.SuggestedRole != TrackerRole.None)
            return tracker.SuggestedRole.ToString();
        string id = tracker.PublicID ?? string.Empty;
        return "Tracker " + (id.Length > 4 ? id[^4..] : id);
    }

    // Column-major TRS -> translation + rotation. Scale is stripped off the basis columns and dropped:
    // bone scale is never touched by the solver, so it never needs restoring. A mirrored or degenerate
    // matrix, or a rotation that does not rebuild the columns it came from, is refused rather than
    // written to a bone. -xlinka
    private static bool TryDecompose(in float4x4 m, out float3 position, out floatQ rotation)
    {
        position = new float3(m[0, 3], m[1, 3], m[2, 3]);
        rotation = floatQ.Identity;

        var x = new float3(m[0, 0], m[1, 0], m[2, 0]);
        var y = new float3(m[0, 1], m[1, 1], m[2, 1]);
        var z = new float3(m[0, 2], m[1, 2], m[2, 2]);
        if (x.LengthSquared < 1e-12f || y.LengthSquared < 1e-12f || z.LengthSquared < 1e-12f)
            return false;
        x = x.Normalized;
        y = y.Normalized;
        z = z.Normalized;
        if (float3.Dot(float3.Cross(x, y), z) < 0f)
            return false;

        float m00 = x.x, m10 = x.y, m20 = x.z;
        float m01 = y.x, m11 = y.y, m21 = y.z;
        float m02 = z.x, m12 = z.y, m22 = z.z;

        float trace = m00 + m11 + m22;
        float qx, qy, qz, qw;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            qw = 0.25f * s;
            qx = (m21 - m12) / s;
            qy = (m02 - m20) / s;
            qz = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            qw = (m21 - m12) / s;
            qx = 0.25f * s;
            qy = (m01 + m10) / s;
            qz = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            qw = (m02 - m20) / s;
            qx = (m01 + m10) / s;
            qy = 0.25f * s;
            qz = (m12 + m21) / s;
        }
        else
        {
            float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            qw = (m10 - m01) / s;
            qx = (m02 + m20) / s;
            qy = (m12 + m21) / s;
            qz = 0.25f * s;
        }

        var q = new floatQ(qx, qy, qz, qw);
        if (float.IsNaN(q.Length) || q.Length < 1e-6f)
            return false;
        q = q.Normalized;

        var rebuilt = float4x4.Rotate(q);
        float worst = 0f;
        worst = MathF.Max(worst, MathF.Abs(rebuilt[0, 0] - m00));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[1, 0] - m10));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[2, 0] - m20));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[0, 1] - m01));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[1, 1] - m11));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[2, 1] - m21));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[0, 2] - m02));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[1, 2] - m12));
        worst = MathF.Max(worst, MathF.Abs(rebuilt[2, 2] - m22));
        if (worst > 1e-3f)
            return false;

        rotation = q;
        return true;
    }
}
