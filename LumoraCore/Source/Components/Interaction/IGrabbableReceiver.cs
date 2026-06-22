// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// A drop target for released grabbables. On a full release, a Grabber sphere-searches for nearby
// receivers and hands each receivable object to the closest one that will take it. Implementors are
// Components that also implement this interface. - xlinka
public interface IGrabbableReceiver
{
    // Smaller distance wins. Return null to decline this object. - xlinka
    float? GetReceiveDistance(IGrabbable grabbable, Grabber grabber);

    // Take the object. Whatever reparenting/state change this does is the receiver's responsibility. - xlinka
    void Receive(IGrabbable grabbable, Grabber grabber);
}
