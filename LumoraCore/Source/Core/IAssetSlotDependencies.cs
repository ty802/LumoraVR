// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core;

// An asset that is nothing without slots of its own. When a saved object takes it along from outside, as
// it does a material it wears, these slots are saved whole beside the object as its dependencies, so the
// asset loads with them and with every reference between the two intact. -xlinka
public interface IAssetSlotDependencies
{
    IEnumerable<Slot> SlotDependencies { get; }
}
