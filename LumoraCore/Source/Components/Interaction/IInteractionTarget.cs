// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// implemented by any Component that wants to receive laser interaction. the laser
// walks the slot's parent chain and selects the highest-priority target. - xlinka
public interface IInteractionTarget
{
    int InteractionTargetPriority { get; }

    InteractionDescription GetInteractionDescription(InteractionLaser laser);
}
