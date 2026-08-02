// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components.Interaction;

// A UI element that can consume a held reference card: releasing the grip over a reference field
// offers everything in the hand, and the field assigns the first compatible reference.
public interface IProxyReceiver
{
    // true when one was consumed
    bool TryReceiveProxy(IReadOnlyList<IGrabbable> held, Grabber grabber);
}
