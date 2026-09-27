// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

[Node("Flow/If")]
public sealed class If : FlowNode
{
    public override void Build(FlowBuilder f)
        => f.Branch(f.Input<bool>("condition"), f.Out("true"), f.Out("false"));
}
