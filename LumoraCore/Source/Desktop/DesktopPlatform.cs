// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Desktop;

// Picks how this machine's desktop is reached, once.
//
// Under Wine every Windows question answers yes, but there is no desktop duplication in a prefix and
// SendInput only reaches the prefix's own windows, so the desktop is left alone there rather than offered
// and broken.
//
// A Wayland session is not read through X even when X is there: XWayland only holds the X programs, so
// its screen is mostly black. Wayland goes through the desktop portal, which asks the user.
//
// A standalone headset has no desktop to show. It is turned away first, before anything asks whether
// this is Linux, since Android can answer that one either way. -xlinka
public static class DesktopPlatform
{
    private static readonly Lazy<IDesktopBackend?> _backend = new(Pick);

    public static IDesktopBackend? Backend => _backend.Value;

    public static bool IsSupported => Backend != null;

    private static IDesktopBackend? Pick()
    {
        try
        {
            if (OperatingSystem.IsAndroid())
                return null;
            if (OperatingSystem.IsWindows())
                return Engine.IsWine ? null : new WindowsDesktopBackend();
            if (OperatingSystem.IsLinux())
            {
                if (IsWaylandSession())
                    return PortalDesktopBackend.TryCreate();
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                    return X11DesktopBackend.TryCreate();
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"DesktopPlatform: no desktop capture: {ex.Message}");
        }
        return null;
    }

    private static bool IsWaylandSession()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            || string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);
}
