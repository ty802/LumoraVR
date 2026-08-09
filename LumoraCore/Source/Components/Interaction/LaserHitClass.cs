// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// What the laser is allowed to do with one hit along its ray.
//
// Nearest-wins occlusion is right for the world and wrong for the controls layered on top of it. A
// translate arrow sits INSIDE the object it moves and a rotation ring can end up behind a wall, so a
// first-hit ray never sees either of them: the object is always closer. Sorting hits into three
// classes fixes that without loosening occlusion for anything else - a Prefer hit beats every nearer
// Allow hit outright, Prefer hits resolve nearest-first among themselves, and when nothing asks to be
// preferred the ordinary path runs exactly as before. -xlinka
public enum LaserHitClass
{
    // wins over any nearer Allow hit
    Prefer,

    Allow,

    // dropped as if the ray had missed it
    Ignore,
}

// An interaction target that asks to stay reachable through the geometry in front of it. Gizmo
// handles declare this so they work with a bare hand, with no tool equipped to speak for them.
public interface ILaserPreferredTarget : IInteractionTarget
{
    // false falls back to an ordinary occluded hit, so a target can drop the request when it is not
    // currently acting as a control
    bool PreferLaserHit(InteractionLaser laser);
}

// Per-tool hit filter. The equipped tool gets a say on every hit its laser resolves, so a tool can
// pull its own chrome in front of the world or drop hits it must never act on.
public interface ILaserHitClassifier
{
    // a tool may raise a hit to Prefer or drop it to Ignore; returning Allow leaves whatever the
    // target itself asked for intact
    LaserHitClass ClassifyLaserHit(InteractionLaser laser, Slot hitSlot, IInteractionTarget target);
}
