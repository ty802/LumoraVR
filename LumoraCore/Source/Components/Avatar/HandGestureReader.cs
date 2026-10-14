// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Reads the wearer's hand shapes: per hand, which gesture the fingers make and how hard a fist is held.
//
// It reads the same finger stream every machine already receives for posing the hands, so every machine
// comes to the same answer without anything new on the wire. A finger's bend is how far its joints have
// turned toward the palm from the open hand, as a share of a full fist: the same measure a controller's
// estimated hand is built from, so a squeezed grip reads back as the squeeze. A shape counts once it has
// held for HoldTime, which keeps a hand passing through a shape on its way to another from firing it. A
// hand with no finger data (a desktop user, a controller that is not tracked) reads Neutral. -xlinka
[ComponentCategory("Users/Avatar")]
[DefaultUpdateOrder(-300)]
public class HandGestureReader : Component
{
    public readonly Sync<float> HoldTime;

    private static readonly floatQ[][] OpenInverse = { OpenHand(Chirality.Left), OpenHand(Chirality.Right) };

    private readonly AvatarFaceWearer _wearer = new();
    private readonly floatQ[] _rotations = HandPoseHelper.NewRotations();
    private readonly float3[] _positions = HandPoseHelper.NewPositions();
    private readonly float[] _bend = new float[5];
    private readonly Side _left = new();
    private readonly Side _right = new();

    public HandGesture Left => _left.Gesture;
    public HandGesture Right => _right.Gesture;
    public float LeftFist => _left.Fist;
    public float RightFist => _right.Fist;

    public HandGestureReader()
    {
        HoldTime = new Sync<float>(this, 0.08f);
    }

    public HandGesture Of(Chirality side) => side == Chirality.Left ? _left.Gesture : _right.Gesture;

    public float FistOf(Chirality side) => side == Chirality.Left ? _left.Fist : _right.Fist;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (_wearer.Update(Slot))
        {
            _left.Reset();
            _right.Reset();
        }
        var wearer = _wearer.Current;
        var source = wearer?.GetRegisteredComponent<UserHandPoseInfo>()?.HandPoseSource.Target;
        Read(source, Chirality.Left, _left, delta);
        Read(source, Chirality.Right, _right, delta);
    }

    private void Read(IHandPoseSource? source, Chirality side, Side state, float delta)
    {
        var seen = HandGesture.Neutral;
        float fist = 0f;
        if (source != null && source.IsHandTracked(side) && HandPoseHelper.Read(source, side, _rotations, _positions, out _))
        {
            HandPoseHelper.ToHierarchy(null, _rotations);
            Bends(side, _rotations, _bend);
            seen = Classify(_bend[0], _bend[1], _bend[2], _bend[3], _bend[4]);
            if (seen == HandGesture.Fist)
                fist = System.Math.Clamp((_bend[1] - Curled) / (1f - Curled), 0f, 1f);
        }
        state.Advance(seen, fist, delta, MathF.Max(0f, HoldTime.Value));
    }

    // Per finger (thumb, index, middle, ring, pinky), 0 open to 1 fully curled, from rotations that are
    // each relative to the segment before.
    internal static void Bends(Chirality side, floatQ[] hierarchy, float[] into)
    {
        var open = OpenInverse[side == Chirality.Left ? 0 : 1];
        for (int f = 0; f < HandPoseHelper.Fingers.Length; f++)
        {
            var finger = HandPoseHelper.Fingers[f];
            float sum = 0f;
            int joints = 0;
            for (var segment = FingerSegmentType.Proximal; segment <= FingerSegmentType.Distal; segment++)
            {
                if (!finger.IsValidFinger(segment))
                    continue;
                int i = HandPoseHelper.IndexOf(finger, segment);
                sum += Flexion(hierarchy[i] * open[i]);
                joints++;
            }
            into[f] = System.Math.Clamp(sum / (joints * HandPoseSynth.FullCurl(finger)), 0f, 1f);
        }
    }

    // A curl is applied on top of the open joint, so taking the open joint off leaves the curl. In the
    // stream's hand space flexion toward the palm is the positive turn about the knuckle axis (+X).
    private static float Flexion(floatQ curl)
    {
        if (curl.w < 0f)
            curl = new floatQ(-curl.x, -curl.y, -curl.z, -curl.w);
        return 2f * MathF.Atan2(curl.x, curl.w);
    }

    // The device's open hand as the stream carries it: each segment frame given the same half turn about
    // Y the device applies on the way out, which points it down the fingers.
    private static floatQ[] OpenHand(Chirality side)
    {
        var positions = new float3[HandPoseHelper.NodesPerHand];
        var rotations = new floatQ[HandPoseHelper.NodesPerHand];
        HandPoseSynth.OpenPose(side, positions, rotations);
        var halfTurn = floatQ.AxisAngle(float3.Up, MathF.PI);
        for (int i = 0; i < rotations.Length; i++)
            rotations[i] = rotations[i] * halfTurn;
        HandPoseHelper.ToHierarchy(null, rotations);
        for (int i = 0; i < rotations.Length; i++)
            rotations[i] = rotations[i].Inverse;
        return rotations;
    }

    private const float Straight = 0.12f;
    private const float Extended = 0.35f;
    private const float Curled = 0.6f;
    private const float ThumbIn = 0.35f;

    internal static HandGesture Classify(float thumb, float index, float middle, float ring, float pinky)
    {
        bool iOut = index < Extended, mOut = middle < Extended, rOut = ring < Extended, pOut = pinky < Extended;
        bool iIn = index > Curled, mIn = middle > Curled, rIn = ring > Curled, pIn = pinky > Curled;
        bool thumbOut = thumb < ThumbIn;

        if (iIn && mIn && rIn && pIn)
            return thumbOut ? HandGesture.ThumbsUp : HandGesture.Fist;
        if (iOut && mIn && rIn && pIn)
            return thumbOut ? HandGesture.Gun : HandGesture.Point;
        if (iOut && mOut && rIn && pIn)
            return HandGesture.Victory;
        if (iOut && pOut && mIn && rIn)
            return HandGesture.RockNRoll;
        // Open is a flat hand, not a resting one: a relaxed hand bends a little and should read as nothing.
        if (index < Straight && middle < Straight && ring < Straight && pinky < Straight && thumbOut)
            return HandGesture.Open;
        return HandGesture.Neutral;
    }

    private sealed class Side
    {
        public HandGesture Gesture;
        public float Fist;
        private HandGesture _candidate;
        private float _held;

        public void Advance(HandGesture seen, float fist, float delta, float hold)
        {
            if (seen != _candidate)
            {
                _candidate = seen;
                _held = 0f;
            }
            else
            {
                _held += delta;
            }
            if (_candidate != Gesture && _held >= hold)
                Gesture = _candidate;
            Fist = Gesture == HandGesture.Fist ? fist : 0f;
        }

        public void Reset()
        {
            Gesture = HandGesture.Neutral;
            Fist = 0f;
            _candidate = HandGesture.Neutral;
            _held = 0f;
        }
    }
}
