// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Everyone in the session, the host included.
[Node("Users/User Count", Capability.UsersRead)]
public sealed class UserCount : ReadNode
{
    [return: Port("count")]
    public static int Run(ScriptContext ctx) => ScriptReads.Users(ctx).Count;
}
