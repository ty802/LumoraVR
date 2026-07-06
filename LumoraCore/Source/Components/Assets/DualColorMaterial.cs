// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>
/// Unlit UI material that lerps between two tints by the texture's luminance: a
/// grayscale mask renders dark→<see cref="TintColor"/>, light→<see cref="SecondColor"/>
/// (2-tone icons). Use on an Image/RawImage by setting its Material to this.
/// </summary>
[ComponentCategory("Assets/Materials")]
public class DualColorMaterial : MaterialProvider, ICommonMaterial
{
    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;

    /// <summary>Color at texture luminance 0.</summary>
    public readonly Sync<colorHDR> TintColor;
    /// <summary>Color at texture luminance 1.</summary>
    public readonly Sync<colorHDR> SecondColor;

    /// <summary>Grayscale mask texture.</summary>
    public readonly AssetRef<TextureAsset> Texture;

    public readonly Sync<bool> UseVertexColor;
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<float> AlphaCutoff;
    public readonly Sync<Culling> Culling;
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.UI_DualColor;

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

    public DualColorMaterial()
    {
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        TintColor = new Sync<colorHDR>(this, colorHDR.Black);
        SecondColor = new Sync<colorHDR>(this, colorHDR.White);
        Texture = new AssetRef<TextureAsset>(this);
        UseVertexColor = new Sync<bool>(this, true);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Transparent);
        AlphaCutoff = new Sync<float>(this, 0.5f);
        Culling = new Sync<Culling>(this, Assets.Culling.None);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var textureAsset = Texture.Asset;

        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetBool("AlphaClip", BlendMode.Value == Assets.BlendMode.Cutout);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetColor("TintColor", TintColor.Value);     // -> albedo_color
        asset.SetColor("SecondColor", SecondColor.Value); // -> second_color
        asset.SetTexture("Texture", textureAsset!);        // -> albedo_texture (+ use_albedo_texture)
    }
}
