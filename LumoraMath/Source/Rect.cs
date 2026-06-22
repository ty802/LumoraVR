// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Math;

/// <summary>
/// Axis-aligned 2D rectangle. Position is bottom-left corner (xMin, yMin).
/// </summary>
public struct Rect : IEquatable<Rect>
{
    public float x;
    public float y;
    public float width;
    public float height;

    public Rect(float x, float y, float width, float height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public Rect(in float2 position, in float2 size)
    {
        x = position.x;
        y = position.y;
        width = size.x;
        height = size.y;
    }

    public static Rect Zero => new Rect(0f, 0f, 0f, 0f);
    public static Rect UnitRect => new Rect(0f, 0f, 1f, 1f);

    public float xMin
    {
        get => x;
        set { float old = x; x = value; width -= x - old; }
    }

    public float yMin
    {
        get => y;
        set { float old = y; y = value; height -= y - old; }
    }

    public float xMax
    {
        get => x + width;
        set => width = value - x;
    }

    public float yMax
    {
        get => y + height;
        set => height = value - y;
    }

    public float2 Position
    {
        get => new float2(x, y);
        set { x = value.x; y = value.y; }
    }

    public float2 Size
    {
        get => new float2(width, height);
        set { width = value.x; height = value.y; }
    }

    public float2 Min => new float2(x, y);
    public float2 Max => new float2(x + width, y + height);
    public float2 Center => new float2(x + width * 0.5f, y + height * 0.5f);

    public float Area => width * height;

    public bool IsEmpty => width <= 0f || height <= 0f;

    public bool Contains(in float2 point)
    {
        return point.x >= x && point.x <= x + width
            && point.y >= y && point.y <= y + height;
    }

    public bool Overlaps(in Rect other)
    {
        return xMin < other.xMax && xMax > other.xMin
            && yMin < other.yMax && yMax > other.yMin;
    }

    /// <summary>
    /// True if this rect fully contains <paramref name="other"/> (every edge of other is at or inside this).
    /// A clip rect that encloses a graphic's bounds discards nothing, so the clip can be skipped for it.
    /// </summary>
    public bool Encloses(in Rect other)
    {
        return xMin <= other.xMin && yMin <= other.yMin
            && xMax >= other.xMax && yMax >= other.yMax;
    }

    public Rect Intersection(in Rect other)
    {
        float ix = MathF.Max(xMin, other.xMin);
        float iy = MathF.Max(yMin, other.yMin);
        float ax = MathF.Min(xMax, other.xMax);
        float ay = MathF.Min(yMax, other.yMax);
        if (ax < ix || ay < iy) return Zero;
        return new Rect(ix, iy, ax - ix, ay - iy);
    }

    public static Rect FromMinMax(in float2 min, in float2 max)
        => new Rect(min.x, min.y, max.x - min.x, max.y - min.y);

    /// <summary>
    /// Returns the smallest rect containing both this rect and <paramref name="other"/>. An empty/degenerate
    /// rect counts as "nothing" so it doesn't drag the union toward the origin (e.g. a collapsed UI element).
    /// </summary>
    public Rect Encapsulate(in Rect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        float minX = MathF.Min(xMin, other.xMin);
        float minY = MathF.Min(yMin, other.yMin);
        float maxX = MathF.Max(xMax, other.xMax);
        float maxY = MathF.Max(yMax, other.yMax);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public bool Equals(Rect other)
        => x == other.x && y == other.y && width == other.width && height == other.height;

    public override bool Equals(object? obj) => obj is Rect r && Equals(r);

    public override int GetHashCode()
        => HashCode.Combine(x, y, width, height);

    public static bool operator ==(Rect a, Rect b) => a.Equals(b);
    public static bool operator !=(Rect a, Rect b) => !a.Equals(b);

    public override string ToString() => $"Rect({x}, {y}, {width}, {height})";
}
