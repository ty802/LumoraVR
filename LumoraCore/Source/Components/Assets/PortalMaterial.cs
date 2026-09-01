// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// The face of a world portal, backed by res://Shaders/Mat_Portal.gdshader: a picture inside an
// ellipse, a crackling crystal rim, a glow and sparks outside it. Meant for one quad whose proportions
// set the ellipse; QuadAspect tells the shader those proportions so the picture is cover-fitted
// rather than stretched. -xlinka
[ComponentCategory("Assets/Materials")]
public class PortalMaterial : MaterialProvider
{
    [Group("Picture")]
    public readonly AssetRef<TextureAsset> Picture;
    public readonly Sync<bool> FlipPicture;
    public readonly Sync<colorHDR> PictureTint;
    public readonly Sync<colorHDR> FillColor;
    public readonly Sync<float> QuadAspect;

    [Group("Rim")]
    public readonly Sync<colorHDR> RimColor;
    public readonly Sync<colorHDR> CrackColor;
    public readonly Sync<float> RimWidth;
    public readonly Sync<float> CrackScale;
    public readonly Sync<float> CrackWidth;
    public readonly Sync<float> CrackBrightness;
    public readonly Sync<float> Flicker;

    [Group("Glow")]
    public readonly Sync<float> GlowWidth;
    public readonly Sync<float> GlowStrength;
    public readonly Sync<float> Sparkle;

    [Group("Motion")]
    public readonly Sync<float> Speed;
    public readonly Sync<float> Seed;

    [Group("Rendering")]
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Portal;

    // The picture lands seconds after the door is up. A checker in the doorway while it does reads as
    // a broken texture, and the shader has its own fill for the wait, so no loading skin here.
    public override bool UseLoadingPlaceholder => false;

    public PortalMaterial()
    {
        Picture = new AssetRef<TextureAsset>(this);
        FlipPicture = new Sync<bool>(this, false);
        PictureTint = new Sync<colorHDR>(this, colorHDR.White);
        FillColor = new Sync<colorHDR>(this, new colorHDR(0.08f, 0.09f, 0.16f, 1f));
        QuadAspect = new Sync<float>(this, 0.76f);
        RimColor = new Sync<colorHDR>(this, new colorHDR(0.3f, 0.5f, 1f, 1f));
        CrackColor = new Sync<colorHDR>(this, colorHDR.White);
        RimWidth = new Sync<float>(this, 0.16f);
        CrackScale = new Sync<float>(this, 22f);
        CrackWidth = new Sync<float>(this, 0.045f);
        CrackBrightness = new Sync<float>(this, 2.5f);
        Flicker = new Sync<float>(this, 0.6f);
        GlowWidth = new Sync<float>(this, 0.14f);
        GlowStrength = new Sync<float>(this, 1.4f);
        Sparkle = new Sync<float>(this, 0.8f);
        Speed = new Sync<float>(this, 1f);
        Seed = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, 3010);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var picture = Picture.Asset;
        bool hasPicture = picture != null && picture.Hook != null;
        if (hasPicture)
            asset.SetTexture("Picture", picture!);
        asset.SetBool("UsePicture", hasPicture);
        asset.SetBool("FlipPicture", FlipPicture.Value);
        asset.SetColor("PictureTint", PictureTint.Value);
        asset.SetColor("FillColor", FillColor.Value);
        asset.SetFloat("QuadAspect", QuadAspect.Value);
        asset.SetColor("RimColor", RimColor.Value);
        asset.SetColor("CrackColor", CrackColor.Value);
        asset.SetFloat("RimWidth", RimWidth.Value);
        asset.SetFloat("CrackScale", CrackScale.Value);
        asset.SetFloat("CrackWidth", CrackWidth.Value);
        asset.SetFloat("CrackBrightness", CrackBrightness.Value);
        asset.SetFloat("Flicker", Flicker.Value);
        asset.SetFloat("GlowWidth", GlowWidth.Value);
        asset.SetFloat("GlowStrength", GlowStrength.Value);
        asset.SetFloat("Sparkle", Sparkle.Value);
        asset.SetFloat("Speed", Speed.Value);
        asset.SetFloat("Seed", Seed.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);
    }

    // The rim is what the surface reads as from a distance.
    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        color = RimColor.Value;
        return true;
    }
}
