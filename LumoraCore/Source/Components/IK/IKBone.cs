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
    public readonly SyncRef<Slot> TransformTarget = new();

    /// <summary>
    /// Bone radius for collision/visualization.
    /// </summary>
    public readonly Sync<float> Radius = new();

    /// <summary>
    /// Bone length.
    /// </summary>
    public readonly Sync<float> Height = new();

    /// <summary>
    /// Whether this bone is fixed in place.
    /// </summary>
    public readonly Sync<bool> Pinned = new();

    public override void OnInit()
    {
        base.OnInit();
        Radius.Value = 0.01f;
        Height.Value = 0.1f;
        // Pinned = false (C# default, skip)
    }
}
