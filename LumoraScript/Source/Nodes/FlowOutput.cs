// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

public readonly struct FlowOutput<T>
{
    internal readonly int Index;

    internal FlowOutput(int index)
    {
        Index = index;
    }
}
