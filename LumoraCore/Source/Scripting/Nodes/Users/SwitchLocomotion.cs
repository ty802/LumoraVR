// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Moves the user at this machine to the locomotion whose name is the one given, or, unless exact, the first
// whose name holds it. Says whether it switched: no such locomotion, one the world does not allow, or anyone
// else is a no. Each of those fails as well, except anyone else for a Local script, whose copy on that
// person's machine switches them.
[Node("Users/Switch Locomotion", Capability.UsersAffect)]
public sealed class SwitchLocomotion : ActionNode
{
    [return: Port("switched")]
    public static bool Run(ScriptContext ctx, User? user, string name, bool exact = false)
    {
        if (!ScriptReads.HereFor(ctx, user))
            return false;
        if (string.IsNullOrEmpty(name))
        {
            ctx.Fail("no locomotion name");
            return false;
        }
        if (user.Root?.Slot is not { IsDestroyed: false } body || body.GetComponent<LocomotionController>() is not { IsDestroyed: false } controller)
        {
            ctx.Fail("the user has no locomotion here");
            return false;
        }
        foreach (var module in controller.Modules)
        {
            if (module == null || module.IsDestroyed || string.IsNullOrEmpty(module.DisplayName))
                continue;
            bool match = exact ? module.DisplayName == name : module.DisplayName.Contains(name, System.StringComparison.OrdinalIgnoreCase);
            if (!match)
                continue;
            using (ctx.AsSelf(user))
                controller.ActivateModule(module);
            if (ReferenceEquals(controller.ActiveModule, module))
                return true;
            ctx.Fail($"'{module.DisplayName}' is not allowed here");
            return false;
        }
        ctx.Fail($"no locomotion named '{name}'");
        return false;
    }
}
