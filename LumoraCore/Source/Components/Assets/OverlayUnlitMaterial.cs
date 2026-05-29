// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Materials")]
public class OverlayUnlitMaterial : MaterialProvider, ICommonMaterial
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly Sync<float2> FrontTextureScale;
    public readonly Sync<float2> FrontTextureOffset;
    public readonly Sync<float2> BehindTextureScale;
    public readonly Sync<float2> BehindTextureOffset;
    public readonly Sync<colorHDR> FrontTintColor;
    public readonly Sync<colorHDR> BehindTintColor;
    public readonly Sync<bool> UseVertexColor;
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;
    public readonly Sync<ZWrite> ZWrite;
    public readonly Sync<ZTest> ZTest;
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.OverlayUnlit;

    public colorHDR Color
    {
        get => FrontTintColor.Value;
        set => FrontTintColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => Texture.Target;
        set => Texture.Target = value;
    }

    public OverlayUnlitMaterial()
    {
        Texture = new AssetRef<TextureAsset>(this);
        FrontTextureScale = new Sync<float2>(this, float2.One);
        FrontTextureOffset = new Sync<float2>(this, float2.Zero);
        BehindTextureScale = new Sync<float2>(this, float2.One);
        BehindTextureOffset = new Sync<float2>(this, float2.Zero);
        FrontTintColor = new Sync<colorHDR>(this, colorHDR.White);
        BehindTintColor = new Sync<colorHDR>(this, new colorHDR(0.5f, 0.5f, 0.5f, 0.5f));
        UseVertexColor = new Sync<bool>(this, true);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Additive);
        Culling = new Sync<Culling>(this, Assets.Culling.None);
        ZWrite = new Sync<ZWrite>(this, Assets.ZWrite.Off);
        ZTest = new Sync<ZTest>(this, Assets.ZTest.Disabled);
        RenderQueue = new Sync<int>(this, 4010);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetTexture("Texture", Texture.Asset);
        asset.SetFloat2("FrontTextureScale", FrontTextureScale.Value);
        asset.SetFloat2("FrontTextureOffset", FrontTextureOffset.Value);
        asset.SetFloat2("BehindTextureScale", BehindTextureScale.Value);
        asset.SetFloat2("BehindTextureOffset", BehindTextureOffset.Value);
        asset.SetColor("FrontTintColor", FrontTintColor.Value);
        asset.SetColor("BehindTintColor", BehindTintColor.Value);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);
        asset.SetInt("ZWrite", (int)ZWrite.Value);
        asset.SetInt("ZTest", (int)ZTest.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
