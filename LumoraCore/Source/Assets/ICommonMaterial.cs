// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>
/// Interface for materials with common properties.
/// Allows generic material manipulation regardless of material type.
/// </summary>
public interface ICommonMaterial
{
    /// <summary>
    /// The primary color of the material (albedo/tint).
    /// </summary>
    colorHDR Color { get; set; }

    /// <summary>
    /// The primary texture of the material.
    /// </summary>
    IAssetProvider<TextureAsset> MainTexture { get; set; }
}