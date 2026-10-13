// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Says what this slot's colliders sound like to walk on. A foot that comes down on one of them plays a
// step from the first set in Sounds that is there and switched on; the list holds one per surface of a
// multi-material floor so they can be filled or driven separately, and the first one stands for all of
// them, since a ground probe says which collider it hit and not which triangle.
//
// Costs nothing until a foot lands: it is looked up on the collider a step's probe hits, nothing here runs
// per frame, and a world with no FootstepSoundSet in it never probes at all. See FootstepPlayback. -xlinka
[ComponentCategory("Users/Locomotion/Tagging")]
public sealed class FootstepSurface : Component
{
    public readonly SyncRefList<FootstepSoundSet> Sounds;

    public FootstepSurface()
    {
        Sounds = new SyncRefList<FootstepSoundSet>(this);
    }

    public FootstepSoundSet? CurrentSet
    {
        get
        {
            for (int i = 0; i < Sounds.Count; i++)
            {
                if (Sounds[i] is { IsDestroyed: false } set && set.Enabled.Value)
                    return set;
            }
            return null;
        }
    }
}
