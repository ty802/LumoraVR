// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Bootstrap;

// Hooks self-declare their targets via [ImplementableHook(typeof(X))] on the
// hook class itself, so registration is just a reflection scan of the hooks
// assembly. Add overrides below only if a hook needs registration the
// attribute scheme can't express. - xlinka
public static class GodotHookRegistry
{
    public static void RegisterAll()
    {
        var hookAssembly = typeof(Lumora.Godot.Hooks.SlotHook).Assembly;

        int componentHooks = World.HookTypes.RegisterFromAssembly(hookAssembly);
        int assetHooks = AssetHookRegistry.RegisterFromAssembly(hookAssembly);

        LumoraLogger.Log($"GodotHookRegistry: registered {componentHooks} component hooks, {assetHooks} asset hooks via reflection");
    }
}
