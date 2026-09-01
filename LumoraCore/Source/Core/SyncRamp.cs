// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Components.Utility;
using Lumora.Core.Math;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

// A value that moves at a constant rate: base + rate * (now - anchor), on the session clock.
//
// Same trade as SyncPlayback. A scrolling UV, a fill bar, a conveyor, a drifting light - none of those
// need a number sent every frame, they need the two numbers that describe the line and a clock every
// peer agrees on. Writing the value re-anchors, so an edit costs one state change and the line
// continues from where the writer left it rather than snapping back to whatever the old anchor
// implied.
//
// Rotations are deliberately NOT supported. A quaternion "rate" is not a linear quantity - spinning it
// up is composition, not multiplication by elapsed seconds - and pretending otherwise here would give
// an unnormalized mess a few seconds in. That job belongs to a spinner component. -xlinka
public class SyncRamp<T> : ConflictingSyncElement, ISyncMemberCopy
{
    private T _base;
    private T _rate;
    private double _anchor;
    private bool _running;

    public event Action<SyncRamp<T>>? OnRampChange;

    public SyncRamp()
    {
        // The guard is HERE and not in a static constructor: a static one would run on any touch of
        // the closed type, so IsSupportedType could not be asked about an unsupported T without
        // throwing the answer away. -xlinka
        if (!IsSupportedType)
            throw new Exception($"{typeof(T)} cannot ramp: no additive blend for it");

        _base = SyncCoder.GetDefault<T>();
        _rate = default!;
    }

    // Rotations blend but do not add linearly, so they are out; everything else in the blend set has a
    // component-wise lerp and a component-wise add, which is all a ramp is.
    public static bool IsSupportedType
        => typeof(T) != typeof(floatQ) && ValueOps<T>.CanBlend && ValueOps<T>.CanStep;

    private double NowSeconds => UtilityClock.Seconds(World);

    // The value the line started from, at AnchorSeconds.
    public T BaseValue => _base;

    public double AnchorSeconds => _anchor;

    public double Elapsed => _running ? NowSeconds - _anchor : 0.0;

    // Per second.
    public T Rate
    {
        get => _rate;
        set
        {
            if (SyncCoder.Equals(_rate, value))
                return;
            // Re-anchor at where the line is right now, or the elapsed time already travelled gets
            // replayed at the new rate and the value jumps.
            if (_running)
                SetAll(ValueAt(NowSeconds), value, NowSeconds, true);
            else
                SetAll(_base, value, _anchor, false);
        }
    }

    public bool Running
    {
        get => _running;
        set
        {
            if (_running == value)
                return;
            // Both directions freeze the current value into the base: starting so the line continues
            // from here, stopping so it stays where it stopped.
            SetAll(ValueAt(NowSeconds), _rate, NowSeconds, value);
        }
    }

    // Writing re-anchors.
    public T Value
    {
        get => ValueAt(NowSeconds);
        set => SetAll(value, _rate, NowSeconds, _running);
    }

    // Explicit instant so a caller can look ahead and so the maths is testable without a wall clock.
    public T ValueAt(double sessionSeconds)
    {
        if (!_running)
            return _base;
        float elapsed = (float)(sessionSeconds - _anchor);
        if (!float.IsFinite(elapsed))
            return _base;
        return ValueOps<T>.Step!(_base, Scale(_rate, elapsed));
    }

    // Blending from the type's zero IS scaling: lerp is a + (b - a) * t component-wise, and every
    // supported type's zero is its default. Going through the shared table keeps this member off a
    // second per-type arithmetic list that could drift from the first one. -xlinka
    private static T Scale(T value, float factor) => ValueOps<T>.Blend!(default!, value, factor);

    public void Start()
    {
        if (!_running)
            SetAll(ValueAt(NowSeconds), _rate, NowSeconds, true);
    }

    public void Stop()
    {
        if (_running)
            SetAll(ValueAt(NowSeconds), _rate, NowSeconds, false);
    }

    // Restart the line from a value without touching the rate or the running flag.
    public void Reanchor(T value) => SetAll(value, _rate, NowSeconds, _running);

    private void SetAll(T baseValue, T rate, double anchor, bool running, bool sync = true, bool change = true)
    {
        if (!double.IsFinite(anchor))
            anchor = 0.0;

        if (!BeginModification(throwOnError: false))
            return;

        _base = baseValue;
        _rate = rate;
        _anchor = anchor;
        _running = running;

        if (sync)
            InvalidateSyncElement();

        if (change)
        {
            BlockModification();
            WasChanged = true;
            try
            {
                OnRampChange?.Invoke(this);
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"SyncRamp: ramp change handler threw: {ex}");
            }
            UnblockModification();
        }

        EndModification();
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        writer.Write(_running);
        writer.Write(_anchor);
        SyncCoder.Encode(writer, _base);
        SyncCoder.Encode(writer, _rate);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        bool running = reader.ReadBoolean();
        double anchor = reader.ReadDouble();
        T baseValue = SyncCoder.Decode<T>(reader);
        T rate = SyncCoder.Decode<T>(reader);
        // Session-clock seconds cross raw; the offset was already applied once by the clock itself.
        SetAll(baseValue, rate, anchor, running, sync: false);
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
        => InternalEncodeFull(writer, outboundMessage);

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
        => InternalDecodeFull(reader, inboundMessage);

    protected override void InternalClearDirty()
    {
    }

    // The CURRENT value is saved, not the anchor: an anchor restored from a file is measured against a
    // clock that has moved on, so a running ramp would reload having travelled however long the world
    // sat on disk. See SyncTimeAnchor for the same call.

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Running", _running);
        dictionary.Add("Value", DataTreeCoder.Encode(ValueAt(NowSeconds)));
        dictionary.Add("Rate", DataTreeCoder.Encode(_rate));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;

        bool running = dictionary.ExtractOrDefault("Running", false);
        T baseValue = dictionary.TryGetNode("Value") is { } valueNode
            ? DataTreeCoder.Decode<T>(valueNode)
            : SyncCoder.GetDefault<T>();
        T rate = dictionary.TryGetNode("Rate") is { } rateNode
            ? DataTreeCoder.Decode<T>(rateNode)
            : default!;

        SetAll(baseValue, rate, NowSeconds, running, sync: false, change: false);
    }

    // Four plain values with no sync member of their own; without this a duplicated ramp comes out at
    // the type default and stopped.
    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not SyncRamp<T> other || ReferenceEquals(other, this))
            return;
        SetAll(other._base, other._rate, other._anchor, other._running);
    }

    public override object? GetValueAsObject() => Value;

    public override void Dispose()
    {
        OnRampChange = null;
        base.Dispose();
    }

    public override string ToString() => $"{Value} ({(_running ? "running" : "held")} at {_rate}/s)";
}
