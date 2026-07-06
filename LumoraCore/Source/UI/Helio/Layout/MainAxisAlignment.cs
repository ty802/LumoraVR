// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI.Layout;

/// <summary>
/// How children are distributed along a layout's MAIN axis when it isn't
/// force-expanding that axis (the CSS justify-content equivalent).
/// </summary>
public enum MainAxisAlignment
{
    /// <summary>Packed at the start (left/top). Default.</summary>
    Start,
    /// <summary>Packed centered, leftover split evenly on both ends.</summary>
    Center,
    /// <summary>Packed at the end (right/bottom).</summary>
    End,
    /// <summary>First/last flush to the edges, leftover split between items.</summary>
    SpaceBetween,
    /// <summary>Equal space around each item (half-units at the ends).</summary>
    SpaceAround,
    /// <summary>Equal space between items and at both ends.</summary>
    SpaceEvenly,
}
