// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Desktop;

// Where a desktop texture's pixels come from, pulled by the renderer on its own thread rather than pushed
// at it: a capture runs as fast as the screen changes and the renderer only wants the latest.
//
// The picture is cut into horizontal strips so a change to one corner of the screen re-uploads one strip,
// not a whole 4K frame. Layer -1 is the mouse pointer's own image. -xlinka
public interface IDesktopFrameSource
{
    public const int PointerLayer = -1;

    // Copies a layer into pixels (RGBA8, grown as needed) when it changed since version, and moves version
    // on. False when nothing changed or the layer does not exist (yet).
    bool TryCopy(int layer, ref int version, ref byte[] pixels, out int width, out int height);
}
