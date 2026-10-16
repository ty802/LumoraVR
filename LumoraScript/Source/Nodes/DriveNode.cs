// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Holds a field at a value while the script runs. Build declares the field and the value with the Drive
// shape. Every frame, every machine running the script works the value out in its drives function, from
// what needs no event, and writes it into the field on that machine only. -xlinka
public abstract class DriveNode : ScriptNode
{
    public abstract void Build(FlowBuilder f);
}
