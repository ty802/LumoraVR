// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Warden;

namespace Lumora.Core.Components;

// A grab surface whose grab moves the slot it sits on. The parent and pose writes land on the SLOT, so the
// slot's own gate view asks the grab component on it which of its members that grab writes: a carried
// object its parent and whole pose, a hinge only its rotation. Anything else on the slot stays owner-gated.
// -xlinka
public interface ISlotGrabSurface : IPermissionGrabSurface
{
    bool MovesSlotMember(Slot slot, IPermissionTarget member);
}
