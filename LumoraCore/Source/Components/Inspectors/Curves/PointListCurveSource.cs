// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// A curve stored as (x, y) points: x along 0..1 (root to tip on a bone chain), y the multiplier there.
// An empty list reads as 1 all the way along.
public sealed class PointListCurveSource : FieldCurveSource
{
    private readonly SyncFieldList<float2> _points;

    public PointListCurveSource(Component owner, string member, SyncFieldList<float2> points)
        : base(owner, member, new[] { "Value" }, whole: false)
    {
        _points = points;
    }

    protected override float EmptyValue => 1f;

    protected override SyncElement ListElement => _points;

    protected override List<CurveKeyRow> ReadRows()
    {
        var rows = new List<CurveKeyRow>(_points.Count);
        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            var row = new CurveKeyRow(1) { Time = p.x, Shape = KeyframeInterpolation.Linear };
            row.Values[0] = p.y;
            rows.Add(row);
        }
        return rows;
    }

    protected override void WriteRows(IReadOnlyList<CurveKeyRow> rows)
    {
        _points.EnsureExactCount(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var p = new float2(rows[i].Time, rows[i].Values[0]);
            if (_points[i] != p)
                _points[i] = p;
        }
    }

    protected override long Hash()
    {
        long hash = _points.Count;
        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            hash = Mix(Mix(hash, p.x), p.y);
        }
        return hash;
    }
}
