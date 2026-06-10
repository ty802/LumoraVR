// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components;

[ComponentCategory("Utility")]
public class ValueGraphRecorder : Component
{
    public readonly SyncRef<IField<float>> Source;
    public readonly Sync<float> UpdateInterval;
    public readonly Sync<int> Points;
    public readonly Sync<float> MinRangeAdjustThreshold;
    public readonly Sync<float> MinRangeAdjustMultiplier;
    public readonly Sync<float> MaxRangeAdjustThreshold;
    public readonly Sync<float> MaxRangeAdjustMultiplier;
    public readonly Sync<float> RangeMin;
    public readonly Sync<float> RangeMax;

    private float[]? _buffer;
    private int _startIndex;
    private int _count;
    private double _lastUpdate;
    private int _version;

    public int Capacity => _buffer?.Length ?? 0;
    public int Count => _count;
    public int StartIndex => _startIndex;
    public int Version => _version;

    public ValueGraphRecorder()
    {
        Source = new SyncRef<IField<float>>(this);
        UpdateInterval = new Sync<float>(this, 0.05f);
        Points = new Sync<int>(this, 128);
        MinRangeAdjustThreshold = new Sync<float>(this, 1f);
        MinRangeAdjustMultiplier = new Sync<float>(this, 1f);
        MaxRangeAdjustThreshold = new Sync<float>(this, 1f);
        MaxRangeAdjustMultiplier = new Sync<float>(this, 1f);
        RangeMin = new Sync<float>(this, 0f);
        RangeMax = new Sync<float>(this, 60f);
    }

    public float GetSample(int index)
    {
        if (_buffer == null || _count == 0)
            return 0f;
        return _buffer[(_startIndex + index) % _buffer.Length];
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var source = Source.Target;
        if (source == null || Points.Value <= 0)
            return;

        EnsureBuffer();

        double now = Engine.Current?.Metrics?.TotalTime ?? 0;
        if (now - _lastUpdate < UpdateInterval.Value)
            return;
        _lastUpdate = now;

        float value = source.Value;
        Push(value);
        AdjustRange(value);
        _version++;
    }

    private void EnsureBuffer()
    {
        int p = Points.Value < 2 ? 2 : Points.Value;
        if (_buffer == null || _buffer.Length != p)
        {
            _buffer = new float[p];
            _startIndex = 0;
            _count = 0;
        }
    }

    private void Push(float value)
    {
        if (_buffer == null) return;
        int len = _buffer.Length;
        if (_count < len)
        {
            _buffer[(_startIndex + _count) % len] = value;
            _count++;
        }
        else
        {
            _buffer[_startIndex] = value;
            _startIndex = (_startIndex + 1) % len;
        }
    }

    private void AdjustRange(float value)
    {
        float min = RangeMin.Value;
        float max = RangeMax.Value;
        float range = max - min;
        if (range <= 0f)
            return;

        float minThreshold = max - range * MinRangeAdjustThreshold.Value;
        float maxThreshold = min + range * MaxRangeAdjustThreshold.Value;
        if (value < minThreshold)
            RangeMin.Value -= (min - value) * MinRangeAdjustMultiplier.Value;
        else if (value > maxThreshold)
            RangeMax.Value += (value - max) * MaxRangeAdjustMultiplier.Value;
    }
}
