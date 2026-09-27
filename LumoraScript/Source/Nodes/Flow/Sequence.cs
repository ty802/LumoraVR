// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Runs each chain to its end before starting the next.
[Node("Flow/Sequence")]
public sealed class Sequence : FlowNode
{
    public override void Build(FlowBuilder f)
        => f.Sequence(f.Out("out0"), f.Out("out1"), f.Out("out2"), f.Out("out3"));
}
