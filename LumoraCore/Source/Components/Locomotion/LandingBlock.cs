// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Refuses hop landings on this slot's colliders. BlinkLocomotion looks for this on the hit slot and
// its parents, so one on a region root blocks everything under it.
//
// A blocked hit STOPS the arc rather than passing through it: a no-go volume you can arc over is not
// a no-go volume. Put a LandingSurface deeper inside a blocked branch to carve an allowed pad out of
// it - the surface tag is checked first. - xlinka
[ComponentCategory("Users/Locomotion/Tagging")]
[SingleInstancePerSlot]
public sealed class LandingBlock : Component
{
}
