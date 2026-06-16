// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// stops the grabber's parent-chain walk at this slot. attach to a slot whose
// children should be grabbable but the slot itself is not. - xlinka
[ComponentCategory("Interaction")]
[SingleInstancePerSlot]
public sealed class GrabBlock : Component
{
}
