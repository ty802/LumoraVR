// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
namespace Lumora.Core.Components;

// The order a builder dragged a slot's components into in the inspector. Presentation only: nothing
// outside the inspector reads it, so update order stays whatever attach order made it. Saved with the
// object so the arrangement survives.
[HideInInspector]
[ComponentCategory("Hidden")]
public sealed class InspectorComponentOrder : Component
{
    public readonly SyncRefList<Component> Items = new();
}
