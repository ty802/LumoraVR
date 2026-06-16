// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

/// <summary>
/// Base interface for asset hooks that bridge C# assets to engine-specific implementations.
/// </summary>
public interface IAssetHook
{
    /// <summary>
    /// Initialize the hook with an asset instance.
    /// </summary>
    void Initialize(IAsset asset);

    /// <summary>
    /// Unload/dispose the hook and its resources.
    /// </summary>
    void Unload();
}

/// <summary>
/// Hook interface for texture assets.
/// </summary>
public interface ITextureAssetHook : IAssetHook
{
    /// <summary>
    /// Upload texture data to the renderer.
    /// </summary>
    void UploadData(byte[] pixels, int width, int height, bool hasMipmaps);

    /// <summary>
    /// Update texture wrap modes.
    /// </summary>
    void SetWrapMode(TextureWrapMode wrapU, TextureWrapMode wrapV);

    /// <summary>
    /// Whether the texture is valid and can be used.
    /// </summary>
    bool IsValid { get; }
}

public interface IRenderTextureAssetHook : ITextureAssetHook
{
    void Configure(
        int width,
        int height,
        int cullMask,
        Math.color clearColor,
        Math.float3 cameraPosition,
        Math.floatQ cameraRotation,
        float orthographicSize);

    /// <summary>
    /// Pause or resume offscreen rendering without tearing the viewport down.
    /// </summary>
    void SetRenderEnabled(bool enabled);
}

/// <summary>
/// Hook interface for mesh data assets.
/// </summary>
public interface IMeshAssetHook : IAssetHook
{
    /// <summary>
    /// Upload mesh data to the renderer.
    /// </summary>
    void UploadMesh(Phos.PhosMesh mesh);

    /// <summary>
    /// Whether the mesh is valid and can be used.
    /// </summary>
    bool IsValid { get; }
}

/// <summary>
/// Hook interface for material assets - bridges to Godot ShaderMaterial/StandardMaterial3D.
/// </summary>
public interface IMaterialAssetHook : IAssetHook
{
    /// <summary>
    /// Set the material type/shader.
    /// </summary>
    void SetMaterialType(MaterialType type);

    /// <summary>
    /// Set blend mode (Opaque, Cutout, Transparent, Additive).
    /// </summary>
    void SetBlendMode(BlendMode mode);

    /// <summary>
    /// Set face culling mode.
    /// </summary>
    void SetCulling(Culling culling);

    /// <summary>
    /// Set a float property.
    /// </summary>
    void SetFloat(string property, float value);

    /// <summary>
    /// Set an int property.
    /// </summary>
    void SetInt(string property, int value);

    /// <summary>
    /// Set a bool property.
    /// </summary>
    void SetBool(string property, bool value);

    /// <summary>
    /// Set a color/float4 property.
    /// </summary>
    void SetColor(string property, Math.colorHDR value);

    /// <summary>
    /// Set a float2 property (e.g., texture scale/offset).
    /// </summary>
    void SetFloat2(string property, Math.float2 value);

    /// <summary>
    /// Set a float3 property.
    /// </summary>
    void SetFloat3(string property, Math.float3 value);

    /// <summary>
    /// Set a float4 property.
    /// </summary>
    void SetFloat4(string property, Math.float4 value);

    /// <summary>
    /// Set a texture property.
    /// </summary>
    void SetTexture(string property, TextureAsset texture);

    /// <summary>
    /// Set a custom shader path (for Custom material type).
    /// </summary>
    void SetCustomShader(string shaderPath);

    /// <summary>
    /// Set a custom shader source code string.
    /// </summary>
    void SetCustomShaderSource(string shaderSource);

    /// <summary>
    /// Clear all properties (called before UpdateMaterial).
    /// </summary>
    void Clear();

    /// <summary>
    /// Apply all pending changes.
    /// </summary>
    void ApplyChanges(Action callback);

    /// <summary>
    /// Get the underlying Godot material for assignment to renderers.
    /// </summary>
    object GodotMaterial { get; }

    /// <summary>
    /// Renderer queue requested by the material provider (-1 = default queue).
    /// </summary>
    int RenderQueue { get; }

    /// <summary>
    /// Whether the material is valid and ready for use.
    /// </summary>
    bool IsValid { get; }
}

public interface IFontAssetHook : IAssetHook
{
    bool IsValid { get; }

    // engine-side texture asset wrapping the font's glyph atlas - xlinka
    TextureAsset? AtlasTexture { get; }

    // load a font from a file path. format detected from extension (.ttf/.otf/.woff). - xlinka
    void LoadFromFile(string path);

    // return false if the codepoint isn't rasterized at this size (caller may request and retry) - xlinka
    bool TryGetGlyph(int codepoint, float size, out GlyphMetrics metrics, out Math.Rect uvRect);

    // ensure the given codepoint is in the atlas at the given size. async-friendly. - xlinka
    void RequestGlyph(int codepoint, float size);

    float GetLineHeight(float size);
    float GetAscent(float size);
    float GetDescent(float size);
    float GetKerning(int leftCodepoint, int rightCodepoint, float size);

    // MSDF reconstruction parameter - pixels of distance encoded around each glyph edge.
    // The shader uses this to scale signed distance into screen-space pixel units. - xlinka
    int PixelRange { get; }

    // Bumped whenever a glyph entry is added to the atlas. The atlas never repacks, so a
    // shaped run stays valid until this changes - text shaping caches against it. - xlinka
    int CacheGeneration { get; }
}

public interface IMaterialPropertyBlockAssetHook : IAssetHook
{
    void SetFloat(string property, float value);
    void SetInt(string property, int value);
    void SetBool(string property, bool value);
    void SetColor(string property, Math.colorHDR value);
    void SetFloat2(string property, Math.float2 value);
    void SetFloat3(string property, Math.float3 value);
    void SetFloat4(string property, Math.float4 value);
    void SetTexture(string property, TextureAsset texture);
    void Clear();
    void ApplyChanges(Action callback);
    object ApplyToMaterial(object baseMaterial, MaterialType materialType);
    bool IsValid { get; }
}

/// <summary>
/// Texture wrap modes for sampling.
/// </summary>
public enum TextureWrapMode
{
    Repeat,
    Clamp,
    Mirror,
    ClampToBorder
}
