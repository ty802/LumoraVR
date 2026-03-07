// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

/// <summary>
/// Marks a slot as grabbable by input grabbers.
/// </summary>
[ComponentCategory("Interaction")]
public sealed class Grabbable : Component
{
    /// <summary>
    /// Whether this object can currently be grabbed.
    /// </summary>
    public Sync<bool> AllowGrab { get; private set; }

    /// <summary>
    /// Whether rotation should follow the grabber.
    /// </summary>
    public Sync<bool> FollowRotation { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        AllowGrab = new Sync<bool>(this, true);
        FollowRotation = new Sync<bool>(this, true);
    }
}