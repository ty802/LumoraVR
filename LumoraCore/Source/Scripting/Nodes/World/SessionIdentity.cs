// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// The session's id, the same on every machine in it. A world nobody hosts still has one of its own.
[Node("World/Session/Session Id", Capability.WorldRead)]
public sealed class SessionIdentity : ReadNode
{
    [return: Port("id")]
    public static string Run(ScriptContext ctx) => ctx.World.SessionID?.Value ?? string.Empty;
}
