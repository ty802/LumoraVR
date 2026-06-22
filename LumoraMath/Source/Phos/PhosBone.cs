// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Phos;

/// <summary>
/// A single skeleton bone in a PhosMesh: its name plus its bind pose (the rest-pose transform used to
/// skin vertices). Per-vertex PhosBoneBinding entries index into the owning mesh's bone list, so this
/// table is what makes a skinned mesh self-describing as an asset. - xlinka
/// </summary>
public class PhosBone
{
    /// <summary>Bone name (matches the rig joint this drives).</summary>
    public string Name { get; set; }

    /// <summary>Bind pose (rest-pose) transform for this bone.</summary>
    public float4x4 BindPose { get; set; }

    public PhosBone(string name)
    {
        Name = name;
        BindPose = float4x4.Identity;
    }
}
