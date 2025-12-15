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
/// Texture wrap modes for sampling.
/// </summary>
public enum TextureWrapMode
{
    Repeat,
    Clamp,
    Mirror,
    ClampToBorder
}
