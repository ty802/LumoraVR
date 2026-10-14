// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Touch;

// What a contact receiver reports. Constant: 1 while anything it listens for touches it. Enter: 1 for the one frame
// something arrives fast enough. Depth: how far in the nearest one reaches, 0 at the edge and 1 at the middle.
public enum ContactReceiverMode
{
    Constant,
    Enter,
    Depth,
}
