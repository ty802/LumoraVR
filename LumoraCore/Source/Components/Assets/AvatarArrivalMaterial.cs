// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// The glowing stand-in's material (AvatarArrival.gdshader). One per world, shared by every stand-in in it:
// each piece hands the shader its own presence, floor, height and progress through
// MeshRenderer.SetInstanceParams, so a room full of people arriving at once still draws off this one.
//
// The colours are the dash's own and go to the shader linear, as DashTheme keeps them. -xlinka
[ComponentCategory("Assets/Materials")]
public sealed class AvatarArrivalMaterial : MaterialProvider
{
    public readonly Sync<colorHDR> CoreColor;
    public readonly Sync<colorHDR> GlowColor;
    public readonly Sync<colorHDR> FailColor;

    // Metres between the contour lines, and how fast they climb in metres a second.
    public readonly Sync<float> BandSpacing;
    public readonly Sync<float> BandSpeed;
    public readonly Sync<float> TimeScale;

    protected override MaterialType MaterialType => MaterialType.AvatarArrival;

    // A stand-in is the loading state itself; a checker over it would say it is loading twice.
    public override bool UseLoadingPlaceholder => false;

    public AvatarArrivalMaterial()
    {
        var accent = DashTheme.Accent;
        var fail = DashTheme.Warning;
        CoreColor = new Sync<colorHDR>(this, new colorHDR(accent.r * 0.12f, accent.g * 0.12f, accent.b * 0.2f, 1f));
        GlowColor = new Sync<colorHDR>(this, new colorHDR(accent.r, accent.g, accent.b, 1f));
        FailColor = new Sync<colorHDR>(this, new colorHDR(fail.r, fail.g, fail.b, 1f));
        BandSpacing = new Sync<float>(this, 0.06f);
        BandSpeed = new Sync<float>(this, 0.035f);
        TimeScale = new Sync<float>(this, 1f);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetColor("CoreColor", CoreColor.Value);
        asset.SetColor("GlowColor", GlowColor.Value);
        asset.SetColor("FailColor", FailColor.Value);
        asset.SetFloat("BandSpacing", BandSpacing.Value);
        asset.SetFloat("BandSpeed", BandSpeed.Value);
        asset.SetFloat("TimeScale", TimeScale.Value);
    }

    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        color = GlowColor.Value;
        return true;
    }
}
