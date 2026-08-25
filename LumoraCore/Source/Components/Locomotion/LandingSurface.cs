// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Marks this slot's colliders as a hop destination. BlinkLocomotion looks for this on the hit slot
// and its parents, so putting one on a prop's root tags every collider under it at once.
//
// This OVERRIDES the slope test: a surface tagged here accepts a landing no matter which way its
// normal points, which is how a moving platform, a ladder top or a hand-authored perch becomes
// standable without shipping a flat collider for it. Put it on geometry you WANT people to arrive on,
// not on scenery that merely happens to be walkable - untagged geometry still passes on slope alone.
// - xlinka
[ComponentCategory("Locomotion/Tagging")]
[SingleInstancePerSlot]
public sealed class LandingSurface : Component
{
}
