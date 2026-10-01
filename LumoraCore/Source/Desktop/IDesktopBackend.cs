// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Desktop;

// How this machine's desktop is seen and driven: the monitors, a capture per monitor, and input. -xlinka
public interface IDesktopBackend
{
    string Name { get; }

    // Every monitor attached to the desktop, the main one first. Empty while the desktop has not said yet.
    IReadOnlyList<DesktopDisplay> Displays();

    DesktopCapture CreateCapture(DesktopDisplay display);

    // Null where the desktop can be seen but not driven.
    IDesktopInput? Input { get; }

    // What stands between the user and the monitor list when it is empty, in words for them.
    string? Status => null;

    // The user came back to the desktop: a desktop that asks permission may ask again now, never before.
    void Retry()
    {
    }
}
