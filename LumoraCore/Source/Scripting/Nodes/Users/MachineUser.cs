// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// The user on the machine running the script. A Shared script runs on the host, so for one this is the
// host; a Local script runs on every machine, so for one it is whoever is at this one.
[Node("Users/Local User", Capability.UsersRead)]
public sealed class MachineUser : ReadNode
{
    [return: Port("user")]
    public static User? Run(ScriptContext ctx)
    {
        var user = ctx.World.LocalUser;
        return ScriptReads.Visible(ctx, user) ? user : null;
    }
}
