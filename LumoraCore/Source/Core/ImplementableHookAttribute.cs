// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

// Declares which component/asset types a hook serves. Applied to a concrete
// Hook<D> or IAssetHook subclass so HookTypeRegistry / AssetHookRegistry can
// discover the mapping by reflection instead of being told via a giant
// hand-written Register() table.
//
// One hook can declare itself for several types: MeshHook serves
// ProceduralMesh, BoxMesh, QuadMesh, etc., so apply the attribute once per
// type or list them in the constructor. - xlinka
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImplementableHookAttribute : Attribute
{
    public Type[] Targets { get; }

    public ImplementableHookAttribute(params Type[] targets)
    {
        Targets = targets ?? Array.Empty<Type>();
    }
}
