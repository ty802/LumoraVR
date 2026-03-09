// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Phos;

/// <summary>
/// UV channel data container.
/// Supports 2D, 3D, and 4D UV coordinates.
/// PhosMeshSys UV array.
/// </summary>
public struct PhosUVArray
{
    /// <summary>2D UV coordinates (most common)</summary>
    public float2[]? uv2D;

    /// <summary>3D UV coordinates (for volumetric textures)</summary>
    public float3[]? uv3D;

    /// <summary>4D UV coordinates (for special effects)</summary>
    public float4[]? uv4D;

    /// <summary>Check if this UV channel has any data</summary>
    public bool HasData => uv2D != null || uv3D != null || uv4D != null;
}
