// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI;

public enum TextHorizontalAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

public enum TextVerticalAlignment
{
    Top,
    Middle,
    Bottom,
}

// The block of lines a vertical alignment lines up with the top, middle or bottom.
public enum TextBlockMeasure
{
    // Every line's full box: the first line's top to the last line's bottom.
    LineBoxes,
    // The glyphs drawn: the first line's highest to the last line's lowest.
    Glyphs,
    // One line height above the first baseline, down to the last baseline.
    Baselines,
}

// How a justified line fills the width of its rect.
public enum TextJustification
{
    // The gaps between words widen; a paragraph's last line stays as it is.
    WordGaps,
    // Every glyph moves out in proportion to how far along the line it sits, on any line that fills two thirds
    // of the width or more; a shorter one stays as it is.
    Glyphs,
}
