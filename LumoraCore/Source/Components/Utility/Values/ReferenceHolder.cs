// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// A bare synchronized reference with nothing attached to it. See ValueHolder.
[ComponentCategory("Utility/Values")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceHolder<T> : Component where T : class, IWorldElement
{
    public readonly SyncRef<T> Reference;

    public ReferenceHolder()
    {
        Reference = new SyncRef<T>(this);
    }
}
