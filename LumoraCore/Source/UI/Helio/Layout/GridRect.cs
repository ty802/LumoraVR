// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Helio.UI.Layout;

/// <summary>
/// An integer rectangle in grid-cell coordinates - the placement record for a widget in a WidgetGrid.
/// (X,Y) is the min corner cell; Width/Height are in cells. This is what the 2D placement engine packs
/// and collision-checks, and what serializes as "[X=0;Y=0;W=2;H=1]" for authored default layouts. -xlinka
/// </summary>
public readonly struct GridRect : IEquatable<GridRect>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Width;
    public readonly int Height;

    public GridRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int Right => X + Width;
    public int Top => Y + Height;

    /// <summary>Half-open overlap test: rects sharing only an edge do NOT intersect.</summary>
    public bool Intersects(in GridRect other)
        => X < other.Right && other.X < Right && Y < other.Top && other.Y < Top;

    public GridRect Translate(int dx, int dy) => new GridRect(X + dx, Y + dy, Width, Height);

    public bool Equals(GridRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is GridRect g && Equals(g);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>Serializes as "[X=0;Y=0;W=2;H=1]" - the authored-layout form.</summary>
    public override string ToString() => $"[X={X};Y={Y};W={Width};H={Height}]";

    /// <summary>Parse "[X=0;Y=0;W=2;H=1]" (whitespace + brackets tolerant). Returns false on malformed input.</summary>
    public static bool TryParse(string? s, out GridRect rect)
    {
        rect = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        int x = 0, y = 0, w = 1, h = 1;
        bool gotX = false, gotW = false;
        foreach (var rawPart in s.Trim().Trim('[', ']').Split(';'))
        {
            var part = rawPart.Trim();
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = part.Substring(0, eq).Trim().ToUpperInvariant();
            if (!int.TryParse(part.Substring(eq + 1).Trim(), out var val))
                return false;
            switch (key)
            {
                case "X": x = val; gotX = true; break;
                case "Y": y = val; break;
                case "W": case "WIDTH": w = val; gotW = true; break;
                case "H": case "HEIGHT": h = val; break;
            }
        }

        if (!gotX && !gotW)
            return false;
        if (w < 1) w = 1;
        if (h < 1) h = 1;
        rect = new GridRect(x, y, w, h);
        return true;
    }
}
