// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Physics;

// What a rigid body tells its platform body straight away rather than through the hook queue: its object
// moved, called on the world thread for every move it hears of, so it only takes a note; and something is
// about to read where the object is (a grab), so the last physics step goes into it now.
public interface IRigidBodyHook
{
    void SlotMoved();

    void WritePoseNow();
}
