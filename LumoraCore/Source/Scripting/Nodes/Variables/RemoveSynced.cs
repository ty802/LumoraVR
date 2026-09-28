// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Drops a synced value this script keeps, freeing its place under the object's limit. Shared scripts only.
[Node("Variables/Remove Synced", Capability.Events)]
public sealed class RemoveSynced : ActionNode
{
    public static void Run(ScriptContext ctx, string? name)
    {
        if (ctx.IsLocal)
            throw new UnauthorizedAccessException("a Local script reads synced values; a Shared script keeps them");
        if (string.IsNullOrEmpty(name) || ctx.Host is not { IsDestroyed: false } host || !ctx.World.IsAuthority)
            return;
        host.RemoveSynced(name);
    }
}
