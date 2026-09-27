// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

public readonly struct FlowExit
{
    internal readonly int Index;

    internal FlowExit(int index)
    {
        Index = index;
    }
}
