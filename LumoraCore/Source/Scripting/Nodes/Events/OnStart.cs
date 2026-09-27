// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Once, when the script starts. A live graph swap keeps its stored values and does not start again.
[Node("Events/On Start")]
public sealed class OnStart : EventNode<ScriptStarted>
{
}
