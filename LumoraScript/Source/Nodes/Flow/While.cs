// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Runs the body while the condition holds, reading it afresh before every turn, then completed.
[Node("Flow/While")]
public sealed class While : FlowNode
{
    public override void Build(FlowBuilder f)
        => f.While(f.Input<bool>("condition"), f.Out("body"), f.Out("completed"));
}
