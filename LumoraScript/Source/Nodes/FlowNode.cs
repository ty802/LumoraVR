// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Control flow. Build declares the ports and picks one of the shapes FlowBuilder offers; the registry
// runs it once at startup and the compiler lowers the shape.
public abstract class FlowNode : ScriptNode
{
    public abstract void Build(FlowBuilder f);
}
