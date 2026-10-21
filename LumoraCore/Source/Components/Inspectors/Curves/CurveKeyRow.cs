// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// One key of a track as the curve editor sees it: a time, the shape of the segment that leaves it, and
// one float per channel (X/Y/Z of a position, R/G/B/A of a color, the single value of a weight).
// Tangents are value per second per channel, the same units the clip stores.
//
// A rotation key shows as three Euler channels in degrees but keeps the quaternion it was read as, and
// that quaternion is what gets written back unless a channel was actually moved: going through Euler
// and back would nudge every untouched key by float error on each save. -xlinka
public sealed class CurveKeyRow
{
    public float Time;
    public KeyframeInterpolation Shape;
    public readonly float[] Values;
    public readonly float[] In;
    public readonly float[] Out;

    public floatQ Rotation = floatQ.Identity;
    public floatQ RotationIn;
    public floatQ RotationOut;
    public bool RotationEdited;

    public CurveKeyRow(int channels)
    {
        Values = new float[channels];
        In = new float[channels];
        Out = new float[channels];
    }

    public int Channels => Values.Length;

    public CurveKeyRow Copy()
    {
        var copy = new CurveKeyRow(Values.Length)
        {
            Time = Time,
            Shape = Shape,
            Rotation = Rotation,
            RotationIn = RotationIn,
            RotationOut = RotationOut,
            RotationEdited = RotationEdited,
        };
        Array.Copy(Values, copy.Values, Values.Length);
        Array.Copy(In, copy.In, In.Length);
        Array.Copy(Out, copy.Out, Out.Length);
        return copy;
    }

    public static List<CurveKeyRow> CopyAll(IReadOnlyList<CurveKeyRow> rows)
    {
        var copy = new List<CurveKeyRow>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
            copy.Add(rows[i].Copy());
        return copy;
    }

    public static bool Same(IReadOnlyList<CurveKeyRow>? a, IReadOnlyList<CurveKeyRow>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Time != y.Time || x.Shape != y.Shape || x.Values.Length != y.Values.Length)
                return false;
            for (int c = 0; c < x.Values.Length; c++)
            {
                if (x.Values[c] != y.Values[c] || x.In[c] != y.In[c] || x.Out[c] != y.Out[c])
                    return false;
            }
        }
        return true;
    }

    // Where a new key at this time goes among rows already sorted by time.
    public static int InsertIndex(IReadOnlyList<CurveKeyRow> rows, float time)
    {
        int i = 0;
        while (i < rows.Count && rows[i].Time <= time)
            i++;
        return i;
    }
}
