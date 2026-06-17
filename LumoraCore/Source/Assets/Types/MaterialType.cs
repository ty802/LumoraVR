// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// Material types supported by the system.
/// Maps to shader configurations in the Godot hook layer.
/// </summary>
public enum MaterialType
{
    /// <summary>
    /// Physically-Based Rendering with metallic workflow.
    /// Uses StandardMaterial3D in Godot.
    /// </summary>
    PBS_Metallic,

    /// <summary>
    /// Simple unlit material - no lighting calculations.
    /// Uses ShaderMaterial with custom shader in Godot.
    /// </summary>
    Unlit,

    OverlayUnlit,

    UI_Unlit,

    /// <summary>
    /// Dedicated UI text material - rasterized coverage atlas + fwidth-based AA.
    /// </summary>
    UI_Text,

    /// <summary>
    /// World-space text material (nameplates, labels) - same glyph coverage
    /// path as UI_Text but depth-tested so text occludes behind geometry.
    /// </summary>
    Text,

    /// <summary>
    /// Custom shader material - uses user-provided .gdshader file.
    /// </summary>
    Custom,

    /// <summary>
    /// Ray-marched metaball blobs rising from a surface. Vibrant gradient with rim/fresnel.
    /// </summary>
    Metaball,

    /// <summary>
    /// Built-in grid ground material.
    /// </summary>
    GridSpaceGround,

    /// <summary>
    /// LocalHome-specific rising orb volume material. Not a generic shader.
    /// </summary>
    LocalHomeRising,

    /// <summary>
    /// Frosted-glass backdrop for modal overlays: samples + blurs/pixelates the screen behind it.
    /// </summary>
    Blur
}

/// <summary>
/// Blend modes for material transparency.
/// </summary>
public enum BlendMode
{
    /// <summary>
    /// Fully opaque - no transparency.
    /// </summary>
    Opaque,

    /// <summary>
    /// Alpha cutout - pixels are either fully opaque or fully transparent.
    /// Uses alpha threshold (AlphaCutoff).
    /// </summary>
    Cutout,

    /// <summary>
    /// Alpha blending - smooth transparency.
    /// </summary>
    Alpha,

    /// <summary>
    /// Alpha blending - smooth transparency.
    /// </summary>
    Transparent,

    /// <summary>
    /// Additive blending - adds to background color.
    /// Used for glow effects.
    /// </summary>
    Additive,

    Multiply
}

/// <summary>
/// Face culling modes for materials.
/// </summary>
public enum Culling
{
    /// <summary>
    /// Cull back faces (default) - only front faces visible.
    /// </summary>
    Back,

    /// <summary>
    /// Cull front faces - only back faces visible.
    /// </summary>
    Front,

    /// <summary>
    /// No culling - both sides visible (double-sided).
    /// </summary>
    None
}

public enum ZWrite
{
    Auto,
    Off,
    On
}

public enum ZTest
{
    Disabled = 0,
    Never = 1,
    Less = 2,
    Equal = 3,
    LessOrEqual = 4,
    Greater = 5,
    NotEqual = 6,
    GreaterOrEqual = 7,
    Always = 8
}

[System.Flags]
public enum ColorMask
{
    None = 0,
    R = 1,
    G = 2,
    B = 4,
    A = 8,
    RGB = R | G | B,
    RGBA = R | G | B | A
}

public enum StencilComparison
{
    Disabled = 0,
    Never = 1,
    Less = 2,
    Equal = 3,
    LessOrEqual = 4,
    Greater = 5,
    NotEqual = 6,
    GreaterOrEqual = 7,
    Always = 8
}

public enum StencilOperation
{
    Keep = 0,
    Zero = 1,
    Replace = 2,
    IncrementSaturate = 3,
    DecrementSaturate = 4,
    Invert = 5,
    IncrementWrap = 6,
    DecrementWrap = 7
}
