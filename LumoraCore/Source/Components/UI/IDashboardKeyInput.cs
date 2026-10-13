// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

// A dashboard screen that wants raw keystrokes while it is the current screen (an inline name field, a
// search well). The dash routes typed chars, backspace, enter and escape at the current screen when it
// implements this; each method returns true if it consumed the key, so the dash does not also treat it as
// search. -xlinka
public interface IDashboardKeyInput
{
    // Whether one of the screen's inline fields holds focus right now. The VR keyboard shows for a
    // dash field the same way it shows for a TextInput: on the field taking focus, never on the screen
    // merely being able to take keys. -xlinka
    bool HasFieldFocus { get; }

    // Inline fields copy their current value; null means there is no copyable field.
    string? CopyText => null;

    bool ConsumeChar(char c);
    bool ConsumeBackspace();
    bool ConsumeEnter();
    bool ConsumeEscape();

    // The keys an inline field has no use for; a screen that drives something bigger (the desktop) does.
    bool ConsumeKey(DashboardKey key) => false;

    // The keyboard was put away by hand. A screen whose field focus is its own flag drops it here, or the
    // keyboard would see the focus still held and come straight back up.
    void ReleaseFieldFocus()
    {
    }
}

public enum DashboardKey
{
    Tab,
    Left,
    Right,
    Up,
    Down,
    Copy,
    Paste,
}
