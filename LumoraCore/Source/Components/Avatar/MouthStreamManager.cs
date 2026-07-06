// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Networking.Streams;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Publishes the user's mouth/lip expression weights over the network. The owning peer reads its mouth
/// device and packs the per-shape weights into a stream; remote peers read them. <c>MouthExpressionDriver</c>
/// consumes this to drive avatar blendshapes. Inert until a face tracker feeds the mouth device.
/// </summary>
[ComponentCategory("Users/Avatar/Face")]
public class MouthStreamManager : UserRootComponent
{
    private const string ShapesStreamName = "Mouth.Shapes";
    private const string TrackingName = "Mouth.Tracking";
    private const uint StreamPeriod = 2;

    // Weights are packed 3-per-entry into a float3 array stream (no float-array stream type exists).
    private static readonly int ShapeCount = MouthDevice.ShapeCount;
    private static readonly int EntryCount = (ShapeCount + 2) / 3;

    private Float3ArrayValueStream _shapes = null!;
    private BoolValueStream _tracking = null!;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!IsUnderLocalUser)
            return;

        EnsureStreams();
        if (_shapes == null || !_shapes.IsLocal)
            return;

        var mouth = Engine.Current?.InputInterface?.MouthDevice;
        bool tracking = mouth != null && mouth.IsTracking && mouth.IsWorn;
        if (_tracking != null)
            _tracking.Value = tracking;
        if (!tracking)
            return;

        for (int e = 0; e < EntryCount; e++)
        {
            int b = e * 3;
            _shapes[e] = new float3(
                Weight(mouth!, b),
                Weight(mouth!, b + 1),
                Weight(mouth!, b + 2));
        }
    }

    public bool IsTracking
    {
        get
        {
            if (IsUnderLocalUser)
            {
                var mouth = Engine.Current?.InputInterface?.MouthDevice;
                return mouth != null && mouth.IsTracking && mouth.IsWorn;
            }
            EnsureStreams();
            return _tracking != null && _tracking.HasValidData && _tracking.Value;
        }
    }

    public float GetWeight(MouthShape shape)
    {
        int i = (int)shape;
        if ((uint)i >= (uint)ShapeCount)
            return 0f;

        if (IsUnderLocalUser)
            return Engine.Current?.InputInterface?.MouthDevice?.GetWeight(shape) ?? 0f;

        EnsureStreams();
        if (_shapes == null || !IsTracking)
            return 0f;

        int entry = i / 3;
        if (entry >= _shapes.Count)
            return 0f;
        var v = _shapes[entry];
        return (i % 3) switch { 0 => v.x, 1 => v.y, _ => v.z };
    }

    private void EnsureStreams()
    {
        if (_shapes != null)
            return;

        var user = Slot?.ActiveUserRoot?.ActiveUser;
        if (user == null)
            return;

        if (IsUnderLocalUser)
        {
            _shapes = user.GetStreamOrAdd<Float3ArrayValueStream>(ShapesStreamName, s =>
            {
                s.Count = EntryCount;
                s.Encoding = ValueEncoding.Quantized;
                s.FullFrameBits = 10;
                s.FullFrameMin = float3.Zero;
                s.FullFrameMax = new float3(1f, 1f, 1f);
                s.SetInterpolation();
                s.SetUpdatePeriod(StreamPeriod, 0);
            });
            _tracking = user.GetStreamOrAdd<BoolValueStream>(TrackingName, s => s.SetUpdatePeriod(StreamPeriod, 0));
        }
        else
        {
            _shapes = user.GetStream<Float3ArrayValueStream>(s => s.Name == ShapesStreamName)!;
            _tracking = user.GetStream<BoolValueStream>(s => s.Name == TrackingName)!;
        }
    }

    private static float Weight(MouthDevice mouth, int index)
        => (uint)index < (uint)ShapeCount ? mouth.GetWeight((MouthShape)index) : 0f;
}
