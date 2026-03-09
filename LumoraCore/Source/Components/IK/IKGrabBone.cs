// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.IK;

/// <summary>
/// Makes an IK bone grabbable by users for interaction.
/// </summary>
[ComponentCategory("IK")]
public class IKGrabBone : Component
{
    /// <summary>
    /// Whether grabbing this bone also grabs child bones.
    /// </summary>
    public Sync<bool> GrabChildren { get; private set; }

    /// <summary>
    /// Maximum force applied when grabbed.
    /// </summary>
    public Sync<float> MaximumForce { get; private set; }

    /// <summary>
    /// How rigid the bone is when grabbed.
    /// </summary>
    public Sync<float> Rigidity { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        GrabChildren = new Sync<bool>(this, true);
        MaximumForce = new Sync<float>(this, 100.0f);
        Rigidity = new Sync<float>(this, 1.0f);
    }
}
