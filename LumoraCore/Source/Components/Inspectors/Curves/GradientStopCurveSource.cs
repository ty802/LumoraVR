// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Components.Utility;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// Gradient stops (a value gradient, a curve driver, a blend shape curve): a position on 0..1 and a
// value there, one channel per component of the value. The list is read sorted, so stops written in any
// order come back in time order.
public sealed class GradientStopCurveSource<T> : FieldCurveSource
{
    private readonly SyncList<GradientStop<T>> _stops;
    private readonly float[] _scratch = new float[System.Math.Max(1, CurveChannels<T>.Count)];

    public GradientStopCurveSource(Component owner, string member, SyncList<GradientStop<T>> stops)
        : base(owner, member, CurveChannels<T>.Labels, whole: CurveChannels<T>.Kind == CurveValueKind.Whole)
    {
        _stops = stops;
    }

    protected override float EmptyValue => 0f;

    protected override SyncElement ListElement => _stops;

    protected override List<CurveKeyRow> ReadRows()
    {
        var rows = new List<CurveKeyRow>(_stops.Count);
        for (int i = 0; i < _stops.Count; i++)
        {
            var stop = _stops[i];
            var row = new CurveKeyRow(CurveChannels<T>.Count) { Time = stop.Position.Value, Shape = KeyframeInterpolation.Linear };
            CurveChannels<T>.Split(stop.Value.Value, row.Values);
            rows.Add(row);
        }
        return rows;
    }

    protected override void WriteRows(IReadOnlyList<CurveKeyRow> rows)
    {
        _stops.EnsureExactCount(rows.Count);
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < rows.Count; i++)
        {
            var stop = _stops[i];
            float t = rows[i].Time;
            if (stop.Position.Value != t)
                stop.Position.Value = t;
            var value = CurveChannels<T>.Join(rows[i].Values);
            if (!comparer.Equals(stop.Value.Value, value))
                stop.Value.Value = value;
        }
    }

    protected override long Hash()
    {
        long hash = _stops.Count;
        for (int i = 0; i < _stops.Count; i++)
        {
            var stop = _stops[i];
            hash = Mix(hash, stop.Position.Value);
            CurveChannels<T>.Split(stop.Value.Value, _scratch);
            for (int c = 0; c < CurveChannels<T>.Count; c++)
                hash = Mix(hash, _scratch[c]);
        }
        return hash;
    }
}
