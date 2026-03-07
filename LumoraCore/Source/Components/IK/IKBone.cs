// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.IK;

/// <summary>
/// Represents a bone in the IK system with physical properties.
/// </summary>
[ComponentCategory("IK")]
public class IKBone : Component
{
    /// <summary>
    /// The slot this bone controls.
    /// </summary>
    public SyncRef<Slot> TransformTarget { get; private set; }

    /// <summary>
    /// Bone radius for collision/visualization.
    /// </summary>
    public Sync<float> Radius { get; private set; }

    /// <summary>
    /// Bone length.
    /// </summary>
    public Sync<float> Height { get; private set; }

    /// <summary>
    /// Whether this bone is fixed in place.
    /// </summary>
    public Sync<bool> Pinned { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        TransformTarget = new SyncRef<Slot>(this, null);
        Radius = new Sync<float>(this, 0.01f);
        Height = new Sync<float>(this, 0.1f);
        Pinned = new Sync<bool>(this, false);
    }
}
