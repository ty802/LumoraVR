// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Desktop;

// One capture per monitor for the whole process, however many panels show it, running only while
// something holds it. Textures look captures up without holding them: a hidden page keeps its last
// picture and costs nothing. -xlinka
public static class DesktopCaptures
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DesktopCapture> Captures = new();
    private static readonly Dictionary<DesktopCapture, int> Holds = new();

    // The capture for a display key, created stopped when first asked for; null for a monitor that is
    // not attached.
    public static DesktopCapture? Get(string key)
    {
        var backend = DesktopPlatform.Backend;
        if (string.IsNullOrEmpty(key) || backend == null)
            return null;
        lock (Gate)
        {
            if (Captures.TryGetValue(key, out var capture))
                return capture;
        }
        foreach (var display in backend.Displays())
        {
            if (display.Key == key)
                return For(display);
        }
        return null;
    }

    public static DesktopCapture Hold(DesktopDisplay display)
    {
        lock (Gate)
        {
            var capture = For(display);
            Holds.TryGetValue(capture, out int holds);
            Holds[capture] = holds + 1;
            if (holds == 0)
                capture.Start();
            return capture;
        }
    }

    public static void Release(DesktopCapture capture)
    {
        lock (Gate)
        {
            if (!Holds.TryGetValue(capture, out int holds))
                return;
            if (holds > 1)
            {
                Holds[capture] = holds - 1;
                return;
            }
            Holds.Remove(capture);
            capture.Stop();
        }
    }

    // Keyed by adapter and output only. A resolution change takes the duplication away and the capture
    // reopens at the new size by itself; where the monitor sits is the page's business, not the capture's.
    private static DesktopCapture For(DesktopDisplay display)
    {
        lock (Gate)
        {
            if (Captures.TryGetValue(display.Key, out var existing))
                return existing;
            var capture = DesktopPlatform.Backend!.CreateCapture(display);
            Captures[display.Key] = capture;
            return capture;
        }
    }
}
