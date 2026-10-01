// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lumora.Core.Desktop;

// An X11 session: monitors from RandR, captures through shared memory, input through XTest. A server
// without XTest still shows its desktop; it just cannot be driven. -xlinka
[SupportedOSPlatform("linux")]
internal sealed unsafe class X11DesktopBackend : IDesktopBackend
{
    private X11DesktopBackend(IDesktopInput? input)
    {
        Input = input;
    }

    public string Name => "X11";

    public IDesktopInput? Input { get; }

    public static X11DesktopBackend? TryCreate()
    {
        if (!X11Native.Has(X11Native.X11))
            return null;
        X11Native.Prepare();
        IntPtr x = X11Native.Open();
        if (x == IntPtr.Zero)
            return null;
        try
        {
            bool input = X11Native.Has(X11Native.Xtst) && X11Native.XTestQueryExtension(x, out _, out _, out _, out _) != 0;
            return new X11DesktopBackend(input ? new X11DesktopInput() : null);
        }
        finally
        {
            X11Native.Close(x);
        }
    }

    public DesktopCapture CreateCapture(DesktopDisplay display) => new X11DesktopCapture(display);

    // Each monitor RandR knows, or the whole screen as one when it knows none (or is not there).
    public IReadOnlyList<DesktopDisplay> Displays()
    {
        var found = new List<DesktopDisplay>();
        IntPtr x = X11Native.Open();
        if (x == IntPtr.Zero)
            return found;
        try
        {
            int screen = X11Native.XDefaultScreen(x);
            nuint root = X11Native.XRootWindow(x, screen);
            if (X11Native.Has(X11Native.Xrandr))
            {
                var monitors = X11Native.XRRGetMonitors(x, root, 1, out int count);
                if (monitors != null)
                {
                    try
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var m = monitors[i];
                            if (m.Width > 0 && m.Height > 0)
                                found.Add(new DesktopDisplay(0, i, NameOf(x, m.Name, i), m.X, m.Y, m.Width, m.Height, m.Primary != 0));
                        }
                    }
                    finally
                    {
                        X11Native.XRRFreeMonitors(monitors);
                    }
                }
            }
            if (found.Count == 0)
            {
                found.Add(new DesktopDisplay(0, 0, "Screen", 0, 0,
                    X11Native.XDisplayWidth(x, screen), X11Native.XDisplayHeight(x, screen), true));
            }
            else if (!found.Exists(d => d.IsPrimary))
            {
                found[0] = found[0] with { IsPrimary = true };
            }
        }
        finally
        {
            X11Native.Close(x);
        }
        DesktopDisplay.SortMainFirst(found);
        return found;
    }

    private static string NameOf(IntPtr x, nuint atom, int index)
    {
        if (atom == 0)
            return "Monitor " + (index + 1);
        byte* name = X11Native.XGetAtomName(x, atom);
        if (name == null)
            return "Monitor " + (index + 1);
        try
        {
            return Marshal.PtrToStringUTF8((IntPtr)name) ?? "Monitor " + (index + 1);
        }
        finally
        {
            X11Native.XFree(name);
        }
    }
}
