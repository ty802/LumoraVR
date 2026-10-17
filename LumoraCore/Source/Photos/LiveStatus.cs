// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Photos;

// Whether the person at this machine is live: streaming or recording through the streaming program, showing
// the camera on their screen, or simply saying so. Their user record carries it to everyone, who see a mark
// on them; world logic can read it here. -xlinka
public static class LiveStatus
{
    public static bool IsLive { get; set; }
}
