// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Index wrapping shared by the selector components.
public static class SelectionIndex
{
    // C# remainder keeps the sign of the dividend, so a negative index would land outside the list and
    // throw. Push it back into range instead of clamping: a cycling index should come round to the end,
    // not stick at zero. -xlinka
    public static int Wrap(int index, int count)
    {
        if (count <= 0)
            return 0;
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }
}
