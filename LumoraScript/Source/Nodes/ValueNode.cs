// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Pure math and logic: parameters in, return value and out parameters out, no context and no world.
// The compiler relies on the purity to compute a shared result once, so a ValueNode that reaches for
// state is a bug in the node, not a feature.
public abstract class ValueNode : ScriptNode
{
}
