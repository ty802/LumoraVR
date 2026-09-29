// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Input;

// This machine's window and the screen it sits on, in pixels, reported by the platform layer each frame
// next to the view info. Nothing is known until the first report, and a platform with no window
// (headless) never reports, so every reader has to cope with Known being false. -xlinka
public sealed class DisplayMetrics
{
    public bool Known { get; private set; }

    // The window's drawable size.
    public int2 Window { get; private set; }

    // The window's top left corner on the desktop, in the same pixels the mouse's desktop position uses.
    public int2 WindowPosition { get; private set; }

    // The screen the window is on.
    public int2 Screen { get; private set; }

    public void Report(int2 window, int2 windowPosition, int2 screen)
    {
        if (window.x <= 0 || window.y <= 0)
            return;
        Window = window;
        WindowPosition = windowPosition;
        Screen = screen;
        Known = true;
    }
}
