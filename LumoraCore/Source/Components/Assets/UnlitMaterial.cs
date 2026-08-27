// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Materials")]
public class UnlitMaterial : MaterialProvider, ICommonMaterial
{
    // TEXTURE TRANSFORM

    public readonly Sync<float2> TextureScale;

    public readonly Sync<float2> TextureOffset;

    // COLOR AND TEXTURE

    public readonly Sync<colorHDR> TintColor;

    public readonly AssetRef<TextureAsset> Texture;

    public readonly Sync<bool> UseVertexColor;

    // BLEND SETTINGS

    public readonly Sync<BlendMode> BlendMode;

    // used by Cutout blend mode
    public readonly Sync<float> AlphaCutoff;

    public readonly Sync<Culling> Culling;

    // -1 = default
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Unlit;

    // ICommonMaterial IMPLEMENTATION

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

    public UnlitMaterial()
    {
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);

        TintColor = new Sync<colorHDR>(this, colorHDR.White);
        Texture = new AssetRef<TextureAsset>(this);
        UseVertexColor = new Sync<bool>(this, false);

        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        AlphaCutoff = new Sync<float>(this, 0.5f);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var textureAsset = Texture.Asset;
        LumoraLogger.Debug($"UnlitMaterial.UpdateMaterial: Texture.Target={Texture.Target?.GetType().Name}, Texture.Asset={textureAsset?.GetType().Name}, HasHook={textureAsset?.Hook != null}");

        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetBool("AlphaClip", BlendMode.Value == Assets.BlendMode.Cutout);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetColor("TintColor", TintColor.Value);
        asset.SetTexture("Texture", textureAsset!);
    }
}

