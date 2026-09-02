// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Helio.UI.Layout;

// Pure 2D bin-packing for the widget grid, in cell coordinates: seed a 1x1 at the cursor, try the
// widget's preferred cell sizes, otherwise grow the seed outward (up/right/down/left) into the largest
// collision-free rect and fit a preferred size inside it. No engine dependencies, so it is
// unit-testable on its own. -xlinka
public static class WidgetGridPlacement
{
    public static bool IntersectsAny(in GridRect rect, IReadOnlyList<GridRect> occupied)
    {
        for (int i = 0; i < occupied.Count; i++)
        {
            var o = occupied[i];
            if (rect.Intersects(in o))
                return true;
        }
        return false;
    }

    // Collision-free placement for a widget near the cursor cell. Tries each preferred size (largest
    // first is the caller's job), else grows a 1x1 seed into the biggest free rect and fits the best
    // preferred size in it. Null when even a 1x1 at the cursor collides.
    public static GridRect? FindPlacement(
        int cursorX, int cursorY,
        int gridWidth, int gridHeight,
        IReadOnlyList<(int w, int h)> preferredSizes,
        int minWidth, int minHeight,
        IReadOnlyList<GridRect> occupied)
    {
        if (gridWidth <= 0 || gridHeight <= 0)
            return null;

        cursorX = Clamp(cursorX, 0, gridWidth - 1);
        cursorY = Clamp(cursorY, 0, gridHeight - 1);

        var seed = new GridRect(cursorX, cursorY, 1, 1);
        if (IntersectsAny(in seed, occupied))
            return null;

        if (preferredSizes != null)
        {
            for (int i = 0; i < preferredSizes.Count; i++)
            {
                var (w, h) = preferredSizes[i];
                if (w < 1 || h < 1 || w > gridWidth || h > gridHeight)
                    continue;
                var r = CenterClamp(cursorX, cursorY, w, h, gridWidth, gridHeight);
                if (!IntersectsAny(in r, occupied))
                    return r;
            }
        }

        var rect = GrowFree(seed, gridWidth, gridHeight, occupied);

        if (preferredSizes != null)
        {
            for (int i = 0; i < preferredSizes.Count; i++)
            {
                var (w, h) = preferredSizes[i];
                if (w >= 1 && h >= 1 && w <= rect.Width && h <= rect.Height)
                    return FitWithin(rect, w, h, cursorX, cursorY);
            }
        }

        int fw = Clamp(minWidth < 1 ? 1 : minWidth, 1, rect.Width);
        int fh = Clamp(minHeight < 1 ? 1 : minHeight, 1, rect.Height);
        return FitWithin(rect, fw, fh, cursorX, cursorY);
    }

    // Grow a free seed rect outward one cell at a time in every direction until each side is stopped
    // by an edge or another widget. The result is the free area a carried widget can negotiate into.
    public static GridRect GrowFree(GridRect seed, int gridWidth, int gridHeight, IReadOnlyList<GridRect> occupied)
    {
        var rect = seed;
        bool up = true, right = true, down = true, left = true;
        while (up || right || down || left)
        {
            if (up) up = TryGrowUp(ref rect, gridHeight, occupied);
            if (right) right = TryGrowRight(ref rect, gridWidth, occupied);
            if (down) down = TryGrowDown(ref rect, occupied);
            if (left) left = TryGrowLeft(ref rect, occupied);
        }
        return rect;
    }

    // Place a w x h rect inside `free`, positioned to contain the cursor where possible, clamped to `free`.
    public static GridRect FitWithin(in GridRect free, int w, int h, int cursorX, int cursorY)
    {
        int x = Clamp(cursorX - w / 2, free.X, free.Right - w);
        int y = Clamp(cursorY - h / 2, free.Y, free.Top - h);
        return new GridRect(x, y, w, h);
    }

    // Center a w x h rect on the cursor, clamped into the [0,grid) canvas.
    public static GridRect CenterClamp(int cursorX, int cursorY, int w, int h, int gridWidth, int gridHeight)
    {
        int x = Clamp(cursorX - w / 2, 0, gridWidth - w);
        int y = Clamp(cursorY - h / 2, 0, gridHeight - h);
        return new GridRect(x, y, w, h);
    }

    private static bool TryGrowUp(ref GridRect rect, int gridHeight, IReadOnlyList<GridRect> occupied)
    {
        if (rect.Top >= gridHeight) return false;
        var grown = new GridRect(rect.X, rect.Y, rect.Width, rect.Height + 1);
        if (IntersectsAny(in grown, occupied)) return false;
        rect = grown;
        return true;
    }

    private static bool TryGrowDown(ref GridRect rect, IReadOnlyList<GridRect> occupied)
    {
        if (rect.Y <= 0) return false;
        var grown = new GridRect(rect.X, rect.Y - 1, rect.Width, rect.Height + 1);
        if (IntersectsAny(in grown, occupied)) return false;
        rect = grown;
        return true;
    }

    private static bool TryGrowRight(ref GridRect rect, int gridWidth, IReadOnlyList<GridRect> occupied)
    {
        if (rect.Right >= gridWidth) return false;
        var grown = new GridRect(rect.X, rect.Y, rect.Width + 1, rect.Height);
        if (IntersectsAny(in grown, occupied)) return false;
        rect = grown;
        return true;
    }

    private static bool TryGrowLeft(ref GridRect rect, IReadOnlyList<GridRect> occupied)
    {
        if (rect.X <= 0) return false;
        var grown = new GridRect(rect.X - 1, rect.Y, rect.Width + 1, rect.Height);
        if (IntersectsAny(in grown, occupied)) return false;
        rect = grown;
        return true;
    }

    // Scan the grid for the first cell a placement succeeds at, starting at (startX, startY) and walking
    // row-first or column-first. Used to auto-place a newly added widget when no explicit cell is given.
    // Null if the grid is full.
    public static GridRect? FindInsertion(
        int startX, int startY, int gridWidth, int gridHeight,
        IReadOnlyList<(int w, int h)> preferredSizes, int minWidth, int minHeight,
        IReadOnlyList<GridRect> occupied, bool rowFirst = true)
    {
        if (gridWidth <= 0 || gridHeight <= 0)
            return null;
        startX = Clamp(startX, 0, gridWidth - 1);
        startY = Clamp(startY, 0, gridHeight - 1);

        for (int i = 0; i < gridWidth * gridHeight; i++)
        {
            int cx, cy;
            if (rowFirst) { cx = (startX + i) % gridWidth; cy = (startY + (startX + i) / gridWidth) % gridHeight; }
            else { cy = (startY + i) % gridHeight; cx = (startX + (startY + i) / gridHeight) % gridWidth; }

            var placement = FindPlacement(cx, cy, gridWidth, gridHeight, preferredSizes, minWidth, minHeight, occupied);
            if (placement.HasValue)
                return placement;
        }
        return null;
    }

    // Place a widget at a forced cell size anchored at its top-left, shrinking toward 1x1 until it is
    // collision-free (null if even 1x1 at the origin collides).
    public static GridRect? FitForcedSize(
        int originX, int originY,
        int gridWidth, int gridHeight,
        int forcedWidth, int forcedHeight,
        IReadOnlyList<GridRect> occupied)
    {
        if (gridWidth <= 0 || gridHeight <= 0)
            return null;

        originX = Clamp(originX, 0, gridWidth - 1);
        originY = Clamp(originY, 0, gridHeight - 1);
        int maxW = gridWidth - originX;
        int maxH = gridHeight - originY;
        int w = Clamp(forcedWidth, 1, maxW);
        int h = Clamp(forcedHeight, 1, maxH);

        for (; w >= 1; w--)
        {
            for (int hh = h; hh >= 1; hh--)
            {
                var r = new GridRect(originX, originY, w, hh);
                if (!IntersectsAny(in r, occupied))
                    return r;
            }
        }
        return null;
    }

    private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}
