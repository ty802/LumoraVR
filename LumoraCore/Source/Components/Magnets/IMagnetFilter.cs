// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Magnets;

// A veto a socket consults before it accepts an item. Attach a component implementing this
// anywhere and reference it from MagnetSocket.Filters; every referenced filter has to say yes.
//
// A component, not a delegate list: a filter that has to run on every peer must be reconstructable
// from the save, and a component with its own fields is. Filters run inside candidate scoring, so
// keep Accept cheap and side-effect free. -xlinka
public interface IMagnetFilter : IWorldElement
{
    // False rejects the item for this socket.
    bool Accept(Magnet magnet);
}
