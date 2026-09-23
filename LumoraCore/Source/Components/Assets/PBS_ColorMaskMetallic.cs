// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Metallic PBS with FOUR tint slots picked by a mask texture's channels.
//
// One greyscale-ish mask paints which of four colours each part of a surface takes: red channel gets
// AlbedoColor0, green gets 1, blue gets 2, alpha gets 3, and the four are summed and normalised by
// their own total so a pixel with two channels lit lands halfway between those two colours rather
// than twice as bright. The albedo texture then multiplies the result, so the same greyscale art can
// be recoloured four ways without a second texture.
//
// Emission runs the same four-way pick on its own colour set through the same mask, which is how a
// single decal sheet lights up in one colour and stays dark in another.
//
// The mask has its own scale and offset, separate from the albedo's. They are usually laid out on
// different UV islands and tying them together would move both. -xlinka
[ComponentCategory("Assets/Materials")]
public class PBS_ColorMaskMetallic : MaterialProvider, ICommonMaterial
{
    // ALBEDO

    public readonly Sync<colorHDR> AlbedoColor0;
    public readonly Sync<colorHDR> AlbedoColor1;
    public readonly Sync<colorHDR> AlbedoColor2;
    public readonly Sync<colorHDR> AlbedoColor3;

    public readonly AssetRef<TextureAsset> AlbedoTexture;

    // THE MASK. Which of the four colours each pixel takes, one per channel.
    public readonly AssetRef<TextureAsset> ColorMask;

    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;

    // The mask's own transform. See the note above for why it is not the albedo's.
    public readonly Sync<float2> ColorMaskScale;
    public readonly Sync<float2> ColorMaskOffset;

    // SURFACE

    public readonly Sync<float> Metallic;
    public readonly Sync<float> Smoothness;

    // NOT glTF's PACKING: red is metallic and ALPHA is smoothness. The rest of our PBS materials read
    // green/blue because that is what a glTF ships, but the maps authored for this material are written
    // this way. Reading it as glTF puts roughness in the wrong channel and the surface comes out
    // uniformly shiny. -xlinka
    public readonly AssetRef<TextureAsset> MetallicMap;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float> NormalScale;

    public readonly AssetRef<TextureAsset> OcclusionMap;

    // EMISSION, four-way through the same mask.

    public readonly Sync<colorHDR> EmissiveColor0;
    public readonly Sync<colorHDR> EmissiveColor1;
    public readonly Sync<colorHDR> EmissiveColor2;
    public readonly Sync<colorHDR> EmissiveColor3;

    public readonly AssetRef<TextureAsset> EmissiveMap;

    // RENDERING

    public readonly Sync<bool> Transparent;

    // Draw into the depth buffer even when transparent. A transparent surface normally skips it so
    // what is behind still shows; a visor that should occlude its own inside wants it back.
    public readonly Sync<bool> ForceZWrite;

    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.PBS_ColorMask;

    // ICommonMaterial. Slot 0 is the base tint, which is what anything asking for "the colour" means.

    public colorHDR Color
    {
        get => AlbedoColor0.Value;
        set => AlbedoColor0.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => AlbedoTexture.Target;
        set => AlbedoTexture.Target = value;
    }

    public PBS_ColorMaskMetallic()
    {
        // Red, green, blue, white, so an unmasked material reads as an obvious test pattern rather
        // than as a broken white one.
        AlbedoColor0 = new Sync<colorHDR>(this, new colorHDR(1f, 0f, 0f, 1f));
        AlbedoColor1 = new Sync<colorHDR>(this, new colorHDR(0f, 1f, 0f, 1f));
        AlbedoColor2 = new Sync<colorHDR>(this, new colorHDR(0f, 0f, 1f, 1f));
        AlbedoColor3 = new Sync<colorHDR>(this, colorHDR.White);

        AlbedoTexture = new AssetRef<TextureAsset>(this);
        ColorMask = new AssetRef<TextureAsset>(this);

        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        ColorMaskScale = new Sync<float2>(this, float2.One);
        ColorMaskOffset = new Sync<float2>(this, float2.Zero);

        Metallic = new Sync<float>(this, 0f);
        Smoothness = new Sync<float>(this, 0.25f);
        MetallicMap = new AssetRef<TextureAsset>(this);

        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);

        OcclusionMap = new AssetRef<TextureAsset>(this);

        EmissiveColor0 = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveColor1 = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveColor2 = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveColor3 = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveMap = new AssetRef<TextureAsset>(this);

        Transparent = new Sync<bool>(this, false);
        ForceZWrite = new Sync<bool>(this, false);

        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetColor("AlbedoColor0", AlbedoColor0.Value);
        asset.SetColor("AlbedoColor1", AlbedoColor1.Value);
        asset.SetColor("AlbedoColor2", AlbedoColor2.Value);
        asset.SetColor("AlbedoColor3", AlbedoColor3.Value);
        asset.SetTexture("AlbedoTexture", AlbedoTexture.Asset);
        asset.SetTexture("ColorMask", ColorMask.Asset);

        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);
        asset.SetFloat2("ColorMaskScale", ColorMaskScale.Value);
        asset.SetFloat2("ColorMaskOffset", ColorMaskOffset.Value);

        asset.SetFloat("Metallic", Metallic.Value);
        asset.SetFloat("Smoothness", Smoothness.Value);
        asset.SetTexture("MetallicMap", MetallicMap.Asset);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetTexture("OcclusionMap", OcclusionMap.Asset);

        asset.SetColor("EmissiveColor0", EmissiveColor0.Value);
        asset.SetColor("EmissiveColor1", EmissiveColor1.Value);
        asset.SetColor("EmissiveColor2", EmissiveColor2.Value);
        asset.SetColor("EmissiveColor3", EmissiveColor3.Value);
        asset.SetTexture("EmissiveMap", EmissiveMap.Asset);

        asset.SetBlendMode(Transparent.Value ? BlendMode.Alpha : BlendMode.Opaque);
        asset.SetFloat("ForceZWrite", ForceZWrite.Value ? 1f : 0f);
        asset.SetFloat("OffsetFactor", OffsetFactor.Value);
        asset.SetFloat("OffsetUnits", OffsetUnits.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);
    }
}
