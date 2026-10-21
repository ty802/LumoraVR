// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// A curve stored as two parallel lists, Times and Values (a particle size over lifetime, a trail's width
// along its length). Only as many points as the shorter list holds are real; writing back keeps the two
// the same length. An empty curve reads as 1.
public sealed class PairedListCurveSource : FieldCurveSource
{
    private readonly SyncFieldList<float> _times;
    private readonly SyncFieldList<float> _values;

    public PairedListCurveSource(Component owner, string member, SyncFieldList<float> times, SyncFieldList<float> values)
        : base(owner, member, new[] { "Value" }, whole: false)
    {
        _times = times;
        _values = values;
    }

    protected override float EmptyValue => 1f;

    protected override SyncElement ListElement => _times;

    protected override List<CurveKeyRow> ReadRows()
    {
        int count = System.Math.Min(_times.Count, _values.Count);
        var rows = new List<CurveKeyRow>(count);
        for (int i = 0; i < count; i++)
        {
            var row = new CurveKeyRow(1) { Time = _times[i], Shape = KeyframeInterpolation.Linear };
            row.Values[0] = _values[i];
            rows.Add(row);
        }
        return rows;
    }

    protected override void WriteRows(IReadOnlyList<CurveKeyRow> rows)
    {
        _times.EnsureExactCount(rows.Count);
        _values.EnsureExactCount(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            float t = rows[i].Time;
            float v = rows[i].Values[0];
            if (_times[i] != t)
                _times[i] = t;
            if (_values[i] != v)
                _values[i] = v;
        }
    }

    protected override long Hash()
    {
        int count = System.Math.Min(_times.Count, _values.Count);
        long hash = count;
        for (int i = 0; i < count; i++)
            hash = Mix(Mix(hash, _times[i]), _values[i]);
        return hash;
    }
}
