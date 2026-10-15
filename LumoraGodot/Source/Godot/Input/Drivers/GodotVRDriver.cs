// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Source.Input;
using Lumora.Source.Godot.Bootstrap;
using System.Numerics;
using System;
using System.Collections.Generic;
using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;
using Vector2 = System.Numerics.Vector2;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Input.Drivers;

public class GodotVRDriver : IVRDriver, IInputDriver
{
    public int UpdateOrder => 0;

    // IVRDriver interface properties
    public bool IsRuntimeInitialized => _xrInterface != null && _xrInterface.IsInitialized();
    public bool IsVRActive => _modeActive && IsRuntimeInitialized;
    public string VRSystemName => _xrInterface?.GetName() ?? "None";
    public VRPlatform Platform => DetectPlatform();
    public string RuntimeName => GetRuntimeName();
    public string LeftInteractionProfile  => GetTrackerInteractionProfile(LeftHandTrackerName);
    public string RightInteractionProfile => GetTrackerInteractionProfile(RightHandTrackerName);

    private XRInterface _xrInterface = null!;
    private InputInterface _inputInterface = null!;
    private bool _modeActive;
    private bool _trackerSignalsConnected;

    // Godot XR nodes for tracking (proper Godot 4.x pattern)
    private XRCamera3D _xrCamera = null!;
    private XRController3D _leftController = null!;
    private XRController3D _rightController = null!;
    private XROrigin3D _xrOrigin = null!;

    // Tracked devices for body node system
    private TrackedObject _headTrackedObject = null!;
    private TrackedObject _leftControllerTrackedObject = null!;
    private TrackedObject _rightControllerTrackedObject = null!;
    private TrackedObject _leftHandTrackedObject = null!;
    private TrackedObject _rightHandTrackedObject = null!;

    // Body trackers (the pucks).
    //
    // Godot registers one XRPositionalTracker per top-level path in the action map when the OpenXR
    // session starts, puck or no puck, so "tracker exists" means nothing. A candidate becomes a device
    // only when the runtime binds an interaction profile to its path (profile_changed) or streams a pose
    // for it; it drops back to a candidate when the profile is unbound and to nothing on tracker_removed.
    // The frame loop walks the live list only, so a rig without pucks pays one Count compare.
    //
    // UniqueID is the tracker name, which is the OpenXR role path. Godot exposes no serial or persistent
    // path for a tracker and the runtime keeps role-per-serial itself, so the role path is the most
    // stable identity on offer. It still never leaves the machine as-is. -xlinka
    private sealed class TrackerEntry
    {
        public string Name = string.Empty;
        public XRPositionalTracker Tracker = null!;
        public XRPose? Pose;
        public TrackerDevice? Device;
        public bool Live;
        public bool WantsLive;
        public bool PoseSubscribed;
        public XRPositionalTracker.ProfileChangedEventHandler ProfileHandler = null!;
        public XRPositionalTracker.PoseChangedEventHandler PoseHandler = null!;
    }

    private const string TrackerRolePathPrefix = "/user/vive_tracker_htcx/role/";

    // Below the head and controllers (100) so a puck mapped onto a node one of those owns can never win
    // it, above the controller-derived hand estimate (50) because a puck is a measurement, not a guess.
    // The finger skeleton's 120 is irrelevant: nothing lets a puck map to a finger. -xlinka
    private const int TrackerPriority = 90;

    private static readonly StringName DefaultPoseName = new StringName("default");

    private readonly Dictionary<string, TrackerEntry> _trackerCandidates = new();
    private readonly List<TrackerEntry> _liveTrackers = new();

    // InputInterface cannot unregister a device, so a puck that leaves and returns gets its old device
    // back instead of a second one with the same identity.
    private readonly Dictionary<string, TrackerDevice> _trackerDevices = new();

    // Picks up the XR interface that PhaseXRDetection() already initialized, hooks tracker add/remove,
    // and walks the trackers the runtime registered before anyone was listening.
    public void InitializeVR()
    {
        _xrInterface = XRServer.PrimaryInterface;

        if (_xrInterface != null && _xrInterface.IsInitialized())
        {
            _modeActive = true;
            LumoraLogger.Log($"GodotVRDriver: VR active - interface: {_xrInterface.GetName()}");

            // Log the resolved platform + runtime + active interaction profile
            // so it's obvious at boot whether we're on Quest, Pico, SteamVR PC,
            // etc. Useful for gameplay code that branches on Platform, and for
            // remote debugging on standalone where the only log surface is adb.
            // - xlinka
            LumoraLogger.Log($"GodotVRDriver: Platform={Platform}, Runtime='{RuntimeName}', " +
                     $"LeftProfile='{LeftInteractionProfile}', RightProfile='{RightInteractionProfile}'");

            if (!_trackerSignalsConnected)
            {
                XRServer.TrackerAdded += OnTrackerAdded;
                XRServer.TrackerRemoved += OnTrackerRemoved;
                _trackerSignalsConnected = true;
            }

            DiscoverTrackers();
        }
        else
        {
            _modeActive = false;
            LumoraLogger.Log("GodotVRDriver: No VR interface active - running in desktop mode");
        }
    }

    public void SetModeActive(bool active)
    {
        var newActive = active && IsRuntimeInitialized;
        if (_modeActive != newActive)
            LumoraLogger.Debug($"GodotVRDriver: Mode active = {newActive} (runtime initialized: {IsRuntimeInitialized})");

        _modeActive = newActive;

        if (!_modeActive)
            ClearTrackingState();
    }

    public void LogRuntimeDiagnostics()
    {
        LumoraLogger.Debug($"GodotVRDriver: Runtime='{RuntimeName}', System='{VRSystemName}', Platform={Platform}, Active={IsVRActive}");
        LumoraLogger.Log($"GodotVRDriver: HMD='{GetTrackerDescription("head")}', Left='{GetTrackerDescription("left_hand")}', Right='{GetTrackerDescription("right_hand")}'");
        LumoraLogger.Debug($"GodotVRDriver: Profiles Left='{LeftInteractionProfile}', Right='{RightInteractionProfile}'");
    }

    private void OnTrackerAdded(StringName trackerName, long type)
    {
        var tracker = XRServer.GetTracker(trackerName);
        if (tracker == null) return;

        string desc = tracker is XRPositionalTracker pt ? pt.Description : tracker.Name.ToString();
        LumoraLogger.Debug($"GodotVRDriver: Tracker connected [{(XRServer.TrackerType)type}] '{trackerName}' - {desc}");

        if ((XRServer.TrackerType)type == XRServer.TrackerType.Camera)
            LumoraLogger.Debug($"GodotVRDriver: HMD device: {desc}");

        if ((XRServer.TrackerType)type == XRServer.TrackerType.Hand)
            RefreshHandSkeletonTrackers();

        if (tracker is XRPositionalTracker positional)
            TrackCandidate(positional);
    }

    private void OnTrackerRemoved(StringName trackerName, long type)
    {
        if ((XRServer.TrackerType)type == XRServer.TrackerType.Hand)
            RefreshHandSkeletonTrackers();

        if (!_trackerCandidates.Remove(trackerName.ToString(), out var entry))
            return;

        bool wasLive = entry.Live;
        DetachCandidate(entry);
        if (wasLive)
            LumoraLogger.Debug($"GodotVRDriver: body tracker {entry.Device?.Name} removed");
    }

    // Vive pucks arrive typed Controller with the role path as their name; the type alone cannot tell
    // them from a hand controller, the name can. Everything Godot types as a head, hand, body, face,
    // anchor or base station is something else's job.
    private static bool IsBodyTracker(XRTracker tracker, string name)
    {
        if (tracker is not XRPositionalTracker)
            return false;

        switch (tracker.Type)
        {
            case XRServer.TrackerType.Camera:
            case XRServer.TrackerType.Hand:
            case XRServer.TrackerType.Body:
            case XRServer.TrackerType.Face:
            case XRServer.TrackerType.Anchor:
            case XRServer.TrackerType.Basestation:
                return false;
        }

        return name.StartsWith(TrackerRolePathPrefix, StringComparison.Ordinal);
    }

    private void DiscoverTrackers()
    {
        if (!IsRuntimeInitialized)
            return;

        RefreshHandSkeletonTrackers();

        using var trackers = XRServer.GetTrackers((int)XRServer.TrackerType.Any);
        foreach (var pair in trackers)
        {
            if (pair.Value.AsGodotObject() is XRPositionalTracker tracker)
                TrackCandidate(tracker);
        }
    }

    private void TrackCandidate(XRPositionalTracker tracker)
    {
        string name = tracker.Name.ToString();
        if (!IsBodyTracker(tracker, name) || _trackerCandidates.ContainsKey(name))
            return;

        var entry = new TrackerEntry { Name = name, Tracker = tracker };
        entry.ProfileHandler = profile => OnTrackerProfileChanged(entry, profile);
        entry.PoseHandler = pose => OnTrackerPoseChanged(entry, pose);
        tracker.ProfileChanged += entry.ProfileHandler;
        tracker.PoseChanged += entry.PoseHandler;
        entry.PoseSubscribed = true;
        _trackerCandidates[name] = entry;

        LumoraLogger.Debug($"GodotVRDriver: tracker candidate '{name}' profile='{tracker.Profile}'");

        if (!string.IsNullOrEmpty(tracker.Profile))
            SetTrackerLive(entry, true);
    }

    private void OnTrackerProfileChanged(TrackerEntry entry, string profile)
    {
        SetTrackerLive(entry, !string.IsNullOrEmpty(profile));
    }

    // Runtimes that never bind a profile to the puck still stream poses; the first one is the connect.
    private void OnTrackerPoseChanged(TrackerEntry entry, XRPose pose)
    {
        if (entry.Live || pose == null || pose.Name != DefaultPoseName)
            return;

        entry.Pose = pose;
        SetTrackerLive(entry, true);
    }

    private void SetTrackerLive(TrackerEntry entry, bool live)
    {
        if (live)
        {
            if (entry.Live)
                return;

            if (_inputInterface == null)
            {
                entry.WantsLive = true;
                return;
            }

            entry.Device ??= AcquireTrackerDevice(entry.Name);
            entry.Device.IsDeviceActive = true;
            entry.Live = true;
            entry.WantsLive = false;
            _liveTrackers.Add(entry);

            // The frame loop reads the pose from here on; the signal would be a second copy per frame.
            if (entry.PoseSubscribed)
            {
                entry.Tracker.PoseChanged -= entry.PoseHandler;
                entry.PoseSubscribed = false;
            }

            LumoraLogger.Debug($"GodotVRDriver: body tracker {entry.Device.Name} online, suggested role {entry.Device.SuggestedRole}");
            return;
        }

        entry.WantsLive = false;
        if (!entry.Live)
            return;

        entry.Live = false;
        entry.Pose = null;
        _liveTrackers.Remove(entry);
        entry.Device?.SetOffline();

        if (!entry.PoseSubscribed && GodotObject.IsInstanceValid(entry.Tracker))
        {
            entry.Tracker.PoseChanged += entry.PoseHandler;
            entry.PoseSubscribed = true;
        }

        LumoraLogger.Debug($"GodotVRDriver: body tracker {entry.Device?.Name} offline");
    }

    private TrackerDevice AcquireTrackerDevice(string uniqueId)
    {
        if (_trackerDevices.TryGetValue(uniqueId, out var existing))
        {
            TrackerMappingStore.TryApply(existing);
            return existing;
        }

        var device = new TrackerDevice();
        device.SetIdentity(uniqueId);
        device.SuggestedRole = TrackerRoles.FromRolePath(uniqueId);

        // Never mapped from the role: an uncalibrated puck drives nothing. Set before registering,
        // which applies the saved mapping on top. -xlinka
        device.CorrespondingBodyNode = BodyNode.NONE;
        device.Priority = TrackerPriority;

        string publicId = device.PublicID;
        string shortId = publicId.Length > 12 ? publicId.Substring(0, 12) : publicId;
        _inputInterface.RegisterInputDevice(device, "VR_Tracker_" + shortId);

        _trackerDevices[uniqueId] = device;
        return device;
    }

    // Candidates that went live before RegisterInputs handed us an InputInterface to put devices in.
    private void ActivatePendingTrackers()
    {
        if (_trackerCandidates.Count == 0)
            return;

        foreach (var entry in _trackerCandidates.Values)
        {
            if (entry.WantsLive)
                SetTrackerLive(entry, true);
        }
    }

    private void DetachCandidate(TrackerEntry entry)
    {
        if (entry.Live)
        {
            entry.Live = false;
            _liveTrackers.Remove(entry);
        }

        entry.WantsLive = false;
        entry.Pose = null;
        entry.Device?.SetOffline();

        if (GodotObject.IsInstanceValid(entry.Tracker))
        {
            entry.Tracker.ProfileChanged -= entry.ProfileHandler;
            if (entry.PoseSubscribed)
                entry.Tracker.PoseChanged -= entry.PoseHandler;
        }

        entry.PoseSubscribed = false;
        entry.Tracker = null!;
    }

    // Live pucks only. The cached XRPose is the tracker's own pose object, updated in place by the
    // runtime (set_pose reuses it, invalidate_pose flips has_tracking_data on it), so this reads a few
    // properties and one transform and allocates nothing. Confidence None means no valid pose this
    // frame and the device says so; Low is the runtime extrapolating and still counts as tracking,
    // with the level exposed for anyone who wants to be stricter. -xlinka
    private void UpdateTrackerPoses()
    {
        int count = _liveTrackers.Count;
        if (count == 0)
            return;

        var space = _inputInterface.GlobalTrackingSpace;
        for (int i = 0; i < count; i++)
        {
            var entry = _liveTrackers[i];
            var device = entry.Device!;
            device.TrackingSpace = space;
            device.IsDeviceActive = true;

            var pose = entry.Pose ??= entry.Tracker.GetPose(DefaultPoseName);
            if (pose == null || !pose.HasTrackingData)
            {
                device.SetUntracked();
                continue;
            }

            var confidence = pose.TrackingConfidence;
            if (confidence == XRPose.TrackingConfidenceEnum.None)
            {
                device.SetUntracked();
                continue;
            }

            var transform = pose.Transform;
            var origin = transform.Origin;
            var quat = transform.Basis.GetRotationQuaternion();
            device.SetPose(
                new float3(origin.X, origin.Y, origin.Z),
                new floatQ(quat.X, quat.Y, quat.Z, quat.W),
                confidence == XRPose.TrackingConfidenceEnum.High ? 1f : 0.5f);
        }
    }

    // Platform / interaction profile introspection
    //
    // OpenXRInterface exposes `runtime_name` as a registered Godot property,
    // and XRPositionalTracker exposes the active OpenXR interaction profile
    // path via `Profile`. We go through Get() so this stays compatible across
    // minor Godot versions that may not expose the same C# bindings.
    // - xlinka

    private string GetRuntimeName()
    {
        if (_xrInterface == null) return string.Empty;
        var v = _xrInterface.Get("runtime_name");
        return v.AsString() ?? string.Empty;
    }

    private static readonly StringName LeftHandTrackerName = new StringName("left_hand");
    private static readonly StringName RightHandTrackerName = new StringName("right_hand");

    private static string GetTrackerInteractionProfile(StringName trackerName)
    {
        var tracker = XRServer.GetTracker(trackerName);
        if (tracker is not XRPositionalTracker pt) return string.Empty;
        var profile = pt.Profile;
        return string.IsNullOrEmpty(profile) ? string.Empty : profile;
    }

    // What the bound profile physically has, off its path. An unlisted profile counts as having no
    // face buttons, which puts the dash on a menu hold rather than on a button that may not exist,
    // no force sensor, which keeps the grip on the analog it does report, and no trigger click, which
    // takes the trigger's deep level off its value. -xlinka
    private static bool ProfileHasFaceButtons(string profile)
        => profile.Contains("touch_controller")
           || profile.Contains("pico")
           || profile.Contains("index_controller")
           || profile.Contains("frame_controller_valve")
           || profile.Contains("hp/mixed_reality_controller")
           || profile.Contains("cosmos")
           || profile.Contains("focus3");

    private static bool ProfileHasGripForce(string profile)
        => profile.Contains("index_controller");

    // Profiles whose trigger has a click path of its own. The rest bind trigger_click to the value,
    // which leaves its depth to the runtime.
    private static bool ProfileHasTriggerClick(string profile)
        => profile.Contains("htc/vive_controller")
           || profile.Contains("index_controller")
           || profile.Contains("frame_controller_valve")
           || profile.Contains("cosmos")
           || profile.Contains("focus3")
           || profile.Contains("huawei/controller");

    private static bool ProfileIsHandTracking(string profile)
        => profile.Contains("hand_interaction");

    private static string GetTrackerDescription(string trackerName)
    {
        var tracker = XRServer.GetTracker(trackerName);
        if (tracker == null) return "none";
        if (tracker is XRPositionalTracker positional && !string.IsNullOrWhiteSpace(positional.Description))
            return positional.Description;
        var name = tracker.Name.ToString();
        return string.IsNullOrWhiteSpace(name) ? trackerName : name;
    }

    private VRPlatform DetectPlatform()
    {
        if (!IsVRActive) return VRPlatform.None;
        if (HeadsetDeviceMode.IsSteamFrame) return VRPlatform.SteamFrameStandalone;

        // PC OpenXR runtimes (SteamVR, Oculus PC, WMR, Monado, Varjo, ...)
        // all share the same desktop bucket - mobile/standalone is the
        // distinction that actually changes rendering and feature defaults.
        if (!OS.HasFeature("android")) return VRPlatform.DesktopOpenXR;

        var runtime = GetRuntimeName().ToLowerInvariant();
        if (runtime.Contains("oculus") || runtime.Contains("meta"))     return VRPlatform.MetaQuestStandalone;
        if (runtime.Contains("pico")   || runtime.Contains("bytedance")) return VRPlatform.PicoStandalone;
        if (runtime.Contains("vive")   || runtime.Contains("htc")
                                       || runtime.Contains("focus"))    return VRPlatform.ViveFocusStandalone;

        // Fall back to interaction profile sniffing if the runtime name
        // didn't give us anything (some early standalone runtimes report
        // generic strings until the first controller binds).
        var profiles = (LeftInteractionProfile + "|" + RightInteractionProfile).ToLowerInvariant();
        if (profiles.Contains("oculus"))                                return VRPlatform.MetaQuestStandalone;
        if (profiles.Contains("bytedance") || profiles.Contains("pico")) return VRPlatform.PicoStandalone;
        if (profiles.Contains("htc")       || profiles.Contains("focus3")) return VRPlatform.ViveFocusStandalone;

        return VRPlatform.DesktopOpenXR;
    }

    // XRController3D nodes MUST exist for Godot to track controllers via OpenXR.
    public void FindXRNodes(Node? sceneRoot)
    {
        if (sceneRoot == null) return;

        _xrOrigin = FindNodeOfType<XROrigin3D>(sceneRoot);
        _xrCamera = FindNodeOfType<XRCamera3D>(sceneRoot);

        foreach (var node in GetAllNodes(sceneRoot))
        {
            if (node is XRController3D controller)
            {
                var trackerStr = controller.Tracker.ToString();
                LumoraLogger.Debug($"GodotVRDriver: Found XRController3D '{controller.Name}' with tracker '{trackerStr}'");

                if (trackerStr.Contains("left") || trackerStr == "left_hand")
                    _leftController = controller;
                else if (trackerStr.Contains("right") || trackerStr == "right_hand")
                    _rightController = controller;
            }
        }

        if (_xrOrigin == null || _leftController == null || _rightController == null)
        {
            LumoraLogger.Debug("GodotVRDriver: XR nodes not found, creating them...");
            CreateXRNodes(sceneRoot);
        }

        LumoraLogger.Debug($"GodotVRDriver: XR nodes ready - Origin:{_xrOrigin != null}, Camera:{_xrCamera != null}, Left:{_leftController != null}, Right:{_rightController != null}");
    }

    private void CreateXRNodes(Node sceneRoot)
    {
        if (_xrOrigin == null)
        {
            _xrOrigin = new XROrigin3D();
            _xrOrigin.Name = "XROrigin3D";
            sceneRoot.AddChild(_xrOrigin);
            LumoraLogger.Debug("GodotVRDriver: Created XROrigin3D");
        }

        if (_xrCamera == null)
        {
            _xrCamera = new XRCamera3D();
            _xrCamera.Name = "XRCamera3D";
            _xrOrigin.AddChild(_xrCamera);
            LumoraLogger.Debug("GodotVRDriver: Created XRCamera3D");
        }

        if (_leftController == null)
        {
            _leftController = new XRController3D();
            _leftController.Name = "LeftController";
            _leftController.Tracker = new StringName("left_hand");
            _xrOrigin.AddChild(_leftController);
            LumoraLogger.Debug("GodotVRDriver: Created LeftController (left_hand)");
        }

        if (_rightController == null)
        {
            _rightController = new XRController3D();
            _rightController.Name = "RightController";
            _rightController.Tracker = new StringName("right_hand");
            _xrOrigin.AddChild(_rightController);
            LumoraLogger.Debug("GodotVRDriver: Created RightController (right_hand)");
        }
    }

    private T FindNodeOfType<T>(Node node) where T : Node
    {
        if (node is T result)
            return result;
        foreach (var child in node.GetChildren())
        {
            var found = FindNodeOfType<T>(child);
            if (found != null)
                return found;
        }
        return null!;
    }

    private System.Collections.Generic.IEnumerable<Node> GetAllNodes(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        {
            foreach (var descendant in GetAllNodes(child))
                yield return descendant;
        }
    }

    public void RegisterInputs(InputInterface inputInterface)
    {
        _inputInterface = inputInterface;

        _headTrackedObject = inputInterface.CreateDevice<TrackedObject>("VR_Head");
        _headTrackedObject.CorrespondingBodyNode = BodyNode.Head;
        _headTrackedObject.Priority = 100; // High priority for actual VR tracking

        _leftControllerTrackedObject = inputInterface.CreateDevice<TrackedObject>("VR_LeftController");
        _leftControllerTrackedObject.CorrespondingBodyNode = BodyNode.LeftController;
        _leftControllerTrackedObject.Priority = 100;

        _rightControllerTrackedObject = inputInterface.CreateDevice<TrackedObject>("VR_RightController");
        _rightControllerTrackedObject.CorrespondingBodyNode = BodyNode.RightController;
        _rightControllerTrackedObject.Priority = 100;

        // The hand body node rides the controller pose; the finger devices below hang off it.
        _leftHandTrackedObject = inputInterface.CreateDevice<TrackedObject>("VR_LeftHand");
        _leftHandTrackedObject.CorrespondingBodyNode = BodyNode.LeftHand;
        _leftHandTrackedObject.Priority = 50; // Lower priority than controller

        _rightHandTrackedObject = inputInterface.CreateDevice<TrackedObject>("VR_RightHand");
        _rightHandTrackedObject.CorrespondingBodyNode = BodyNode.RightHand;
        _rightHandTrackedObject.Priority = 50;

        _leftHand = new HandDevice(inputInterface, Chirality.Left, _leftHandTrackedObject);
        _rightHand = new HandDevice(inputInterface, Chirality.Right, _rightHandTrackedObject);

        DiscoverTrackers();
        ActivatePendingTrackers();

        Lumora.Core.Logging.Logger.Log($"GodotVRDriver: Registered VR tracked devices - Head:{_headTrackedObject?.Name}, LeftController:{_leftControllerTrackedObject?.Name}, RightController:{_rightControllerTrackedObject?.Name}");
    }

    private int _debugLogCounter = 0;

    public void UpdateInputs(float deltaTime)
    {
        if (_inputInterface == null)
            return;

        if (!IsVRActive)
        {
            ClearTrackingState();
            return;
        }

        UpdateHeadTracking();

        UpdateControllerTracking(_leftControllerTrackedObject, _leftHandTrackedObject, Chirality.Left);
        UpdateControllerTracking(_rightControllerTrackedObject, _rightHandTrackedObject, Chirality.Right);

        UpdateTrackerPoses();

        // Controller buttons and sticks, apart from the tracked poses above.
        if (_inputInterface.LeftController != null)
            UpdateControllerInputs(_inputInterface.LeftController);
        if (_inputInterface.RightController != null)
            UpdateControllerInputs(_inputInterface.RightController);

        UpdateHand(Chirality.Left);
        UpdateHand(Chirality.Right);

        _debugLogCounter++;
        if (_debugLogCounter >= 60)
        {
            _debugLogCounter = 0;
            var leftPos = _leftControllerTrackedObject?.RawPosition ?? Lumora.Core.Math.float3.Zero;
            var rightPos = _rightControllerTrackedObject?.RawPosition ?? Lumora.Core.Math.float3.Zero;
            var leftTrack = _leftControllerTrackedObject?.IsTracking ?? false;
            var rightTrack = _rightControllerTrackedObject?.IsTracking ?? false;
            var leftThumb = _inputInterface.LeftController?.ThumbstickPosition ?? default;
            // global::Godot.GD.Print($"[VR] L:{leftPos} track:{leftTrack} | R:{rightPos} track:{rightTrack} | Thumb:({leftThumb.X:F2},{leftThumb.Y:F2}) | XRNodes L:{_leftController != null} R:{_rightController != null}");
        }
    }

    // HANDS
    //
    // Two feeds, one pose path. A runtime that exposes the hand skeleton (finger sensing on the handle, or
    // cameras once the controller is put down) fills the hand device from its 26 joints; one that does not
    // gets the hand estimated from the controller's own inputs through the same open preset and curl
    // model. Either way the device holds a pose relative to the controller body node's pose, and when
    // there is no controller a root is derived from the tracked palm so the controller and hand nodes
    // still move. -xlinka
    private HandDevice _leftHand = null!;
    private HandDevice _rightHand = null!;
    private XRHandTracker? _leftHandTracker;
    private XRHandTracker? _rightHandTracker;
    private readonly HandJointSample[] _jointSamples = new HandJointSample[(int)HandJoint.Count];
    private string _leftFeedLogged = string.Empty;
    private string _rightFeedLogged = string.Empty;

    private static readonly StringName LeftHandSkeletonTrackerName = new StringName("/user/hand_tracker/left");
    private static readonly StringName RightHandSkeletonTrackerName = new StringName("/user/hand_tracker/right");

    private void RefreshHandSkeletonTrackers()
    {
        _leftHandTracker = XRServer.GetTracker(LeftHandSkeletonTrackerName) as XRHandTracker;
        _rightHandTracker = XRServer.GetTracker(RightHandSkeletonTrackerName) as XRHandTracker;
    }

    private void UpdateHand(Chirality side)
    {
        bool left = side == Chirality.Left;
        var hand = left ? _leftHand : _rightHand;
        if (hand == null)
            return;

        var controllerObj = left ? _leftControllerTrackedObject : _rightControllerTrackedObject;
        var handObj = left ? _leftHandTrackedObject : _rightHandTrackedObject;
        var tracker = left ? _leftHandTracker : _rightHandTracker;

        hand.SetTrackingSpace(_inputInterface.GlobalTrackingSpace);
        hand.SetDeviceActive(true);

        bool controllerTracking = controllerObj != null && controllerObj.IsTracking;

        if (tracker != null && GodotObject.IsInstanceValid(tracker) && tracker.HasTrackingData && ReadSkeleton(tracker))
        {
            float3 rootPosition;
            floatQ rootRotation;
            if (controllerTracking)
            {
                rootPosition = controllerObj!.RawPosition;
                rootRotation = controllerObj.RawRotation;
            }
            else
            {
                // No controller to hang the hand off, so put the root where a controller would sit for
                // this palm: the synthesized model's palm offset, solved backwards. The skeleton then
                // re-expresses against that root exactly as the controller estimate does. -xlinka
                ref readonly var palm = ref _jointSamples[(int)HandJoint.Palm];
                rootRotation = (palm.Rotation * HandPoseSynth.PalmRotation(side).Inverse).Normalized;
                rootPosition = palm.Position - rootRotation * HandPoseSynth.PalmPosition(side);
                StandInForController(controllerObj, handObj, rootPosition, rootRotation);
            }

            hand.ApplySkeleton(_jointSamples, rootPosition, rootRotation);
            if (LumoraLogger.EnableDebug)
                NoteHandFeed(side, $"runtime hand skeleton, data source {tracker.HandTrackingSource}, root = "
                                  + (controllerTracking ? "controller pose" : "derived from the palm (no controller)"));
            return;
        }

        if (!controllerTracking)
        {
            hand.SetTracking(false);
            return;
        }

        var inputs = ReadControllerHandInputs(side);
        hand.ApplySynthesized(in inputs, controllerObj!.RawPosition, controllerObj.RawRotation);
        NoteHandFeed(side, "controller inputs (synthesized)");
    }

    // All 26 joints into the sample buffer, orientations turned back from Godot's humanoid bone frames
    // to the OpenXR ones the device expects. False when the runtime has no usable root joint this frame.
    private bool ReadSkeleton(XRHandTracker tracker)
    {
        for (int j = 0; j < _jointSamples.Length; j++)
        {
            var joint = (XRHandTracker.HandJoint)j;
            var flags = tracker.GetHandJointFlags(joint);
            var transform = tracker.GetHandJointTransform(joint);
            var origin = transform.Origin;
            var quat = transform.Basis.GetRotationQuaternion();

            ref var sample = ref _jointSamples[j];
            sample.Position = new float3(origin.X, origin.Y, origin.Z);
            sample.Rotation = HandJointConventions.FromGodotHumanoidFrame(new floatQ(quat.X, quat.Y, quat.Z, quat.W));
            sample.Radius = tracker.GetHandJointRadius(joint);
            sample.PositionValid = (flags & XRHandTracker.HandJointFlags.PositionValid) != 0;
            sample.OrientationValid = (flags & XRHandTracker.HandJointFlags.OrientationValid) != 0;
            sample.PositionTracked = (flags & XRHandTracker.HandJointFlags.PositionTracked) != 0;
            sample.OrientationTracked = (flags & XRHandTracker.HandJointFlags.OrientationTracked) != 0;
        }

        ref readonly var palm = ref _jointSamples[(int)HandJoint.Palm];
        ref readonly var wrist = ref _jointSamples[(int)HandJoint.Wrist];
        return palm.PositionValid && palm.OrientationValid && wrist.PositionValid && wrist.OrientationValid;
    }

    private void StandInForController(TrackedObject? controllerObj, TrackedObject? handObj, float3 position, floatQ rotation)
    {
        var space = _inputInterface?.GlobalTrackingSpace!;
        if (controllerObj != null)
        {
            controllerObj.RawPosition = position;
            controllerObj.RawRotation = rotation;
            controllerObj.IsTracking = true;
            controllerObj.IsDeviceActive = true;
            controllerObj.TrackingSpace = space;
        }
        if (handObj != null)
        {
            handObj.RawPosition = position;
            handObj.RawRotation = rotation;
            handObj.IsTracking = true;
            handObj.IsDeviceActive = true;
            handObj.TrackingSpace = space;
        }
    }

    private ControllerHandInputs ReadControllerHandInputs(Chirality side)
    {
        bool left = side == Chirality.Left;
        var vrController = left ? _inputInterface?.LeftController : _inputInterface?.RightController;
        var xrController = left ? _leftController : _rightController;

        var inputs = new ControllerHandInputs();
        if (vrController != null)
        {
            inputs.Trigger = vrController.TriggerValue;
            inputs.Grip = MathF.Max(vrController.GripValue, vrController.GripForce);
            inputs.StickX = vrController.ThumbstickPosition.X;
            inputs.ThumbTouched = vrController.ThumbstickTouched || vrController.TouchpadTouched;
        }

        if (xrController != null && GodotObject.IsInstanceValid(xrController))
        {
            inputs.TriggerTouched = ReadBoolAction(xrController, "trigger_touch");
            inputs.ThumbTouched = inputs.ThumbTouched
                || ReadBoolAction(xrController, "ax_touch")
                || ReadBoolAction(xrController, "by_touch")
                || ReadBoolAction(xrController, "face_left_touch")
                || ReadBoolAction(xrController, "face_up_touch")
                || ReadBoolAction(xrController, "primary_touch")
                || ReadBoolAction(xrController, "secondary_touch");
        }

        if (inputs.ThumbTouched)
            _thumbSensed[left ? 0 : 1] = true;
        inputs.SensesThumb = _thumbSensed[left ? 0 : 1];
        return inputs;
    }

    private readonly bool[] _thumbSensed = new bool[2];

    // Once per change of feed, never per frame: a headset log answers "are these real fingers". -xlinka
    private void NoteHandFeed(Chirality side, string feed)
    {
        ref string logged = ref (side == Chirality.Left ? ref _leftFeedLogged : ref _rightFeedLogged);
        if (logged == feed)
            return;
        logged = feed;
        LumoraLogger.Debug($"GodotVRDriver: {side} hand fingers fed by {feed}");
    }

    private static float ReadFloatAction(XRController3D controller, string action)
    {
        if (controller == null || !GodotObject.IsInstanceValid(controller))
            return 0f;

        float value = controller.GetFloat(action);
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    // GetInput, not Get: Get() is GodotObject's PROPERTY lookup and XRController3D has no property
    // called "ax_button", so every button read came back Nil while the floats (GetFloat) worked. That
    // is why grip, trigger and the stick reached the engine and no face or menu button ever did. -xlinka
    private static bool ReadBoolAction(XRController3D controller, string action)
    {
        if (controller == null || !GodotObject.IsInstanceValid(controller))
            return false;

        Variant value = controller.GetInput(action);
        return value.VariantType switch
        {
            Variant.Type.Bool => value.AsBool(),
            Variant.Type.Float => value.AsSingle() > 0.5f,
            Variant.Type.Int => value.AsInt32() > 0,
            _ => controller.GetFloat(action) > 0.5f
        };
    }

    // Scene-node transforms (XRCamera3D/XRController3D.Position) are written
    // during those nodes' own internal process, in tree order - sampling them
    // from the engine runner can read a frame-stale pose while the headset
    // view is late-latched to the freshest one. The XR server pose data is
    // updated at frame begin, before any process callbacks, so hands/head
    // stay in lockstep with the rendered view. - xlinka
    private void UpdateHeadTracking()
    {
        if (_headTrackedObject == null)
            return;

        bool isTracking = false;
        float3 position = new float3(0, 1.7f, 0);
        floatQ rotation = floatQ.Identity;

        if (IsVRActive)
        {
            Transform3D headTransform = XRServer.GetHmdTransform();

            if (headTransform != Transform3D.Identity)
            {
                var pos = headTransform.Origin;
                position = new float3(pos.X, pos.Y, pos.Z);

                var quat = headTransform.Basis.GetRotationQuaternion();
                rotation = new floatQ(quat.X, quat.Y, quat.Z, quat.W);
                isTracking = true;
            }
            // Fall back to the camera node (playspace-relative, same space)
            else if (_xrCamera != null && GodotObject.IsInstanceValid(_xrCamera))
            {
                var pos = _xrCamera.Position;
                var quat = _xrCamera.Quaternion;

                position = new float3(pos.X, pos.Y, pos.Z);
                rotation = new floatQ(quat.X, quat.Y, quat.Z, quat.W);
                isTracking = true;
            }
        }

        _headTrackedObject.RawPosition = position;
        _headTrackedObject.RawRotation = rotation;
        _headTrackedObject.IsTracking = isTracking;
        _headTrackedObject.IsDeviceActive = isTracking;
        _headTrackedObject.TrackingSpace = _inputInterface?.GlobalTrackingSpace!;
    }

    // XRController3D node transform is a fallback; it can be a frame stale (see UpdateHeadTracking)
    private void UpdateControllerTracking(TrackedObject controllerObj, TrackedObject handObj, Chirality side)
    {
        if (controllerObj == null)
            return;

        bool isTracking = false;
        float3 position = float3.Zero;
        floatQ rotation = floatQ.Identity;

        var xrController = side == Chirality.Left ? _leftController : _rightController;

        if (IsVRActive)
        {
            // Godot 4 standard tracker names. (The old "/user/hand/left" form is
            // an OpenXR action path, not a Godot tracker name - it never resolved.)
            var trackerName = new StringName(side == Chirality.Left ? "left_hand" : "right_hand");
            if (XRServer.GetTracker(trackerName) is XRPositionalTracker positionalTracker)
            {
                // Query the same pose the controller node renders with so the
                // hand offset doesn't shift, then fall through grip/aim/default.
                XRPose? pose = null;
                if (xrController != null && GodotObject.IsInstanceValid(xrController) && !xrController.Pose.IsEmpty)
                    pose = positionalTracker.GetPose(xrController.Pose);
                if (pose == null || !pose.HasTrackingData)
                    pose = positionalTracker.GetPose(new StringName("grip"));
                if (pose == null || !pose.HasTrackingData)
                    pose = positionalTracker.GetPose(new StringName("aim"));
                if (pose == null || !pose.HasTrackingData)
                    pose = positionalTracker.GetPose(new StringName("default"));

                if (pose != null && pose.HasTrackingData && pose.Transform != Transform3D.Identity)
                {
                    var transform = pose.Transform;
                    position = new float3(transform.Origin.X, transform.Origin.Y, transform.Origin.Z);

                    var quat = transform.Basis.GetRotationQuaternion();
                    rotation = new floatQ(quat.X, quat.Y, quat.Z, quat.W);
                    isTracking = true;
                }
            }
        }

        // Fall back to the controller node transform (playspace-relative)
        if (!isTracking && xrController != null && GodotObject.IsInstanceValid(xrController) && xrController.GetIsActive())
        {
            var pos = xrController.Position;
            var quat = xrController.Quaternion;

            position = new float3(pos.X, pos.Y, pos.Z);
            rotation = new floatQ(quat.X, quat.Y, quat.Z, quat.W);
            isTracking = true;
        }

        controllerObj.RawPosition = position;
        controllerObj.RawRotation = rotation;
        controllerObj.IsTracking = isTracking;
        controllerObj.IsDeviceActive = isTracking;
        controllerObj.TrackingSpace = _inputInterface?.GlobalTrackingSpace!;

        // Hands take the controller pose; no separate hand-tracking source.
        if (handObj != null)
        {
            handObj.RawPosition = position;
            handObj.RawRotation = rotation;
            handObj.IsTracking = isTracking;
            handObj.IsDeviceActive = isTracking;
            handObj.TrackingSpace = _inputInterface?.GlobalTrackingSpace!;
        }
    }

    // legacy compatibility with old IVRDriver interface
    public void UpdateVRDevices(VRController leftController, VRController rightController, HeadDevice headDevice)
    {
        if (!IsVRActive)
        {
            ClearLegacyDevice(headDevice);
            OpenXREyeGaze.Release(headDevice);
            ClearLegacyDevice(leftController);
            ClearLegacyDevice(rightController);
            ClearTrackingState();
            return;
        }

        UpdateHeadDevice(headDevice);
        UpdateController(leftController, Chirality.Left);
        UpdateController(rightController, Chirality.Right);
    }

    private void ClearTrackingState()
    {
        ClearTrackedObject(_headTrackedObject);
        ClearTrackedObject(_leftControllerTrackedObject);
        ClearTrackedObject(_rightControllerTrackedObject);
        ClearTrackedObject(_leftHandTrackedObject);
        ClearTrackedObject(_rightHandTrackedObject);

        _leftHand?.SetTracking(false);
        _rightHand?.SetTracking(false);
        _leftFeedLogged = string.Empty;
        _rightFeedLogged = string.Empty;

        if (_trackerDevices.Count > 0)
        {
            foreach (var device in _trackerDevices.Values)
                device.SetOffline();
        }
    }

    private static void ClearTrackedObject(TrackedObject tracked)
    {
        if (tracked == null)
            return;

        tracked.RawPosition = float3.Zero;
        tracked.RawRotation = floatQ.Identity;
        tracked.IsTracking = false;
        tracked.IsDeviceActive = false;
        tracked.TrackingSpace = null!;
    }

    private static void ClearLegacyDevice(HeadDevice headDevice)
    {
        if (headDevice == null)
            return;

        headDevice.Position = Vector3.Zero;
        headDevice.Rotation = Quaternion.Identity;
        headDevice.IsTracked = false;
        headDevice.IsWorn = false;
        headDevice.TrackingConfidence = 0f;
        headDevice.IsDeviceActive = false;
    }

    private static void ClearLegacyDevice(VRController controller)
    {
        if (controller == null)
            return;

        controller.Position = Vector3.Zero;
        controller.Rotation = Quaternion.Identity;
        controller.IsTracked = false;
        controller.IsDeviceActive = false;
        controller.ThumbstickPosition = Vector2.Zero;
        controller.TriggerValue = 0f;
        controller.TriggerClick = false;
        controller.GripValue = 0f;
        controller.GripForce = 0f;
        controller.PrimaryButtonPressed = false;
        controller.SecondaryButtonPressed = false;
        controller.MenuButtonPressed = false;
        controller.ThumbstickPressed = false;
        controller.ThumbstickTouched = false;
        controller.TouchpadPosition = Vector2.Zero;
        controller.TouchpadPressed = false;
        controller.TouchpadTouched = false;
        controller.TriggerTouched = false;
        controller.PrimaryButtonTouched = false;
        controller.SecondaryButtonTouched = false;
        controller.FaceLeftPressed = false;
        controller.FaceUpPressed = false;
        controller.ShoulderPressed = false;
        controller.FaceLeftTouched = false;
        controller.FaceUpTouched = false;
        controller.ShoulderTouched = false;
        controller.ResetLatches();
    }

    private void UpdateHeadDevice(HeadDevice headDevice)
    {
        if (headDevice == null)
            return;

        if (_headTrackedObject != null)
        {
            headDevice.Position = new Vector3(_headTrackedObject.RawPosition.x, _headTrackedObject.RawPosition.y, _headTrackedObject.RawPosition.z);
            headDevice.Rotation = new Quaternion(_headTrackedObject.RawRotation.x, _headTrackedObject.RawRotation.y, _headTrackedObject.RawRotation.z, _headTrackedObject.RawRotation.w);
            headDevice.IsTracked = _headTrackedObject.IsTracking;
            headDevice.IsWorn = _headTrackedObject.IsTracking;
            headDevice.TrackingConfidence = _headTrackedObject.IsTracking ? 1.0f : 0f;
            headDevice.IsDeviceActive = _headTrackedObject.IsDeviceActive;
        }

        // Where the eyes are pointing, when the headset can tell us. Everything downstream of this
        // has been waiting for a source since it was written. -xlinka
        OpenXREyeGaze.Update(headDevice);
    }

    private void UpdateController(VRController controller, Chirality side)
    {
        if (controller == null)
            return;

        var trackedObj = side == Chirality.Left ? _leftControllerTrackedObject : _rightControllerTrackedObject;
        if (trackedObj != null)
        {
            controller.Position = new Vector3(trackedObj.RawPosition.x, trackedObj.RawPosition.y, trackedObj.RawPosition.z);
            controller.Rotation = new Quaternion(trackedObj.RawRotation.x, trackedObj.RawRotation.y, trackedObj.RawRotation.z, trackedObj.RawRotation.w);
            controller.IsTracked = trackedObj.IsTracking;
            controller.IsDeviceActive = trackedObj.IsDeviceActive;
        }

        UpdateControllerInputs(controller);
    }

    private void UpdateControllerInputs(VRController controller)
    {
        var xrController = controller.Side == VRControllerSide.Left ? _leftController : _rightController;
        string profile = controller.Side == VRControllerSide.Left ? LeftInteractionProfile : RightInteractionProfile;
        ref string previousProfile = ref (controller.Side == VRControllerSide.Left ? ref _leftInputProfile : ref _rightInputProfile);
        if (previousProfile != profile)
        {
            previousProfile = profile;
            LumoraLogger.Log($"GodotVRDriver: {controller.Side} controller profile='{profile}'");
            // A user can swap controllers without restarting SteamVR. An Index force sensor seen
            // earlier must not make Frame's ordinary squeeze stop grabbing.
            if (controller.Side == VRControllerSide.Left) { _leftGripForceSeen = false; _leftTriggerClickSeen = false; }
            else { _rightGripForceSeen = false; _rightTriggerClickSeen = false; }
            _thumbSensed[controller.Side == VRControllerSide.Left ? 0 : 1] = false;
            controller.ResetLatches();
            controller.DeviceName = profile.Contains("frame_controller_valve") ? "Steam Frame Controller"
                : GetTrackerDescription(controller.Side == VRControllerSide.Left ? "left_hand" : "right_hand");
        }

        global::Godot.Vector2 thumbstick = global::Godot.Vector2.Zero;
        global::Godot.Vector2 touchpad = global::Godot.Vector2.Zero;
        float trigger = 0f;
        bool triggerClick = false;
        float grip = 0f;
        float gripForce = 0f;
        bool primaryButton = false;
        bool secondaryButton = false;
        bool menuPressed = false;
        bool stickPressed = false;
        bool stickTouched = false;
        bool padPressed = false;
        bool padTouched = false;
        bool triggerTouched = false;
        bool primaryTouched = false;
        bool secondaryTouched = false;
        bool faceLeft = false, faceUp = false, shoulder = false;
        bool faceLeftTouch = false, faceUpTouch = false, shoulderTouch = false;

        // Method 1: Try XRController3D if available (requires action map bindings to work)
        if (xrController != null && GodotObject.IsInstanceValid(xrController))
        {
            thumbstick = xrController.GetVector2("primary");
            touchpad = xrController.GetVector2("secondary");
            trigger = xrController.GetFloat("trigger");
            triggerClick = ReadBoolAction(xrController, "trigger_click");
            grip = xrController.GetFloat("grip");
            // The force sensor, where the profile binds one. It stays at zero from a relaxed hold
            // while the squeeze value above does not, which is why the press comes off it. -xlinka
            gripForce = ReadFloatAction(xrController, "grip_force");

            primaryButton = ReadBoolAction(xrController, "ax_button");
            secondaryButton = ReadBoolAction(xrController, "by_button");
            menuPressed = ReadBoolAction(xrController, "menu_button");
            stickPressed = ReadBoolAction(xrController, "primary_click");
            stickTouched = ReadBoolAction(xrController, "primary_touch");
            padPressed = ReadBoolAction(xrController, "secondary_click");
            padTouched = ReadBoolAction(xrController, "secondary_touch");
            triggerTouched = ReadBoolAction(xrController, "trigger_touch");
            primaryTouched = ReadBoolAction(xrController, "ax_touch");
            secondaryTouched = ReadBoolAction(xrController, "by_touch");
            faceLeft = ReadBoolAction(xrController, "face_left_button");
            faceUp = ReadBoolAction(xrController, "face_up_button");
            shoulder = ReadBoolAction(xrController, "shoulder_button");
            faceLeftTouch = ReadBoolAction(xrController, "face_left_touch");
            faceUpTouch = ReadBoolAction(xrController, "face_up_touch");
            shoulderTouch = ReadBoolAction(xrController, "shoulder_touch");
        }

        // An idle XR stick is valid input. Joypad indices are unrelated to XR hands: Steam Input
        // can expose Frame as ONE gamepad, and copying that pad into a hand caused phantom presses.
        // Flat-screen gamepad input belongs to GodotGamepadDriver.

        ref bool forceSeen = ref (controller.Side == VRControllerSide.Left ? ref _leftGripForceSeen : ref _rightGripForceSeen);
        if (gripForce > GripForcePresenceFloor)
            forceSeen = true;
        ref bool clickSeen = ref (controller.Side == VRControllerSide.Left ? ref _leftTriggerClickSeen : ref _rightTriggerClickSeen);
        if (triggerClick)
            clickSeen = true;

        controller.ThumbstickPosition = new Vector2(thumbstick.X, thumbstick.Y);
        controller.TouchpadPosition = new Vector2(touchpad.X, touchpad.Y);
        // Raw values only. The press edges are latched once a frame on the controller, because this
        // runs twice a frame, once per interface the driver is registered on. -xlinka
        controller.TriggerValue = trigger;
        controller.TriggerClick = triggerClick;
        controller.HasTriggerClick = clickSeen && ProfileHasTriggerClick(profile);
        controller.TriggerIsPinch = ProfileIsHandTracking(profile);
        controller.GripValue = grip;
        controller.GripForce = gripForce;
        controller.HasGripForce = forceSeen || ProfileHasGripForce(profile);
        controller.HasFaceButtons = ProfileHasFaceButtons(profile);
        controller.PrimaryButtonPressed = primaryButton;
        controller.SecondaryButtonPressed = secondaryButton;
        controller.MenuButtonPressed = menuPressed;
        controller.ThumbstickPressed = stickPressed;
        controller.ThumbstickTouched = stickTouched;
        controller.TouchpadPressed = padPressed;
        controller.TouchpadTouched = padTouched;
        controller.TriggerTouched = triggerTouched;
        controller.PrimaryButtonTouched = primaryTouched;
        controller.SecondaryButtonTouched = secondaryTouched;
        controller.FaceLeftPressed = faceLeft;
        controller.FaceUpPressed = faceUp;
        controller.ShoulderPressed = shoulder;
        controller.FaceLeftTouched = faceLeftTouch;
        controller.FaceUpTouched = faceUpTouch;
        controller.ShoulderTouched = shoulderTouch;

        // First press of each control is logged once, so a headset log answers "does the app get my
        // buttons" without a debugger attached. -xlinka
        NoteFirstPress(controller.Side, "primary", primaryButton);
        NoteFirstPress(controller.Side, "secondary", secondaryButton);
        NoteFirstPress(controller.Side, "menu", menuPressed);
        NoteFirstPress(controller.Side, "trigger", trigger > 0.5f);
        NoteFirstPress(controller.Side, "triggerclick", triggerClick);
        NoteFirstPress(controller.Side, "grip", controller.GripPressed);
        NoteFirstPress(controller.Side, "gripforce", gripForce > GripForcePresenceFloor);
        NoteFirstPress(controller.Side, "stick", thumbstick.LengthSquared() > 0.25f);
        NoteFirstPress(controller.Side, "stickclick", stickPressed);
        NoteFirstPress(controller.Side, "padclick", padPressed);

        DrainHaptics(controller, xrController);
    }

    private readonly HashSet<string> _seenPresses = new();

    // A force reading above this once means the handle has a sensor whatever the profile string
    // says, and the press stays on the force from then on.
    private const float GripForcePresenceFloor = 0.05f;
    private bool _leftGripForceSeen;
    private bool _rightGripForceSeen;

    // A profile's trigger click only counts once it has come through, so a runtime that binds it and
    // never sends it still gets a deep level off the value.
    private bool _leftTriggerClickSeen;
    private bool _rightTriggerClickSeen;
    private string _leftInputProfile = string.Empty;
    private string _rightInputProfile = string.Empty;

    private void NoteFirstPress(VRControllerSide side, string control, bool pressed)
    {
        if (!pressed || !LumoraLogger.EnableDebug)
            return;
        if (_seenPresses.Add($"{side}/{control}"))
            LumoraLogger.Debug($"GodotVRDriver: first '{control}' press on the {side} controller");
    }

    // Hand any pulse the engine queued this frame to the runtime. Reading it CLEARS it, so a pulse
    // requested while VR is off or the node is gone is dropped rather than saved up and fired all at
    // once the moment a headset appears.
    //
    // Amplitude 0 means nothing was requested, which is the common case - this runs twice a frame all
    // session. Frequency 0 hands the choice to the runtime, which is what you want: controllers
    // disagree wildly about what a usable frequency is and picking one here gets you silence on
    // hardware that does not do it. -xlinka
    private static void DrainHaptics(VRController controller, XRController3D? xrController)
    {
        var (amplitude, duration, frequency) = controller.GetPendingHaptic();
        if (amplitude <= 0f || duration <= 0f)
            return;

        if (xrController == null || !GodotObject.IsInstanceValid(xrController))
            return;

        xrController.TriggerHapticPulse(
            HapticActionName,
            frequency > 0f ? frequency : 0.0,
            global::System.Math.Clamp(amplitude, 0f, 1f),
            duration,
            0.0);
    }

    // Standard OpenXR output action name; the action map ships it bound on every profile.
    private const string HapticActionName = "haptic";

    public void ShutdownVR()
    {
        if (_trackerSignalsConnected)
        {
            XRServer.TrackerAdded -= OnTrackerAdded;
            XRServer.TrackerRemoved -= OnTrackerRemoved;
            _trackerSignalsConnected = false;
        }

        foreach (var entry in _trackerCandidates.Values)
            DetachCandidate(entry);
        _trackerCandidates.Clear();
        _liveTrackers.Clear();

        _modeActive = false;
        ClearTrackingState();
        _xrInterface = null!;
        LumoraLogger.Debug("GodotVRDriver: VR shutdown");
    }
}

