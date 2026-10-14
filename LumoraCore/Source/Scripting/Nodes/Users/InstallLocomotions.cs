// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Gives the user at this machine a set of locomotions: a copy of the slot given, with every locomotion in it,
// goes under their own, in their picker by its slot's name. Those installed before go first when clear is
// set. Says whether anything came in, and fails when nothing did. Anyone else gets nothing.
[Node("Users/Install Locomotions", Capability.UsersAffect)]
public sealed class InstallLocomotions : ActionNode
{
    [return: Port("installed")]
    public static bool Run(ScriptContext ctx, Slot? from, User? user, bool clear = false)
    {
        if (!ScriptReads.HereFor(ctx, user) || !ScriptReads.Reached(ctx, from, "source"))
            return false;
        if (user.Root?.Slot?.GetComponent<LocomotionController>() is not { IsDestroyed: false } controller)
        {
            ctx.Fail("the user has no locomotion here");
            return false;
        }
        bool installed;
        using (ctx.AsSelf(user))
            installed = controller.Install(from, clear) > 0;
        if (!installed)
            ctx.Fail($"{ScriptReads.Named(from)} holds no locomotion to install");
        return installed;
    }
}
