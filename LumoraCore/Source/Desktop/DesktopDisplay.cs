// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Desktop;

// One monitor as the capture sees it: which graphics adapter and output it hangs off (or which monitor of
// the X screen), and where it sits in the virtual desktop, in physical pixels. Input goes to those
// coordinates. -xlinka
public sealed record DesktopDisplay(int Adapter, int Output, string Name, int Left, int Top, int Width, int Height, bool IsPrimary)
{
    public string Key => $"{Adapter}:{Output}";

    public bool Contains(int x, int y) => x >= Left && x < Left + Width && y >= Top && y < Top + Height;

    // The main monitor first, then left to right, then top to bottom.
    public static void SortMainFirst(List<DesktopDisplay> displays)
    {
        displays.Sort((a, b) => a.IsPrimary != b.IsPrimary ? (a.IsPrimary ? -1 : 1)
            : a.Left != b.Left ? a.Left.CompareTo(b.Left)
            : a.Top.CompareTo(b.Top));
    }
}
