// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

[Node("Users/Name", Capability.UsersRead)]
public sealed class UserName : ReadNode
{
    [return: Port("name")]
    public static string Run(ScriptContext ctx, User? user)
        => ScriptReads.Visible(ctx, user) ? user!.UserName.Value ?? string.Empty : string.Empty;
}
