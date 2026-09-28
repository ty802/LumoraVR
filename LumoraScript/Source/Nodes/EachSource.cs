// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// What a For Each walks. The host that runs the script lists the items. The overlap sources are
// searched once, with the node's query floats, and the loop walks what that found.
public enum EachSource : byte
{
    Users,
    Children,
    OverlapSphere,
    OverlapBox
}
