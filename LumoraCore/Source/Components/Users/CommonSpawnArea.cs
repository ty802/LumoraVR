// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Defines where users spawn: a point generator picks the spot (this slot's position when none),
/// re-rolled up to a few times if another user is already standing there. SimpleUserSpawn consults
/// the world's spawn area when placing a new user's root.
/// </summary>
[ComponentCategory("Users")]
public class CommonSpawnArea : Component
{
    private const int MaxCollisionRetries = 20;

    public readonly SyncRef<IPointGenerator> SpawnPointGenerator;

    /// <summary>Radius around a candidate point that must be free of other users (0 disables).</summary>
    public readonly Sync<float> OtherUserCheckRadius;

    /// <summary>Spawned users take this slot's facing.</summary>
    public readonly Sync<bool> OrientUser;

    /// <summary>Maximum users this area accepts; negative = unlimited.</summary>
    public readonly Sync<int> Capacity;

    public CommonSpawnArea()
    {
        SpawnPointGenerator = new SyncRef<IPointGenerator>(this);
        OtherUserCheckRadius = new Sync<float>(this, 0.5f);
        OrientUser = new Sync<bool>(this, true);
        Capacity = new Sync<int>(this, -1);
    }

    public bool CanSpawnUser()
    {
        if (!Enabled)
            return false;
        if (Capacity.Value >= 0 && World.GetAllUsers().Count > Capacity.Value)
            return false;
        return true;
    }

    /// <summary>Pick a world-space spawn point, avoiding spots occupied by other users.</summary>
    public float3 PickSpawnPoint()
    {
        float checkRadius = OtherUserCheckRadius.Value;
        float3 point = GeneratePoint();
        if (checkRadius <= 0f)
            return point;

        var hits = new List<Slot>();
        for (int attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            hits.Clear();
            World.Physics.OverlapSphere(point + new float3(0f, checkRadius, 0f), checkRadius, hits);
            if (!AnyUserIn(hits))
                return point;
            point = GeneratePoint();
        }
        return point;
    }

    public floatQ SpawnRotation => Slot.GlobalRotation;

    private float3 GeneratePoint()
        => SpawnPointGenerator.Target?.GeneratePoint() ?? Slot.GlobalPosition;

    private static bool AnyUserIn(List<Slot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            for (var s = slots[i]; s != null; s = s.Parent)
            {
                if (s.GetComponent<UserRoot>() != null)
                    return true;
            }
        }
        return false;
    }
}
