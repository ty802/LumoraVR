// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Materials/UI")]
public class UIUnlitMaterial : MaterialProvider, ICommonMaterial
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;
    public readonly Sync<colorHDR> TintColor;
    public readonly Sync<bool> UseVertexColor;
    public readonly Sync<bool> AlphaClip;
    public readonly Sync<float> AlphaCutoff;
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;
    public readonly Sync<ZWrite> ZWrite;
    public readonly Sync<ZTest> ZTest;
    public readonly Sync<int> RenderQueue;
    public readonly Sync<Rect> Rect;
    public readonly Sync<bool> RectClip;
    public readonly Sync<ColorMask> ColorMask;
    public readonly Sync<StencilComparison> StencilComparison;
    public readonly Sync<StencilOperation> StencilOperation;
    public readonly Sync<byte> StencilID;
    public readonly Sync<byte> StencilWriteMask;
    public readonly Sync<byte> StencilReadMask;

    protected override MaterialType MaterialType => MaterialType.UI_Unlit;

    // Texture override that bypasses the AssetRef provider chain — used by text submeshes
    // where the font atlas is a transient `TextureAsset` with no owning provider component.
    // Bakes the atlas directly into the material instead of routing through a property block. - xlinka
    public TextureAsset DirectTexture { get; set; }

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

    public UIUnlitMaterial()
    {
        Texture = new AssetRef<TextureAsset>(this);
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        TintColor = new Sync<colorHDR>(this, colorHDR.White);
        UseVertexColor = new Sync<bool>(this, true);
        AlphaClip = new Sync<bool>(this, true);
        AlphaCutoff = new Sync<float>(this, 0.01f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Alpha);
        Culling = new Sync<Culling>(this, Assets.Culling.None);
        ZWrite = new Sync<ZWrite>(this, Assets.ZWrite.Auto);
        ZTest = new Sync<ZTest>(this, Assets.ZTest.LessOrEqual);
        RenderQueue = new Sync<int>(this, -1);
        Rect = new Sync<Rect>(this, Lumora.Core.Math.Rect.Zero);
        RectClip = new Sync<bool>(this, false);
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
        asset.SetBool("AlphaClip", AlphaClip.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetInt("ZWrite", (int)ZWrite.Value);
        asset.SetInt("ZTest", (int)ZTest.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
        asset.SetFloat4("Rect", new float4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
        asset.SetBool("RectClip", RectClip.Value);
        asset.SetInt("ColorMask", (int)ColorMask.Value);
        asset.SetInt("StencilComparison", (int)StencilComparison.Value);
        asset.SetInt("StencilOperation", (int)StencilOperation.Value);
        asset.SetInt("StencilID", StencilID.Value);
        asset.SetInt("StencilWriteMask", StencilWriteMask.Value);
        asset.SetInt("StencilReadMask", StencilReadMask.Value);
    }
}
