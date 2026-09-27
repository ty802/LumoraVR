// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

[Node("Users/Is Host", Capability.UsersRead)]
public sealed class IsSessionHost : ReadNode
{
    [return: Port("host")]
    public static bool Run(ScriptContext ctx, User? user) => ScriptReads.Visible(ctx, user) && user!.IsHost;
}
