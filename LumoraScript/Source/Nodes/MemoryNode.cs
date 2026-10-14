// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A value with a memory. Build declares the ports and a Follow or FrameDelta shape, which the compiler
// lowers. It computes wherever a value node could, and what it remembers lives in the script's instance,
// so every machine running the script remembers its own. -xlinka
public abstract class MemoryNode : ScriptNode
{
    public abstract void Build(FlowBuilder f);
}
