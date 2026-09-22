// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

// Turns the walking animation off for as long as it exists.
//
// A body that is sitting, driving, strapped into something or being posed by somebody else should not
// be swinging its arms and bobbing its head as though it were out for a walk. The animation has no
// way to know any of that - it can see speed and it can see the floor, and a seat that moves looks
// exactly like walking to both.
//
// So the thing that knows says so. Attach one to the user root (a seat does this when somebody sits,
// and removes it when they get up) and the arm simulation and the head bob stop; the IK itself, the
// tracked limbs and the procedural feet carry on, because those are not animation, they are where the
// body is. -xlinka
[ComponentCategory("Users/Avatar")]
public sealed class LocomotionAnimationSuppressor : UserRootComponent
{
    // The arm simulation and the head bob.
    public readonly Sync<bool> SuppressHandMotion;

    // The procedural feet. Off by default: a seat wants the arms still and usually still wants the
    // legs to reach for the floor.
    public readonly Sync<bool> SuppressFeet;

    public LocomotionAnimationSuppressor()
    {
        SuppressHandMotion = new Sync<bool>(this, true);
        SuppressFeet = new Sync<bool>(this, false);
    }

    // Is anything on this user root asking for quiet right now?
    public static bool IsSuppressed(Slot? slot, out bool feet)
    {
        feet = false;
        var root = slot?.ActiveUserRoot?.Slot;
        if (root == null || root.IsDestroyed)
            return false;

        bool hands = false;
        foreach (var suppressor in root.GetComponentsInChildren<LocomotionAnimationSuppressor>())
        {
            if (suppressor.IsDestroyed || !suppressor.Enabled.Value || suppressor.Slot?.IsActive != true)
                continue;
            hands |= suppressor.SuppressHandMotion.Value;
            feet |= suppressor.SuppressFeet.Value;
        }
        return hands;
    }
}
