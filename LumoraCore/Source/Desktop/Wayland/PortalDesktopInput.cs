// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.Versioning;

namespace Lumora.Core.Desktop;

// Input through the portal's remote desktop: pointer positions per shared monitor, buttons as Linux input
// codes, wheel detents, and keys as keysyms, which the compositor maps onto its own keyboard layout.
//
// Wayland does not tell a program where the pointer is, nor which window has the keyboard, so the
// pointer cannot be read back, and our own window's focus is what the engine was told by its windowing
// layer. -xlinka
[SupportedOSPlatform("linux")]
internal sealed class PortalDesktopInput : IDesktopInput
{
    private const int ButtonLeft = 0x110, ButtonRight = 0x111, ButtonMiddle = 0x112;

    private readonly PortalDesktopBackend _backend;
    private readonly object _gate = new();
    private int _wheelX;
    private int _wheelY;

    public PortalDesktopInput(PortalDesktopBackend backend)
    {
        _backend = backend;
    }

    public bool MoveTo(int x, int y)
    {
        var session = _backend.Session;
        if (session is not { CanPoint: true })
            return false;
        foreach (var stream in session.Streams)
        {
            var display = stream.Display;
            if (display.Contains(x, y))
            {
                session.PointerTo(stream.Node, x - display.Left, y - display.Top);
                return true;
            }
        }
        return false;
    }

    public bool Button(DesktopButton button, bool down)
    {
        var session = _backend.Session;
        if (session is not { CanPoint: true })
            return false;
        session.PointerButton(button switch
        {
            DesktopButton.Right => ButtonRight,
            DesktopButton.Middle => ButtonMiddle,
            _ => ButtonLeft,
        }, down);
        return true;
    }

    // Detents only, so steps smaller than one are saved up. Wayland counts down and right as positive;
    // this interface counts up and right.
    public bool Wheel(int delta, bool horizontal)
    {
        var session = _backend.Session;
        if (session is not { CanPoint: true })
            return false;
        lock (_gate)
        {
            ref int owed = ref horizontal ? ref _wheelX : ref _wheelY;
            owed += delta;
            int steps = owed / IDesktopInput.WheelStep;
            if (steps == 0)
                return true;
            owed -= steps * IDesktopInput.WheelStep;
            session.PointerAxis(horizontal, horizontal ? steps : -steps);
        }
        return true;
    }

    public bool TryGetCursor(out int x, out int y)
    {
        x = y = 0;
        return false;
    }

    public bool OwnWindowFocused() => Lumora.Core.Engine.Current?.InputInterface?.IsWindowFocused ?? true;

    public bool Type(string text)
    {
        var session = _backend.Session;
        if (session is not { CanType: true } || string.IsNullOrEmpty(text) || OwnWindowFocused())
            return false;
        bool all = true;
        foreach (var rune in text.EnumerateRunes())
        {
            uint keysym = DesktopKeysyms.Of(rune);
            if (keysym == 0)
            {
                all = false;
                continue;
            }
            Tap(session, keysym);
        }
        return all;
    }

    public bool Press(DesktopKey key)
    {
        var session = _backend.Session;
        if (session is not { CanType: true } || (key != DesktopKey.Start && OwnWindowFocused()))
            return false;
        if (key is DesktopKey.Copy or DesktopKey.Paste)
        {
            session.Keysym(DesktopKeysyms.ControlLeft, true);
            Tap(session, DesktopKeysyms.Of(key));
            session.Keysym(DesktopKeysyms.ControlLeft, false);
            return true;
        }
        Tap(session, DesktopKeysyms.Of(key));
        return true;
    }

    private static void Tap(PortalSession session, uint keysym)
    {
        session.Keysym(keysym, true);
        session.Keysym(keysym, false);
    }
}
