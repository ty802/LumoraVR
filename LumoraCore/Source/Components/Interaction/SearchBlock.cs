// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// stops the laser's parent-chain interaction-target walk at this slot. used to
// scope which targets a click can reach when walking up from the hit slot. - xlinka
[ComponentCategory("Interaction")]
[SingleInstancePerSlot]
public sealed class SearchBlock : Component
{
}
