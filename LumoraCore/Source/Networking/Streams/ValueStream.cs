// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Value encoding mode for streams.
/// </summary>
public enum ValueEncoding
{
    /// <summary>
    /// Full precision encoding.
    /// </summary>
    Full,

    /// <summary>
    /// Quantized encoding with bit-packing.
    /// </summary>
    Quantized,

    /// <summary>
    /// Quantized full keyframes with bit-packed deltas in between - lower bandwidth for slowly-changing values.
    /// </summary>
    Delta
}

/// <summary>
/// Generic value stream with optional interpolation support.
/// Used for streaming transforms, tracking data, and other continuous values.
/// </summary>
/// <typeparam name="T">The type of value to stream.</typeparam>
public class ValueStream<T> : ImplicitStream, IValue<T>
{
    private struct DataPoint
    {
        public T Value;
        public DateTime Time;

        public DataPoint(T value, DateTime time)
        {
            Value = value;
            Time = time;
        }
    }

    protected T _value = default!;

    // Sync members for configuration
    protected readonly Sync<bool> _isInterpolated = new();
    protected readonly Sync<float> _interpolationOffset = new();
    protected readonly Sync<ValueEncoding> _encoding = new();
    protected readonly Sync<int> _fullFrameBits = new();
    protected readonly Sync<T> _fullFrameMin = new();
    protected readonly Sync<T> _fullFrameMax = new();
    protected readonly Sync<int> _deltaFrameBits = new();
    protected readonly Sync<T> _deltaFrameMin = new();
    protected readonly Sync<T> _deltaFrameMax = new();

    // Delta-frame state: a periodic full keyframe then bit-packed deltas; bounds drift if a frame is lost.
    private const int KeyframeInterval = 30;
    private T _lastEncodedValue = default!;
    private bool _hasLastEncoded;
    private int _keyframeCounter;
    private T _lastDecodedValue = default!;
    private bool _hasLastDecoded;

    // Interpolation state
    private List<DataPoint> _dataPoints = new();
    private float _timeTransition;
    private DateTime _trailingTime;
    private DateTime _leadingTime;

    private bool _receivedFirstData;

    /// <summary>
    /// Value encoding mode.
    /// </summary>
    public ValueEncoding Encoding
    {
        get => _encoding.Value;
        set
        {
            CheckOwnership();
            _encoding.Value = value;
        }
    }

    /// <summary>Bit depth per component when <see cref="Encoding"/> is Quantized.</summary>
    public int FullFrameBits
    {
        get => _fullFrameBits.Value;
        set { CheckOwnership(); _fullFrameBits.Value = value; }
    }

    /// <summary>Lower bound of the quantization range (per component) for Quantized encoding.</summary>
    public T FullFrameMin
    {
        get => _fullFrameMin.Value;
        set { CheckOwnership(); _fullFrameMin.Value = value; }
    }

    /// <summary>Upper bound of the quantization range (per component) for Quantized encoding.</summary>
    public T FullFrameMax
    {
        get => _fullFrameMax.Value;
        set { CheckOwnership(); _fullFrameMax.Value = value; }
    }

    /// <summary>Bit depth per component for the delta frames in Delta encoding.</summary>
    public int DeltaFrameBits
    {
        get => _deltaFrameBits.Value;
        set { CheckOwnership(); _deltaFrameBits.Value = value; }
    }

    /// <summary>Lower bound of the per-component delta range (Delta encoding).</summary>
    public T DeltaFrameMin
    {
        get => _deltaFrameMin.Value;
        set { CheckOwnership(); _deltaFrameMin.Value = value; }
    }

    /// <summary>Upper bound of the per-component delta range (Delta encoding).</summary>
    public T DeltaFrameMax
    {
        get => _deltaFrameMax.Value;
        set { CheckOwnership(); _deltaFrameMax.Value = value; }
    }

    /// <summary>
    /// Whether interpolation is enabled for smooth playback.
    /// </summary>
    public bool IsInterpolated
    {
        get => _isInterpolated.Value;
        set
        {
            CheckOwnership();
            _isInterpolated.Value = value;
        }
    }

    /// <summary>
    /// Time offset for interpolation in seconds.
    /// </summary>
    public float InterpolationOffset
    {
        get => _interpolationOffset.Value;
        set
        {
            CheckOwnership();
            _interpolationOffset.Value = value;
        }
    }

    private DateTime CurrentTime => Lerp(_trailingTime, _leadingTime, _timeTransition);

    /// <summary>
    /// Whether this stream has valid data to read.
    /// </summary>
    public override bool HasValidData
    {
        get
        {
            if (!_receivedFirstData)
                return IsLocal;
            return true;
        }
    }

    /// <summary>
    /// Current value of the stream.
    /// </summary>
    public T Value
    {
        get => _value;
        set
        {
            CheckOwnership();
            _value = value;
        }
    }

    /// <summary>
    /// Event triggered when the value changes.
    /// </summary>
    public event Action<IChangeable> Changed = null!;

    protected override void OnInit()
    {
        base.OnInit();

        // Members wire via the worker pipeline now; OnInit just seeds config defaults (in the init phase, no deltas). -xlinka
        _interpolationOffset.Value = 0.05f;
        _encoding.Value = ValueEncoding.Full;
        _fullFrameBits.Value = 16;
        _deltaFrameBits.Value = 8;
    }

    /// <summary>
    /// Enable interpolation with default settings.
    /// </summary>
    public void SetInterpolation()
    {
        CheckOwnership();
        _isInterpolated.Value = true;
        _interpolationOffset.Value = 0.05f;
    }

    /// <summary>
    /// Called every frame to update interpolation.
    /// </summary>
    public override void Update()
    {
        if (IsInterpolated && !IsLocal)
        {
            if (_dataPoints.Count == 0)
                return;

            if (_dataPoints.Count == 1)
            {
                _value = _dataPoints[0].Value;
                return;
            }

            // Advance time
            var delta = World?.LastDelta ?? 0.016f;
            _trailingTime = _trailingTime.AddSeconds(delta);
            _leadingTime = _leadingTime.AddSeconds(delta);

            var latestTime = _dataPoints[_dataPoints.Count - 1].Time;
            if (_trailingTime > latestTime) _trailingTime = latestTime;
            if (_leadingTime > latestTime) _leadingTime = latestTime;

            _timeTransition = Progress01(_timeTransition, delta / InterpolationOffset);

            var currentTime = CurrentTime;
            int lastIndex = _dataPoints.FindLastIndex(p => currentTime >= p.Time);

            if (lastIndex > 0)
            {
                _dataPoints.RemoveRange(0, System.Math.Min(lastIndex, _dataPoints.Count - 2));
            }

            if (_dataPoints.Count >= 2)
            {
                float lerp = InverseLerp(_dataPoints[0].Time, _dataPoints[1].Time, currentTime);
                _value = Interpolate(_dataPoints[0].Value, _dataPoints[1].Value, lerp);
            }
        }

        Changed?.Invoke(this);
    }

    /// <summary>
    /// Decode stream data from the reader.
    /// </summary>
    public override void Decode(BinaryReader reader, StreamMessage message)
    {
        var enc = _encoding.Value;
        bool quantizable = StreamCoder.SupportsQuantization(typeof(T));
        T value;

        if (enc == ValueEncoding.Quantized && quantizable)
        {
            var br = new BitReader(reader.ReadBytes(StreamCoder.QuantizedByteCount<T>(_fullFrameBits.Value)));
            value = StreamCoder.DecodeQuantized(br, _fullFrameMin.Value, _fullFrameMax.Value, _fullFrameBits.Value);
        }
        else if (enc == ValueEncoding.Delta && quantizable)
        {
            if (!TryDecodeDeltaFrame(reader, out value))
                return; // a delta arrived before any keyframe - skip it, keep the current value
        }
        else
        {
            value = SyncCoder.Decode<T>(reader);
        }

        WriteDataPoint(value, DateTime.FromBinary((long)(message.StreamTime * TimeSpan.TicksPerSecond)));
        _receivedFirstData = true;
    }

    /// <summary>
    /// Encode stream data to the writer.
    /// </summary>
    public override void Encode(BinaryWriter writer)
    {
        var enc = _encoding.Value;
        bool quantizable = StreamCoder.SupportsQuantization(typeof(T));

        // Quantized when requested AND the type has a quantized coder; otherwise full precision. Both sides
        // know FullFrameBits (synced), so the quantized byte count is implied - no length prefix. -xlinka
        if (enc == ValueEncoding.Quantized && quantizable)
        {
            var bw = new BitWriter();
            StreamCoder.EncodeQuantized(bw, _value, _fullFrameMin.Value, _fullFrameMax.Value, _fullFrameBits.Value);
            writer.Write(bw.ToArray());
        }
        else if (enc == ValueEncoding.Delta && quantizable)
        {
            EncodeDeltaFrame(writer);
        }
        else
        {
            SyncCoder.Encode(writer, _value);
        }
    }

    // Delta frame: a 1-bit full/delta flag then the bit-packed value (full = quantized absolute, delta =
    // quantized change from the last value). Length-prefixed because full and delta frames differ in size.
    // A full keyframe goes out every KeyframeInterval frames (and on the first), so a lost delta's drift is
    // bounded and recovers at the next keyframe. -xlinka
    private void EncodeDeltaFrame(BinaryWriter writer)
    {
        bool full = !_hasLastEncoded || (_keyframeCounter % KeyframeInterval) == 0;

        var bw = new BitWriter();
        bw.WriteBits(full ? 1u : 0u, 1);
        if (full)
            StreamCoder.EncodeQuantized(bw, _value, _fullFrameMin.Value, _fullFrameMax.Value, _fullFrameBits.Value);
        else
            StreamCoder.EncodeDelta(bw, _value, _lastEncodedValue, _deltaFrameMin.Value, _deltaFrameMax.Value, _deltaFrameBits.Value);

        var bytes = bw.ToArray();
        writer.Write7BitEncodedInt(bytes.Length);
        writer.Write(bytes);

        _lastEncodedValue = _value;
        _hasLastEncoded = true;
        _keyframeCounter++;
    }

    private bool TryDecodeDeltaFrame(BinaryReader reader, out T value)
    {
        value = default!;

        int length = reader.Read7BitEncodedInt();
        if (length <= 0)
            return false;

        var br = new BitReader(reader.ReadBytes(length));
        bool full = br.ReadBits(1) == 1u;

        if (full)
        {
            value = StreamCoder.DecodeQuantized(br, _fullFrameMin.Value, _fullFrameMax.Value, _fullFrameBits.Value);
        }
        else
        {
            if (!_hasLastDecoded)
                return false; // can't apply a delta without a base value yet

            value = StreamCoder.DecodeDelta(br, _lastDecodedValue, _deltaFrameMin.Value, _deltaFrameMax.Value, _deltaFrameBits.Value);
        }

        _lastDecodedValue = value;
        _hasLastDecoded = true;
        return true;
    }

    /// <summary>
    /// Write a data point for interpolation.
    /// </summary>
    protected void WriteDataPoint(T value, DateTime time)
    {
        if (IsInterpolated)
        {
            if (_dataPoints.Count > 0 && time < _dataPoints[_dataPoints.Count - 1].Time)
                return; // Out of order, skip

            _dataPoints.Add(new DataPoint(value, time));

            if (_dataPoints.Count > 1)
            {
                _trailingTime = CurrentTime;
                _timeTransition = 0f;
                _leadingTime = time.AddSeconds(-InterpolationOffset);
                if (_leadingTime < _trailingTime) _leadingTime = _trailingTime;
            }
            else
            {
                _trailingTime = _leadingTime = time;
                _timeTransition = 1f;
            }
        }
        else
        {
            _value = value;
        }
    }

    /// <summary>
    /// Interpolate between two values.
    /// Override for custom interpolation behavior.
    /// </summary>
    protected virtual T Interpolate(T a, T b, float lerp)
    {
        // Default: no interpolation, just return target
        return lerp < 0.5f ? a : b;
    }

    // Helper methods
    private static DateTime Lerp(DateTime a, DateTime b, float t)
    {
        long ticksA = a.Ticks;
        long ticksB = b.Ticks;
        long result = ticksA + (long)((ticksB - ticksA) * t);
        return new DateTime(result);
    }

    private static float InverseLerp(DateTime a, DateTime b, DateTime value)
    {
        long range = b.Ticks - a.Ticks;
        if (range == 0) return 0f;
        return (float)(value.Ticks - a.Ticks) / range;
    }

    private static float Progress01(float current, float delta)
    {
        return System.Math.Min(1f, current + delta);
    }

    public override void Dispose()
    {
        _dataPoints.Clear();
        Changed = null!;
        base.Dispose();
    }
}

/// <summary>
/// Float3 value stream with linear interpolation.
/// </summary>
public class Float3ValueStream : ValueStream<Lumora.Core.Math.float3>
{
    /// <summary>
    /// Override to check for valid float3 (no NaN or Infinity values).
    /// </summary>
    public override bool HasValidData
    {
        get
        {
            if (!base.HasValidData)
                return false;

            // Check for NaN or Infinity
            var v = _value;
            return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }
    }

    protected override Lumora.Core.Math.float3 Interpolate(
        Lumora.Core.Math.float3 a,
        Lumora.Core.Math.float3 b,
        float lerp)
    {
        return new Lumora.Core.Math.float3(
            a.x + (b.x - a.x) * lerp,
            a.y + (b.y - a.y) * lerp,
            a.z + (b.z - a.z) * lerp
        );
    }

}

/// <summary>
/// Quaternion value stream with spherical interpolation.
/// </summary>
public class FloatQValueStream : ValueStream<Lumora.Core.Math.floatQ>
{
    /// <summary>
    /// Override to check for valid quaternion (non-zero length).
    /// A zero quaternion is invalid and would cause NaN when normalized.
    /// </summary>
    public override bool HasValidData
    {
        get
        {
            if (!base.HasValidData)
                return false;

            // Check if the quaternion has valid (non-zero) length
            var q = _value;
            float lengthSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return lengthSq > 0.0001f;
        }
    }

    protected override Lumora.Core.Math.floatQ Interpolate(
        Lumora.Core.Math.floatQ a,
        Lumora.Core.Math.floatQ b,
        float lerp)
    {
        // Proper spherical interpolation - constant angular velocity, no normalize-induced speed-up. -xlinka
        return Lumora.Core.Math.floatQ.Slerp(a, b, lerp);
    }
}
