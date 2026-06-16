// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.Interaction;

// extends IInteractionTarget for things a Grabber can pick up. CanGrab/Grab/Release
// own the lifecycle; OnLocalGrabbed/Released fire only on the local user's hand. - xlinka
public interface IGrabbable : IInteractionTarget
{
    bool IsGrabbed { get; }
    bool Scalable { get; }
    bool Receivable { get; }
    bool AllowOnlyPhysicalGrab { get; }
    int GrabPriority { get; }
    Grabber? Grabber { get; }

    event Action<IGrabbable>? OnLocalGrabbed;
    event Action<IGrabbable>? OnLocalReleased;

    bool CanGrab(Grabber grabber);
    IGrabbable Grab(Grabber grabber, Slot holdSlot, bool suppressEvents = false);
    void Release(Grabber grabber, bool suppressEvents = false);
}
