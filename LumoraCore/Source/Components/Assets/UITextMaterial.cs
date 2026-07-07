// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Dedicated UI text material - owns its font atlas directly via DirectTexture and uses
// the MSDF UI_Text shader. PixelRange must match the value baked into the atlas. - xlinka
[ComponentCategory("Assets/Materials/UI/Text")]
public class UITextMaterial : MaterialProvider, ICommonMaterial
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;
    public readonly Sync<colorHDR> TintColor;
    public readonly Sync<bool> UseVertexColor;
    public readonly Sync<float> PixelRange;
    // True when the bound atlas is a multi-channel signed distance field (crisp at any size); false = plain
    // coverage alpha (bitmap fonts, or a font not imported with MSDF). Set from the atlas by whoever binds it. -xlinka
    public readonly Sync<bool> UseMSDF;
    // Distance-field styling (MSDF path only). Thickness/dilate are in distance-field units: 0.5 spans the
    // whole PixelRange, so 0.2 = ~1.6 atlas texels at range 8. Dilate bolds the face by pushing its edge
    // outward; the outline is a ring of OutlineColor outside the (dilated) face. 0 = off. - xlinka
    public readonly Sync<colorHDR> OutlineColor;
    public readonly Sync<float> OutlineThickness;
    public readonly Sync<float> FaceDilate;
    // Softness widens the edge smoothing past the ~1px screen AA (glow/soft text). Underlay is a drop
    // shadow: the field re-sampled at UnderlayOffset (atlas texels) laid behind the glyph. Alpha 0 = off. -xlinka
    public readonly Sync<float> FaceSoftness;
    public readonly Sync<colorHDR> UnderlayColor;
    public readonly Sync<float2> UnderlayOffset;
    public readonly Sync<float> UnderlaySoftness;
    public readonly Sync<bool> AlphaClip;
    public readonly Sync<float> AlphaCutoff;
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;
    public readonly Sync<ZWrite> ZWrite;
    public readonly Sync<ZTest> ZTest;
    public readonly Sync<int> RenderQueue;
    public readonly Sync<Rect> Rect;
    public readonly Sync<bool> RectClip;
    public readonly Sync<float2> ClipOffset;
    public readonly Sync<ColorMask> ColorMask;
    public readonly Sync<StencilComparison> StencilComparison;
    public readonly Sync<StencilOperation> StencilOperation;
    public readonly Sync<byte> StencilID;
    public readonly Sync<byte> StencilWriteMask;
    public readonly Sync<byte> StencilReadMask;

    // Same DirectTexture override as UIUnlitMaterial - binds a transient atlas TextureAsset
    // (no owning provider component) without routing through an AssetRef. - xlinka
    public TextureAsset DirectTexture { get; set; } = null!;

    protected override MaterialType MaterialType => MaterialType.UI_Text;

    public colorHDR Color
    {
        get => TintColor.Value;
        set => TintColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => Texture.Target;
        set => Texture.Target = value;
    }

    public UITextMaterial()
    {
        Texture = new AssetRef<TextureAsset>(this);
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        TintColor = new Sync<colorHDR>(this, colorHDR.White);
        UseVertexColor = new Sync<bool>(this, true);
        PixelRange = new Sync<float>(this, 8f);
        UseMSDF = new Sync<bool>(this, false);
        OutlineColor = new Sync<colorHDR>(this, new colorHDR(0f, 0f, 0f, 1f));
        OutlineThickness = new Sync<float>(this, 0f);
        FaceDilate = new Sync<float>(this, 0f);
        FaceSoftness = new Sync<float>(this, 0f);
        UnderlayColor = new Sync<colorHDR>(this, new colorHDR(0f, 0f, 0f, 0f));
        UnderlayOffset = new Sync<float2>(this, float2.Zero);
        UnderlaySoftness = new Sync<float>(this, 0f);
        AlphaClip = new Sync<bool>(this, true);
        // SDF-style edge: smoothstep across the half-coverage threshold using fwidth
        // gives a ~1px screen-space edge at any distance/angle, instead of soft-alphaing
        // the whole glyph coverage and getting minification blur at distance. - xlinka
        AlphaCutoff = new Sync<float>(this, 0.5f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Alpha);
        Culling = new Sync<Culling>(this, Assets.Culling.None);
        ZWrite = new Sync<ZWrite>(this, Assets.ZWrite.Off);
        ZTest = new Sync<ZTest>(this, Assets.ZTest.LessOrEqual);
        // Text sits visually on top of UI quads - bump the priority so Godot's transparent
        // sort pass draws text fragments AFTER the panel/header/button backgrounds in
        // the same chunk. Backgrounds use queue 3000 via GraphicsChunk.GetDefaultUIMaterial. - xlinka
        RenderQueue = new Sync<int>(this, 3010);
        Rect = new Sync<Rect>(this, Lumora.Core.Math.Rect.Zero);
        RectClip = new Sync<bool>(this, false);
        ClipOffset = new Sync<float2>(this, float2.Zero);
        ColorMask = new Sync<ColorMask>(this, Assets.ColorMask.RGBA);
        StencilComparison = new Sync<StencilComparison>(this, Assets.StencilComparison.Always);
        StencilOperation = new Sync<StencilOperation>(this, Assets.StencilOperation.Keep);
        StencilID = new Sync<byte>(this, 0);
        StencilWriteMask = new Sync<byte>(this, byte.MaxValue);
        StencilReadMask = new Sync<byte>(this, byte.MaxValue);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var rect = Rect.Value;

        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetTexture("Texture", DirectTexture ?? Texture.Asset);
        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);
        asset.SetColor("TintColor", TintColor.Value);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);
        asset.SetFloat("PixelRange", PixelRange.Value);
        asset.SetBool("UseMSDF", UseMSDF.Value);
        asset.SetColor("OutlineColor", OutlineColor.Value);
        asset.SetFloat("OutlineThickness", OutlineThickness.Value);
        asset.SetFloat("FaceDilate", FaceDilate.Value);
        asset.SetFloat("FaceSoftness", FaceSoftness.Value);
        asset.SetColor("UnderlayColor", UnderlayColor.Value);
        asset.SetFloat2("UnderlayOffset", UnderlayOffset.Value);
        asset.SetFloat("UnderlaySoftness", UnderlaySoftness.Value);
        asset.SetBool("AlphaClip", AlphaClip.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetInt("ZWrite", (int)ZWrite.Value);
        asset.SetInt("ZTest", (int)ZTest.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
        asset.SetFloat4("Rect", new float4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
        asset.SetBool("RectClip", RectClip.Value);
        asset.SetFloat2("ClipOffset", ClipOffset.Value);
        asset.SetInt("ColorMask", (int)ColorMask.Value);
        asset.SetInt("StencilComparison", (int)StencilComparison.Value);
        asset.SetInt("StencilOperation", (int)StencilOperation.Value);
        asset.SetInt("StencilID", StencilID.Value);
        asset.SetInt("StencilWriteMask", StencilWriteMask.Value);
        asset.SetInt("StencilReadMask", StencilReadMask.Value);
    }
}
