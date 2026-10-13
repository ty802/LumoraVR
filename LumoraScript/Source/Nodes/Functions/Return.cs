// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Ends a Function's body, handing back what is wired into it. "fn" names the Function it returns from, and
// its inputs are what that Function gives. A body that runs out without reaching one gives every output's
// default.
[Node("Functions/Return")]
public sealed class Return : ScriptNode
{
}
