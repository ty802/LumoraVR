// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Desktop;

public enum DesktopButton
{
    Left,
    Right,
    Middle,
}

public enum DesktopKey
{
    Backspace,
    Tab,
    Enter,
    Escape,
    Left,
    Right,
    Up,
    Down,
    Start,
    Copy,
    Paste,
}

// Drives the real mouse and keyboard of this machine. Coordinates are pixels of the virtual desktop, the
// space DesktopDisplay reports monitors in.
//
// Keys land in whatever window has focus. When that window is ours they would come straight back in as
// typing and go round again, so text and editing keys are refused while it is. -xlinka
public interface IDesktopInput
{
    // One detent of a wheel.
    public const int WheelStep = 120;

    bool MoveTo(int x, int y);

    bool Button(DesktopButton button, bool down);

    // delta in WheelStep units per detent; positive scrolls up (or right).
    bool Wheel(int delta, bool horizontal);

    // Characters rather than keys, so the keyboard layout the machine thinks is plugged in does not matter.
    bool Type(string text);

    bool Press(DesktopKey key);

    bool TryGetCursor(out int x, out int y);

    bool OwnWindowFocused();
}
