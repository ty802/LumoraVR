// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

// Where an item sits on the root ring, clockwise from nine o'clock in this order. Items of one group keep
// the order their sources added them in. -xlinka
public enum ContextMenuGroup
{
    // What the hand is holding: destroy, duplicate, save, and whatever the held things offer.
    Held,
    // Equipping or unequipping the tool.
    Equip,
    // Undo and redo.
    History,
    // The user's own movement and size.
    Self,
    // The equipped tool's own actions.
    Tool,
    // Anything else: the world's items and what the pointer is on.
    Content,
}
