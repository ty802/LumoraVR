// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

// Triggers own the INTERACTION, never the seating itself - Seat.TrySit / Seat.Release stay the single
// path in and out, so a new trigger kind cannot invent its own half of the transform bookkeeping and
// drift from the rest. - xlinka
public interface ISeatTrigger
{
    Seat? Seat { get; }

    // false when the seat refused (occupied, filtered, not local)
    bool TrySit(User user);

    // false when there was nothing to release
    bool Release();
}
