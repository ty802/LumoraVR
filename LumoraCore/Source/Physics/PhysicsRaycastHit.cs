// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Physics;

/// <summary>
/// Result of a collision query against a world's physics bodies.
/// </summary>
public struct PhysicsRaycastHit
{
    /// <summary>The slot whose collider was struck, or null if it couldn't be resolved.</summary>
    public Slot? Slot;

    /// <summary>World-space contact point.</summary>
    public float3 Point;

    /// <summary>World-space surface normal at the contact.</summary>
    public float3 Normal;

    /// <summary>Distance from the query origin to the contact.</summary>
    public float Distance;
}
