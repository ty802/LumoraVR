// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Networking.Streams;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// The user's finger pose source, backed by network streams. The owning peer reads
/// local tracking hardware and writes per-finger-node positions into a packed array
/// stream (plus a per-side tracking flag); remote peers read those streams.
/// Published per-user via <see cref="UserHandPoseInfo"/>.
/// </summary>
// Local user: serves live input directly (zero latency) AND writes the streams for
// others. Remote users: serves the replicated, interpolated streams. When nothing
// is tracking it reports not-tracking, so the poser rests the hand - desktop and
// untracked remotes never get synthesized curl. - xlinka
[ComponentCategory("Users/Avatar/Hands")]
public class HandPoseStreamManager : UserRootComponent, IHandPoseSourceComponent
{
    private const string PositionStreamName = "Finger.Positions";
    private const string LeftTrackingName = "Finger.LeftTracking";
    private const string RightTrackingName = "Finger.RightTracking";
    private const uint StreamPeriod = 2; // every other sync tick (~35Hz at 70Hz sync)

    // 24 finger nodes per hand (LeftThumb_Metacarpal .. LeftPinky_Tip).
    private const int NodesPerHand = 24;
    private static readonly float3 RangeMin = new float3(-0.3f, -0.3f, -0.3f);
    private static readonly float3 RangeMax = new float3(0.3f, 0.3f, 0.3f);

    private Float3ArrayValueStream _positions = null!;
    private BoolValueStream _leftTracking = null!;
    private BoolValueStream _rightTracking = null!;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // Only the owner produces the streams; remote copies are read-only.
        if (!IsUnderLocalUser)
            return;

        EnsureStreams();
        if (_positions == null || !_positions.IsLocal)
            return;

        var input = Engine.Current?.InputInterface;
        if (input == null)
            return;

        bool left = WriteSide(input, Chirality.Left);
        bool right = WriteSide(input, Chirality.Right);
        if (_leftTracking != null) _leftTracking.Value = left;
        if (_rightTracking != null) _rightTracking.Value = right;
    }

    // Tracking hardware reports proximal-and-outward; the metacarpal devices are
    // not tracked, so TryLivePosition returns false for them and those array slots
    // stay at rest. Report that the metacarpals aren't carried so the consumer
    // leaves the metacarpal bones in their authored pose instead of driving them
    // from absent data.
    public bool TracksMetacarpals => false;

    public bool IsHandTracked(Chirality side)
    {
        if (IsUnderLocalUser)
        {
            var input = Engine.Current?.InputInterface;
            return input != null && LiveTracking(input, side);
        }

        EnsureStreams();
        var flag = side == Chirality.Left ? _leftTracking : _rightTracking;
        return flag != null && flag.HasValidData && flag.Value;
    }

    public bool TryGetFingerPosition(BodyNode node, out float3 wristLocalPosition)
    {
        wristLocalPosition = float3.Zero;

        if (IsUnderLocalUser)
        {
            var input = Engine.Current?.InputInterface;
            return input != null && TryLivePosition(input, node, out wristLocalPosition);
        }

        EnsureStreams();
        if (_positions == null || !IsHandTracked(node.GetChirality()))
            return false;

        int index = NodeToIndex(node);
        if (index < 0 || index >= _positions.Count)
            return false;

        wristLocalPosition = _positions[index];
        return true;
    }

    private void EnsureStreams()
    {
        if (_positions != null)
            return;

        var user = Slot?.ActiveUserRoot?.ActiveUser;
        if (user == null)
            return;

        if (IsUnderLocalUser)
        {
            _positions = user.GetStreamOrAdd<Float3ArrayValueStream>(PositionStreamName, s =>
            {
                s.Count = NodesPerHand * 2;
                s.Encoding = ValueEncoding.Quantized;
                s.FullFrameBits = 12;
                s.FullFrameMin = RangeMin;
                s.FullFrameMax = RangeMax;
                s.SetInterpolation();
                s.SetUpdatePeriod(StreamPeriod, 0);
            });
            _leftTracking = user.GetStreamOrAdd<BoolValueStream>(LeftTrackingName, s => s.SetUpdatePeriod(StreamPeriod, 0));
            _rightTracking = user.GetStreamOrAdd<BoolValueStream>(RightTrackingName, s => s.SetUpdatePeriod(StreamPeriod, 0));
        }
        else
        {
            _positions = user.GetStream<Float3ArrayValueStream>(s => s.Name == PositionStreamName)!;
            _leftTracking = user.GetStream<BoolValueStream>(s => s.Name == LeftTrackingName)!;
            _rightTracking = user.GetStream<BoolValueStream>(s => s.Name == RightTrackingName)!;
        }
    }

    private bool WriteSide(InputInterface input, Chirality side)
    {
        if (!LiveTracking(input, side))
            return false;

        int start = side == Chirality.Left
            ? (int)BodyNode.LeftThumb_Metacarpal
            : (int)BodyNode.RightThumb_Metacarpal;
        int baseIndex = side == Chirality.Left ? 0 : NodesPerHand;

        for (int i = 0; i < NodesPerHand; i++)
        {
            if (TryLivePosition(input, (BodyNode)(start + i), out var pos))
                _positions[baseIndex + i] = pos;
        }
        return true;
    }

    private static bool LiveTracking(InputInterface input, Chirality side)
    {
        var wrist = input.GetBodyNode(WristNode(side));
        if (wrist == null || !wrist.IsTracking)
            return false;

        var probe = input.GetBodyNode(FingerType.Index.ComposeFinger(FingerSegmentType.Proximal, side));
        return probe != null && probe.IsTracking;
    }

    private static bool TryLivePosition(InputInterface input, BodyNode node, out float3 wristLocalPosition)
    {
        wristLocalPosition = float3.Zero;

        var side = node.GetChirality();
        var wrist = input.GetBodyNode(WristNode(side));
        if (wrist == null || !wrist.IsTracking)
            return false;

        var device = input.GetBodyNode(node);
        if (device == null || !device.IsTracking)
            return false;

        wristLocalPosition = wrist.RawRotation.Inverse * (device.RawPosition - wrist.RawPosition);
        return true;
    }

    private static int NodeToIndex(BodyNode node)
    {
        int n = (int)node;
        if (n >= (int)BodyNode.LeftThumb_Metacarpal && n <= (int)BodyNode.LeftPinky_Tip)
            return n - (int)BodyNode.LeftThumb_Metacarpal;
        if (n >= (int)BodyNode.RightThumb_Metacarpal && n <= (int)BodyNode.RightPinky_Tip)
            return NodesPerHand + (n - (int)BodyNode.RightThumb_Metacarpal);
        return -1;
    }

    private static BodyNode WristNode(Chirality side)
        => side == Chirality.Left ? BodyNode.LeftController : BodyNode.RightController;
}
