// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// A held object that runs its own action when the user presses primary while carrying it. The hand
// tool offers the press to this before its own align gesture, so a reference card can open its
// target instead of the object aligning to an axis.
public interface IHeldActivatable
{
    // true when handled
    bool OnHeldActivate(Grabber grabber);
}
