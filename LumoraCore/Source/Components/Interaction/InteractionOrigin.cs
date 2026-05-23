// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// optional per-frame override for laser origin/direction plus interaction flags.
// lets a tool drive a laser from a non-default position (e.g. through a mirror or
// pinned to a target) without moving the laser slot. - xlinka
public readonly struct InteractionOrigin
{
    public readonly float3 Origin;
    public readonly float3 Direction;
    public readonly float IgnoreDistance;
    public readonly float MaxDistance;
    public readonly float SelfIgnoreDistance;
    public readonly bool SmoothDistance;
    public readonly bool ShowOnlyOnInteraction;
    public readonly bool StickyHits;
    public readonly bool PrimaryInteraction;
    public readonly bool SecondaryInteraction;
    public readonly bool ForceClickActivation;

    public InteractionOrigin(
        in float3 origin,
        in float3 direction,
        float ignoreDistance,
        bool primaryInteraction,
        bool secondaryInteraction,
        float maxDistance = float.PositiveInfinity,
        float selfIgnoreDistance = 0f,
        bool smoothDistance = false,
        bool showOnlyOnInteraction = false,
        bool stickyHits = false,
        bool forceClickActivation = false)
    {
        Origin = origin;
        Direction = direction;
        IgnoreDistance = ignoreDistance;
        MaxDistance = maxDistance;
        SelfIgnoreDistance = selfIgnoreDistance;
        SmoothDistance = smoothDistance;
        ShowOnlyOnInteraction = showOnlyOnInteraction;
        StickyHits = stickyHits;
        PrimaryInteraction = primaryInteraction;
        SecondaryInteraction = secondaryInteraction;
        ForceClickActivation = forceClickActivation;
    }
}
