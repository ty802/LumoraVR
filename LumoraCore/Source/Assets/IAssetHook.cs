// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

public interface IAssetHook
{
    void Initialize(IAsset asset);

    void Unload();
}

public interface ITextureAssetHook : IAssetHook
{
    void UploadData(byte[] pixels, int width, int height, bool hasMipmaps);

    void SetWrapMode(TextureWrapMode wrapU, TextureWrapMode wrapV);

    bool IsValid { get; }

    // Completes once the most recent UploadData has actually built the GPU texture - UploadData
    // itself only QUEUES a deferred (main-thread) build. The asset awaits this before reporting FullyLoaded so a
    // consuming material never binds the texture while IsValid is still false (the white-body race).
    System.Threading.Tasks.Task WaitForUploadAsync();

    // Upload a texture the renderer is allowed to make decisions about: a pre-built mip chain
    // instead of a single level, and permission to block-compress if the device supports it.
    //
    // The split between this and UploadData is deliberate. The engine cannot know
    // which compressed formats the running GPU accepts, so it never picks one; it states intent
    // (compressible or not, normal map or not, sRGB or not) and the renderer decides. Whatever the
    // renderer ends up building it reports back through Report,
    // so the format and VRAM figures the inspector shows are observed rather than assumed.
    //
    // Default implementation uploads the base level the old way, which keeps every renderer that
    // has not implemented this compiling and correct, just without compression. -xlinka
    void UploadTexture(TextureUploadRequest request)
    {
        var levels = request.MipLevels;
        if (levels == null || levels.Length == 0)
            return;
        UploadData(levels[0], request.Width, request.Height, request.GenerateMipmaps || levels.Length > 1);
    }
}

public sealed class TextureUploadRequest
{
    public byte[][] MipLevels { get; init; } = System.Array.Empty<byte[]>();

    public int Width { get; init; }
    public int Height { get; init; }

    public bool GenerateMipmaps { get; init; }

    public bool AllowBlockCompression { get; init; }

    // Measured from the pixel scan, so the renderer can pick a format without alpha.
    public bool HasAlpha { get; init; }

    // Set only when the importer or user marked it; drives normal-map-aware compression.
    public bool IsNormalMap { get; init; }

    public bool? SRgb { get; init; }

    // Same key means same pixels and same compression intent. Null skips the cache.
    public string? CacheKey { get; init; }

    // Null disables the cache.
    public string? CacheDirectory { get; init; }

    // The renderer reports the format it actually built, the mip count it holds and the resident
    // bytes. Never a guess.
    public System.Action<TextureFormatKind, int, long>? Report { get; init; }
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

    void SetRenderEnabled(bool enabled);

    // Render exactly one frame now, then go idle again (keeping the last frame). Used for render-on-change:
    // the UI viewport only re-renders when its captured content actually changed, instead of every frame.
    void RequestRender();
}

public interface IMeshAssetHook : IAssetHook
{
    void UploadMesh(Phos.PhosMesh mesh);

    bool IsValid { get; }
}

public interface IMaterialAssetHook : IAssetHook
{
    void SetMaterialType(MaterialType type);

    void SetBlendMode(BlendMode mode);

    void SetCulling(Culling culling);

    void SetFloat(string property, float value);

    void SetInt(string property, int value);

    void SetBool(string property, bool value);

    void SetColor(string property, Math.colorHDR value);

    void SetFloat2(string property, Math.float2 value);

    // Set a float2 property AND flush just that one to the live material immediately (no full ApplyChanges,
    // which re-pushes every property). For per-frame hot paths like scroll clip_offset. -xlinka
    void ApplyFloat2Now(string property, Math.float2 value);

    void SetFloat3(string property, Math.float3 value);

    void SetFloat4(string property, Math.float4 value);

    void SetTexture(string property, TextureAsset texture);

    void SetCustomShader(string shaderPath);

    void SetCustomShaderSource(string shaderSource);

    // Called before UpdateMaterial.
    void Clear();

    void ApplyChanges(Action callback);

    object GodotMaterial { get; }

    // -1 = default queue.
    int RenderQueue { get; }

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

public enum TextureWrapMode
{
    Repeat,
    Clamp,
    Mirror,
    ClampToBorder
}
