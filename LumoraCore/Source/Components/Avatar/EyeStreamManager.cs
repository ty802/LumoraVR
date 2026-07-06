// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Networking.Streams;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Publishes the user's eye tracking over the network. The owning peer reads its head device's eye
/// data (gaze directions, per-eye openness, pupil dilation) and writes it into streams; remote peers
/// read the replicated, interpolated values. Drives nothing itself - <see cref="EyeGazeDriver"/>
/// and <see cref="BlinkDriver"/> consume it. Inert until a head device actually reports eye tracking.
/// </summary>
[ComponentCategory("Users/Avatar/Face")]
public class EyeStreamManager : UserRootComponent
{
    private const string GazeStreamName = "Eye.Gaze";       // [0]=left dir, [1]=right dir, [2]=combined dir
    private const string StateStreamName = "Eye.State";     // x=left openness, y=right openness, z=pupil
    private const string ExprStreamName = "Eye.Expr";       // [0]=left (widen,squeeze,frown), [1]=right
    private const string TrackingName = "Eye.Tracking";
    private const uint StreamPeriod = 2; // every other sync tick

    private Float3ArrayValueStream _gaze = null!;
    private Float3ValueStream _state = null!;
    private Float3ArrayValueStream _expr = null!;
    private BoolValueStream _tracking = null!;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!IsUnderLocalUser)
            return;

        EnsureStreams();
        if (_gaze == null || !_gaze.IsLocal)
            return;

        var head = Engine.Current?.InputInterface?.HeadDevice;
        bool tracking = head != null && head.HasEyeTracking && head.IsWorn;
        if (_tracking != null)
            _tracking.Value = tracking;
        if (!tracking)
            return;

        _gaze[0] = ToFloat3(head!.LeftEyeGazeDirection);
        _gaze[1] = ToFloat3(head.RightEyeGazeDirection);
        _gaze[2] = ToFloat3(head.CombinedEyeGazeDirection);
        if (_state != null)
            _state.Value = new float3(head.LeftEyeOpenness, head.RightEyeOpenness, head.PupilDilation);
        if (_expr != null)
        {
            _expr[0] = new float3(head.LeftEyeWiden, head.LeftEyeSqueeze, head.LeftEyeFrown);
            _expr[1] = new float3(head.RightEyeWiden, head.RightEyeSqueeze, head.RightEyeFrown);
        }
    }

    public bool IsTracking
    {
        get
        {
            if (IsUnderLocalUser)
            {
                var head = Engine.Current?.InputInterface?.HeadDevice;
                return head != null && head.HasEyeTracking && head.IsWorn;
            }
            EnsureStreams();
            return _tracking != null && _tracking.HasValidData && _tracking.Value;
        }
    }

    /// <summary>Combined (binocular) gaze direction. Returns false if eye tracking isn't active.</summary>
    public bool TryGetCombinedGaze(out float3 direction)
    {
        direction = float3.Backward;
        if (!IsTracking)
            return false;

        if (IsUnderLocalUser)
        {
            var head = Engine.Current?.InputInterface?.HeadDevice;
            if (head == null)
                return false;
            direction = ToFloat3(head.CombinedEyeGazeDirection);
            return true;
        }

        EnsureStreams();
        if (_gaze == null || _gaze.Count < 3)
            return false;
        direction = _gaze[2];
        return true;
    }

    /// <summary>Average eye openness 0..1 (1 = fully open). Defaults to open when not tracking.</summary>
    public float Openness
    {
        get
        {
            if (IsUnderLocalUser)
            {
                var head = Engine.Current?.InputInterface?.HeadDevice;
                if (head == null)
                    return 1f;
                return (head.LeftEyeOpenness + head.RightEyeOpenness) * 0.5f;
            }
            EnsureStreams();
            if (_state == null || !_state.HasValidData)
                return 1f;
            var s = _state.Value;
            return (s.x + s.y) * 0.5f;
        }
    }

    /// <summary>Pupil dilation 0..1 (0 = constricted, 1 = dilated). 0 when no eye tracking is feeding it.</summary>
    public float Pupil
    {
        get
        {
            if (IsUnderLocalUser)
                return Engine.Current?.InputInterface?.HeadDevice?.PupilDilation ?? 0f;
            EnsureStreams();
            return _state != null && _state.HasValidData ? _state.Value.z : 0f;
        }
    }

    public float GetWiden(Chirality side) => GetExpr(side, 0);
    public float GetSqueeze(Chirality side) => GetExpr(side, 1);
    public float GetFrown(Chirality side) => GetExpr(side, 2);

    private float GetExpr(Chirality side, int component)
    {
        if (IsUnderLocalUser)
        {
            var head = Engine.Current?.InputInterface?.HeadDevice;
            if (head == null)
                return 0f;
            return side == Chirality.Left
                ? (component == 0 ? head.LeftEyeWiden : component == 1 ? head.LeftEyeSqueeze : head.LeftEyeFrown)
                : (component == 0 ? head.RightEyeWiden : component == 1 ? head.RightEyeSqueeze : head.RightEyeFrown);
        }

        EnsureStreams();
        if (_expr == null || _expr.Count < 2)
            return 0f;
        var v = _expr[side == Chirality.Left ? 0 : 1];
        return component == 0 ? v.x : component == 1 ? v.y : v.z;
    }

    private void EnsureStreams()
    {
        if (_gaze != null)
            return;

        var user = Slot?.ActiveUserRoot?.ActiveUser;
        if (user == null)
            return;

        if (IsUnderLocalUser)
        {
            _gaze = user.GetStreamOrAdd<Float3ArrayValueStream>(GazeStreamName, s =>
            {
                s.Count = 3;
                s.Encoding = ValueEncoding.Quantized;
                s.FullFrameBits = 10;
                s.FullFrameMin = new float3(-1f, -1f, -1f);
                s.FullFrameMax = new float3(1f, 1f, 1f);
                s.SetInterpolation();
                s.SetUpdatePeriod(StreamPeriod, 0);
            });
            _state = user.GetStreamOrAdd<Float3ValueStream>(StateStreamName, s =>
            {
                s.SetInterpolation();
                s.SetUpdatePeriod(StreamPeriod, 0);
            });
            _expr = user.GetStreamOrAdd<Float3ArrayValueStream>(ExprStreamName, s =>
            {
                s.Count = 2;
                s.Encoding = ValueEncoding.Quantized;
                s.FullFrameBits = 8;
                s.FullFrameMin = float3.Zero;
                s.FullFrameMax = new float3(1f, 1f, 1f);
                s.SetInterpolation();
                s.SetUpdatePeriod(StreamPeriod, 0);
            });
            _tracking = user.GetStreamOrAdd<BoolValueStream>(TrackingName, s => s.SetUpdatePeriod(StreamPeriod, 0));
        }
        else
        {
            _gaze = user.GetStream<Float3ArrayValueStream>(s => s.Name == GazeStreamName)!;
            _state = user.GetStream<Float3ValueStream>(s => s.Name == StateStreamName)!;
            _expr = user.GetStream<Float3ArrayValueStream>(s => s.Name == ExprStreamName)!;
            _tracking = user.GetStream<BoolValueStream>(s => s.Name == TrackingName)!;
        }
    }

    private static float3 ToFloat3(System.Numerics.Vector3 v) => new float3(v.X, v.Y, v.Z);
}
