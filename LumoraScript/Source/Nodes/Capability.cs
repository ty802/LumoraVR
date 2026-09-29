// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// What a node reaches for. Every host call a module imports carries one, and the runtime links only
// the groups the script's author and the world allow. The gate still judges every single write. Input
// is the viewer's own keys, buttons and sticks, which only a script running on their machine can read. Web
// is a request to an address outside the session, which the machine sending it asks its own user about.
public enum Capability : byte
{
    Core,
    WorldRead,
    WorldWrite,
    WorldStructure,
    UsersRead,
    UsersAffect,
    Physics,
    Audio,
    Events,
    UI,
    Input,
    Web
}
