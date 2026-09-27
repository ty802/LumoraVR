// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// One value, set on its card, for as many inputs as are wired to it. "result" is "value" as it stands;
// the compiler reads straight through it. -xlinka
[Node("Variables/Constant")]
public sealed class Constant<T> : ScriptNode
{
}
