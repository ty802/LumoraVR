// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets.Animation;

namespace Lumora.Core.Components;

// Straight lines between points, flat past either end: how every curve field on a component reads
// itself (a bone chain's falloff, a particle curve, gradient stops). No points reads as the field's
// own default, which differs by field.
public sealed class LinearRowSampler : ICurveSampler
{
    private readonly IReadOnlyList<CurveKeyRow> _rows;
    private readonly int _channels;
    private readonly float _empty;

    public LinearRowSampler(IReadOnlyList<CurveKeyRow> rows, int channels, float empty)
    {
        _rows = rows;
        _channels = channels;
        _empty = empty;
    }

    public float Sample(int channel, float time, ref float carry)
    {
        float value = Evaluate(channel, time);
        carry = value;
        return value;
    }

    public CurveKeyRow RowAt(float time)
    {
        var row = new CurveKeyRow(_channels) { Time = time, Shape = KeyframeInterpolation.Linear };
        for (int c = 0; c < _channels; c++)
            row.Values[c] = Evaluate(c, time);
        return row;
    }

    private float Evaluate(int channel, float time)
    {
        int n = _rows.Count;
        if (n == 0 || channel < 0 || channel >= _channels)
            return _empty;
        if (time <= _rows[0].Time)
            return _rows[0].Values[channel];
        if (time >= _rows[n - 1].Time)
            return _rows[n - 1].Values[channel];
        for (int i = 1; i < n; i++)
        {
            var b = _rows[i];
            if (time > b.Time)
                continue;
            var a = _rows[i - 1];
            float span = b.Time - a.Time;
            if (span <= 1e-6f)
                return b.Values[channel];
            float u = (time - a.Time) / span;
            return a.Values[channel] + (b.Values[channel] - a.Values[channel]) * u;
        }
        return _rows[n - 1].Values[channel];
    }
}
