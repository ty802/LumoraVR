// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Input;

// Where a hand's finger data is coming from this frame.
public enum HandFeed
{
    None,
    RuntimeSkeleton,
    Synthesized,
}

// One joint as a runtime skeleton reports it, in tracking space. Order and count follow the 26-joint
// hand skeleton every OpenXR-style runtime exposes. Rotation is the OpenXR XR_EXT_hand_tracking joint
// frame and nothing else: +Y out of the back of the hand, +Z back along the bone away from the
// fingertip (so -Z toward the next joint), +X completing the right-handed set. HandPoseSynth builds the
// same frame, so a reader whose runtime hands joints over in another convention converts them before
// filling this, or every finger lands ~155 degrees off and curls out the back of the hand. -xlinka
public struct HandJointSample
{
    public float3 Position;
    public floatQ Rotation;
    public float Radius;
    public bool PositionValid;
    public bool OrientationValid;
    public bool PositionTracked;
    public bool OrientationTracked;
}

// Godot stores XRHandTracker joints in its humanoid bone convention: every orientation, palm and wrist
// included, is the runtime's OpenXR frame multiplied by one fixed half turn so that +Y runs along the
// bone toward the tip and -Z points out of the back of the hand. Positions and radii are untouched.
// Multiplying by the inverse on the way in gives back the OpenXR frame HandJointSample carries. -xlinka
public static class HandJointConventions
{
    // Godot's turn, applied after the joint's own rotation: OpenXR +Z to -Y, OpenXR +Y to -Z.
    public static readonly floatQ GodotHumanoidAdjustment = new floatQ(0f, -0.70710678f, 0.70710678f, 0f);
    private static readonly floatQ GodotHumanoidAdjustmentInverse = GodotHumanoidAdjustment.Inverse;

    public static floatQ FromGodotHumanoidFrame(floatQ godotJointRotation)
        => (godotJointRotation * GodotHumanoidAdjustmentInverse).Normalized;
}

public enum HandJoint
{
    Palm = 0,
    Wrist = 1,
    ThumbMetacarpal = 2,
    ThumbProximal = 3,
    ThumbDistal = 4,
    ThumbTip = 5,
    IndexMetacarpal = 6,
    IndexProximal = 7,
    IndexIntermediate = 8,
    IndexDistal = 9,
    IndexTip = 10,
    MiddleMetacarpal = 11,
    MiddleProximal = 12,
    MiddleIntermediate = 13,
    MiddleDistal = 14,
    MiddleTip = 15,
    RingMetacarpal = 16,
    RingProximal = 17,
    RingIntermediate = 18,
    RingDistal = 19,
    RingTip = 20,
    PinkyMetacarpal = 21,
    PinkyProximal = 22,
    PinkyIntermediate = 23,
    PinkyDistal = 24,
    PinkyTip = 25,
    Count = 26,
}

// A finger segment: the tracked object other systems already read (tracking space, body node bound)
// plus the same pose relative to the hand root.
public sealed class HandSegment
{
    public readonly HandFinger Finger;
    public readonly FingerSegmentType SegmentType;
    public readonly BodyNode Node;
    public readonly int Index;
    public readonly TrackedObject Tracked;

    public float3 LocalPosition;
    public floatQ LocalRotation = floatQ.Identity;
    public float Radius = HandPoseSynth.DefaultJointRadius;

    // The same segment in the poser's hand space (see HandDevice.HandFrameRotation).
    public float3 HandSpacePosition;
    public floatQ HandSpaceRotation = floatQ.Identity;

    public bool IsTracking => Tracked.IsTracking;

    internal HandSegment(HandFinger finger, FingerSegmentType segmentType, TrackedObject tracked)
    {
        Finger = finger;
        SegmentType = segmentType;
        Node = finger.Type.ComposeFinger(segmentType, finger.Hand.Side);
        Index = Node.GetFingerSegmentIndex();
        Tracked = tracked;
    }

    public HandSegment? PreviousSegment => SegmentType switch
    {
        FingerSegmentType.Proximal => Finger.Metacarpal,
        FingerSegmentType.Intermediate => Finger.Proximal,
        FingerSegmentType.Distal => Finger.Type == FingerType.Thumb ? Finger.Proximal : Finger.Intermediate,
        FingerSegmentType.Tip => Finger.Distal,
        _ => null,
    };

    public HandSegment? NextSegment => SegmentType switch
    {
        FingerSegmentType.Metacarpal => Finger.Proximal,
        FingerSegmentType.Proximal => Finger.Type == FingerType.Thumb ? Finger.Distal : Finger.Intermediate,
        FingerSegmentType.Intermediate => Finger.Distal,
        FingerSegmentType.Distal => Finger.Tip,
        _ => null,
    };
}

public sealed class HandFinger : IEnumerable<HandSegment>
{
    public readonly HandDevice Hand;
    public readonly FingerType Type;
    public readonly HandSegment Metacarpal;
    public readonly HandSegment Proximal;
    public readonly HandSegment? Intermediate;
    public readonly HandSegment Distal;
    public readonly HandSegment Tip;

    internal HandFinger(HandDevice hand, FingerType type, Func<BodyNode, TrackedObject> acquire)
    {
        Hand = hand;
        Type = type;
        Metacarpal = Make(FingerSegmentType.Metacarpal, acquire);
        Proximal = Make(FingerSegmentType.Proximal, acquire);
        if (type != FingerType.Thumb)
            Intermediate = Make(FingerSegmentType.Intermediate, acquire);
        Distal = Make(FingerSegmentType.Distal, acquire);
        Tip = Make(FingerSegmentType.Tip, acquire);
    }

    private HandSegment Make(FingerSegmentType seg, Func<BodyNode, TrackedObject> acquire)
        => new HandSegment(this, seg, acquire(Type.ComposeFinger(seg, Hand.Side)));

    public HandSegment this[FingerSegmentType segment] => segment switch
    {
        FingerSegmentType.Metacarpal => Metacarpal,
        FingerSegmentType.Proximal => Proximal,
        FingerSegmentType.Intermediate => Intermediate ?? throw new ArgumentException("The thumb has no intermediate segment"),
        FingerSegmentType.Distal => Distal,
        FingerSegmentType.Tip => Tip,
        _ => throw new ArgumentException($"Invalid segment: {segment}"),
    };

    public IEnumerator<HandSegment> GetEnumerator()
    {
        yield return Metacarpal;
        yield return Proximal;
        if (Intermediate != null)
            yield return Intermediate;
        yield return Distal;
        yield return Tip;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// One hand as an input device: five fingers of tracked segments, a palm and a wrist, fed either by a
// runtime skeleton or by a controller estimate, and read the same way whichever it was.
//
// Three views of one write. Each segment's TrackedObject is what the body node table and every existing
// reader (hand visual, laser tip, pose stream) already consume, so it stays in tracking space. The same
// segment keeps its pose relative to the hand root: the pose the side's controller body node carries
// (the action map binds it to the aim pose), or the runtime's palm when there is no controller to carry
// one. Root-relative poses use engine axes: -Z forward, +Y up, +X right, right-handed, and a segment
// frame runs -Z along its bone toward the next segment with +Y out of the back of the hand, which is the
// convention runtime skeletons arrive in. The third view is the finger poser's hand space from
// IHandPoseSource: origin at the wrist, +Z along the extended fingers, +Y out of the back of the hand.
// That is the wrist frame turned half a turn about its own Y, applied to positions and to every segment
// frame alike, so a segment lying flat along the hand reads identity there. -xlinka
public sealed class HandDevice : InputDevice, IEnumerable<HandFinger>
{
    public const int SegmentTrackedPriority = 120;

    public readonly Chirality Side;
    public readonly HandFinger Thumb;
    public readonly HandFinger Index;
    public readonly HandFinger Middle;
    public readonly HandFinger Ring;
    public readonly HandFinger Pinky;

    public readonly TrackedObject Palm;
    public readonly TrackedObject Wrist;

    public bool IsTracking { get; private set; }
    public bool TracksMetacarpals { get; set; }
    public float Confidence { get; private set; }
    public float GrabStrength { get; private set; }
    public float PinchStrength { get; private set; }
    public HandFeed Feed { get; private set; }

    // Tracking-space pose every root-relative pose hangs off.
    public float3 RootPosition { get; private set; }
    public floatQ RootRotation { get; private set; } = floatQ.Identity;

    public float3 PalmLocalPosition { get; private set; }
    public floatQ PalmLocalRotation { get; private set; } = floatQ.Identity;
    public float3 WristLocalPosition { get; private set; }
    public floatQ WristLocalRotation { get; private set; } = floatQ.Identity;

    // Avatar sockets use -Z toward the fingers and +Y toward the thumb. Runtime wrist joints
    // instead use +Y out of the back of the hand. Keep that conversion separate from laser aim.
    public bool TryGetSocketPose(out float3 position, out floatQ rotation)
    {
        position = RootPosition + RootRotation * WristLocalPosition;
        rotation = (RootRotation * WristLocalRotation * HandPoseSynth.WristRotation(Side).Inverse).Normalized;
        return IsTracking && float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z)
            && float.IsFinite(rotation.x) && float.IsFinite(rotation.y) && float.IsFinite(rotation.z) && float.IsFinite(rotation.w);
    }

    // The poser's hand frame, root relative: at the wrist, +Z along the fingers, +Y out of the back.
    public float3 HandFrameOrigin => WristLocalPosition;
    public floatQ HandFrameRotation { get; private set; } = floatQ.Identity;
    public float3 PalmHandSpacePosition { get; private set; }
    public floatQ PalmHandSpaceRotation { get; private set; } = floatQ.Identity;

    private static readonly floatQ HalfTurnAboutY = floatQ.AxisAngle(float3.Up, MathF.PI);

    private readonly HandSegment[] _segments = new HandSegment[HandPoseSynth.SegmentCount];
    private readonly float3[] _scratchPositions = new float3[HandPoseSynth.SegmentCount];
    private readonly floatQ[] _scratchRotations = new floatQ[HandPoseSynth.SegmentCount];
    private readonly float[] _curl = new float[HandPoseSynth.FingerCount];
    private readonly float[] _splay = new float[HandPoseSynth.FingerCount];
    private TrackingSpace? _space;

    public const float SynthesizedConfidence = 0.2f;

    // Reference ratios the strengths are measured against, from the pose model itself so a synthesized
    // fist reads exactly 1 and an open hand exactly 0. Per side, built once. -xlinka
    private static readonly StrengthReference?[] References = new StrengthReference?[3];

    private sealed class StrengthReference
    {
        public readonly float[] OpenReach = new float[HandPoseSynth.FingerCount];
        public readonly float[] ClosedReach = new float[HandPoseSynth.FingerCount];
        public float OpenPinch;
        public float KnuckleSpan;
    }

    public HandDevice(InputInterface input, Chirality side, TrackedObject? wrist = null, int priority = SegmentTrackedPriority)
    {
        if (side == Chirality.None)
            throw new ArgumentException("A hand needs a side");
        Side = side;

        input.RegisterInputDevice(this, side + " Hand");

        Palm = input.CreateDevice<TrackedObject>("VR_" + side.PalmNode());
        Palm.CorrespondingBodyNode = side.PalmNode();
        Palm.Priority = priority;

        if (wrist == null)
        {
            wrist = input.CreateDevice<TrackedObject>("VR_" + side.HandNode());
            wrist.CorrespondingBodyNode = side.HandNode();
            wrist.Priority = priority;
        }
        Wrist = wrist;

        TrackedObject Acquire(BodyNode node)
        {
            var tracked = input.CreateDevice<TrackedObject>("VR_" + node);
            tracked.CorrespondingBodyNode = node;
            tracked.Priority = priority;
            return tracked;
        }

        Thumb = new HandFinger(this, FingerType.Thumb, Acquire);
        Index = new HandFinger(this, FingerType.Index, Acquire);
        Middle = new HandFinger(this, FingerType.Middle, Acquire);
        Ring = new HandFinger(this, FingerType.Ring, Acquire);
        Pinky = new HandFinger(this, FingerType.Pinky, Acquire);

        foreach (var finger in this)
            foreach (var segment in finger)
                _segments[segment.Index] = segment;

        TracksMetacarpals = true;
        SetTracking(false, force: true);
    }

    public HandFinger this[FingerType type] => type switch
    {
        FingerType.Thumb => Thumb,
        FingerType.Index => Index,
        FingerType.Middle => Middle,
        FingerType.Ring => Ring,
        FingerType.Pinky => Pinky,
        _ => throw new ArgumentException($"Invalid finger: {type}"),
    };

    public HandSegment this[BodyNode node]
    {
        get
        {
            if (node.GetChirality() != Side)
                throw new ArgumentException($"{node} is not on the {Side} hand");
            int index = node.GetFingerSegmentIndex();
            if (index < 0)
                throw new ArgumentException($"{node} is not a finger segment");
            return _segments[index];
        }
    }

    public bool TryGetSegment(BodyNode node, out HandSegment segment)
    {
        int index = node.GetFingerSegmentIndex();
        if (index < 0 || node.GetChirality() != Side)
        {
            segment = null!;
            return false;
        }
        segment = _segments[index];
        return true;
    }

    public IEnumerator<HandFinger> GetEnumerator()
    {
        yield return Thumb;
        yield return Index;
        yield return Middle;
        yield return Ring;
        yield return Pinky;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void SetTrackingSpace(TrackingSpace? space)
    {
        _space = space;
        Palm.TrackingSpace = space!;
        for (int i = 0; i < _segments.Length; i++)
            _segments[i].Tracked.TrackingSpace = space!;
    }

    // Tracking on or off for the whole hand: palm and every segment follow. The wrist object belongs to
    // the controller path and keeps its own flag.
    public void SetTracking(bool tracking, bool force = false)
    {
        if (IsTracking == tracking && !force)
            return;
        IsTracking = tracking;
        Palm.IsTracking = tracking;
        Palm.IsDeviceActive = tracking;
        for (int i = 0; i < _segments.Length; i++)
        {
            var tracked = _segments[i].Tracked;
            tracked.IsTracking = tracking;
            tracked.IsDeviceActive = tracking;
        }
        if (!tracking)
        {
            Feed = HandFeed.None;
            Confidence = 0f;
            GrabStrength = 0f;
            PinchStrength = 0f;
        }
    }

    public void SetDeviceActive(bool active)
    {
        IsDeviceActive = active;
        Palm.IsDeviceActive = active && Palm.IsTracking;
        for (int i = 0; i < _segments.Length; i++)
            _segments[i].Tracked.IsDeviceActive = active && _segments[i].Tracked.IsTracking;
    }

    public void SetRoot(float3 position, floatQ rotation)
    {
        RootPosition = position;
        RootRotation = rotation;
    }

    // One segment, hand-root relative in; the tracked object gets the tracking-space composition.
    public void SetSegment(int index, float3 localPosition, floatQ localRotation, float radius = HandPoseSynth.DefaultJointRadius)
    {
        var segment = _segments[index];
        segment.LocalPosition = localPosition;
        segment.LocalRotation = localRotation;
        segment.Radius = radius;
        Compose(segment.Tracked, localPosition, localRotation);
    }

    public void SetPalm(float3 localPosition, floatQ localRotation)
    {
        PalmLocalPosition = localPosition;
        PalmLocalRotation = localRotation;
        Compose(Palm, localPosition, localRotation);
    }

    public void SetWrist(float3 localPosition, floatQ localRotation)
    {
        WristLocalPosition = localPosition;
        WristLocalRotation = localRotation;
    }

    private void Compose(TrackedObject tracked, float3 localPosition, floatQ localRotation)
    {
        tracked.RawPosition = RootPosition + RootRotation * localPosition;
        tracked.RawRotation = RootRotation * localRotation;
        tracked.TrackingSpace = _space!;
    }

    // Runtime skeleton in tracking space, re-expressed against the given root. Palm goes to the palm
    // object, the wrist becomes the root-relative wrist offset, the 24 finger joints land on the
    // segments in body node order (the two skeletons list them identically).
    public void ApplySkeleton(ReadOnlySpan<HandJointSample> joints, float3 rootPosition, floatQ rootRotation)
    {
        if (joints.Length != (int)HandJoint.Count)
            throw new ArgumentException("A hand skeleton has 26 joints");

        SetRoot(rootPosition, rootRotation);
        floatQ inverse = rootRotation.Inverse;

        float confidence = 0f;
        for (int j = 0; j < joints.Length; j++)
        {
            ref readonly var joint = ref joints[j];
            float3 local = inverse * (joint.Position - rootPosition);
            floatQ rotation = (inverse * joint.Rotation).Normalized;
            switch ((HandJoint)j)
            {
                case HandJoint.Palm:
                    SetPalm(local, rotation);
                    break;
                case HandJoint.Wrist:
                    SetWrist(local, rotation);
                    break;
                default:
                    SetSegment(j - (int)HandJoint.ThumbMetacarpal, local, rotation, joint.Radius > 0f ? joint.Radius : HandPoseSynth.DefaultJointRadius);
                    break;
            }
            confidence += JointConfidence(in joint);
        }

        Confidence = confidence / joints.Length;
        TracksMetacarpals = true;
        Feed = HandFeed.RuntimeSkeleton;
        SetTracking(true);
        RefreshHandSpace();
        MeasureStrengths();
    }

    // Controller estimate through the same preset and curl model, against the given root.
    public void ApplySynthesized(in ControllerHandInputs inputs, float3 rootPosition, floatQ rootRotation)
    {
        SetRoot(rootPosition, rootRotation);
        HandPoseSynth.Estimate(in inputs, Side, _curl, _splay);
        HandPoseSynth.Synthesize(Side, _curl, _splay, _scratchPositions, _scratchRotations);
        for (int i = 0; i < HandPoseSynth.SegmentCount; i++)
            SetSegment(i, _scratchPositions[i], _scratchRotations[i]);
        SetPalm(HandPoseSynth.PalmPosition(Side), HandPoseSynth.PalmRotation(Side));
        SetWrist(HandPoseSynth.WristPosition(Side), HandPoseSynth.WristRotation(Side));

        Confidence = SynthesizedConfidence;
        TracksMetacarpals = true;
        Feed = HandFeed.Synthesized;
        SetTracking(true);
        RefreshHandSpace();
        MeasureStrengths();
    }

    // Re-express every segment and the palm in the poser's hand space off the current wrist.
    public void RefreshHandSpace()
    {
        HandFrameRotation = (WristLocalRotation * HalfTurnAboutY).Normalized;
        floatQ inverse = HandFrameRotation.Inverse;
        float3 origin = WristLocalPosition;
        for (int i = 0; i < _segments.Length; i++)
        {
            var segment = _segments[i];
            segment.HandSpacePosition = inverse * (segment.LocalPosition - origin);
            segment.HandSpaceRotation = (inverse * segment.LocalRotation * HalfTurnAboutY).Normalized;
        }
        PalmHandSpacePosition = inverse * (PalmLocalPosition - origin);
        PalmHandSpaceRotation = (inverse * PalmLocalRotation * HalfTurnAboutY).Normalized;
    }

    // Segment frames from segment positions, for a feed that has positions only: -Z toward the next
    // segment (the tip keeps its bone's heading), +Y carried down the chain from the metacarpal's
    // back-of-hand.
    public void ComputeRotationsFromPositions()
    {
        foreach (var finger in this)
        {
            float3 up = finger.Type == FingerType.Thumb ? float3.Up : HandPoseSynth.BackOfHand(Side);
            foreach (var segment in finger)
            {
                var previous = segment.PreviousSegment;
                var next = segment.NextSegment;
                float3 to = next != null
                    ? next.LocalPosition - segment.LocalPosition
                    : segment.LocalPosition - previous!.LocalPosition;
                if (previous != null)
                    up = previous.LocalRotation * float3.Up;
                var frame = HandPoseSynth.FrameFromForwardUp(to, up);
                SetSegment(segment.Index, segment.LocalPosition, frame, segment.Radius);
            }
        }
        RefreshHandSpace();
    }

    private static float JointConfidence(in HandJointSample joint)
    {
        if (!joint.PositionValid || !joint.OrientationValid)
            return 0f;
        return joint.PositionTracked && joint.OrientationTracked ? 1f : 0.5f;
    }

    // Grab: how far each of the four fingers has drawn its tip in toward the palm, as a fraction of the
    // way from the model's open hand to its fist, averaged. Pinch: thumb tip to index tip against the
    // open distance, saturating at pad contact. Both are ratios against the hand's own knuckle span so
    // a large or small real hand measures the same as the model. -xlinka
    private void MeasureStrengths()
    {
        var reference = ReferenceFor(Side);
        float3 palm = PalmLocalPosition;
        float span = KnuckleSpan(_segments);
        float scale = span > 1e-6f ? span / reference.KnuckleSpan : 1f;

        float grab = 0f;
        for (int f = (int)FingerType.Index; f <= (int)FingerType.Pinky; f++)
        {
            var finger = this[(FingerType)f];
            float reach = float3.Distance(finger.Tip.LocalPosition, palm) / scale;
            float open = reference.OpenReach[f];
            float closed = reference.ClosedReach[f];
            float range = open - closed;
            grab += range > 1e-6f ? System.Math.Clamp((open - reach) / range, 0f, 1f) : 0f;
        }
        GrabStrength = grab / 4f;

        float pinchDistance = float3.Distance(Thumb.Tip.LocalPosition, Index.Tip.LocalPosition) / scale;
        float contact = (Thumb.Tip.Radius + Index.Tip.Radius) / scale;
        float pinchRange = reference.OpenPinch - contact;
        PinchStrength = pinchRange > 1e-6f
            ? System.Math.Clamp((reference.OpenPinch - pinchDistance) / pinchRange, 0f, 1f)
            : 0f;
    }

    private static float KnuckleSpan(HandSegment[] segments)
    {
        int index = HandPoseSynth.IndexOf(FingerType.Index, FingerSegmentType.Proximal);
        int pinky = HandPoseSynth.IndexOf(FingerType.Pinky, FingerSegmentType.Proximal);
        return float3.Distance(segments[index].LocalPosition, segments[pinky].LocalPosition);
    }

    private static StrengthReference ReferenceFor(Chirality side)
    {
        var existing = References[(int)side];
        if (existing != null)
            return existing;

        var built = new StrengthReference();
        Span<float3> positions = stackalloc float3[HandPoseSynth.SegmentCount];
        Span<floatQ> rotations = stackalloc floatQ[HandPoseSynth.SegmentCount];
        Span<float> curl = stackalloc float[HandPoseSynth.FingerCount];
        Span<float> splay = stackalloc float[HandPoseSynth.FingerCount];
        float3 palm = HandPoseSynth.PalmPosition(side);

        HandPoseSynth.Synthesize(side, curl, splay, positions, rotations);
        built.KnuckleSpan = float3.Distance(
            positions[HandPoseSynth.IndexOf(FingerType.Index, FingerSegmentType.Proximal)],
            positions[HandPoseSynth.IndexOf(FingerType.Pinky, FingerSegmentType.Proximal)]);
        for (int f = 0; f < HandPoseSynth.FingerCount; f++)
            built.OpenReach[f] = float3.Distance(positions[HandPoseSynth.IndexOf((FingerType)f, FingerSegmentType.Tip)], palm);
        built.OpenPinch = float3.Distance(
            positions[HandPoseSynth.IndexOf(FingerType.Thumb, FingerSegmentType.Tip)],
            positions[HandPoseSynth.IndexOf(FingerType.Index, FingerSegmentType.Tip)]);

        curl.Fill(1f);
        HandPoseSynth.Synthesize(side, curl, splay, positions, rotations);
        for (int f = 0; f < HandPoseSynth.FingerCount; f++)
            built.ClosedReach[f] = float3.Distance(positions[HandPoseSynth.IndexOf((FingerType)f, FingerSegmentType.Tip)], palm);

        References[(int)side] = built;
        return built;
    }
}
