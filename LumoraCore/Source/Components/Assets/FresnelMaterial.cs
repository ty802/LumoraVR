using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>
/// Material with Fresnel effect - color changes based on viewing angle.
/// Used for debug rig visualization and special effects.
/// </summary>
[ComponentCategory("Assets/Materials")]
public class FresnelMaterial : MaterialProvider
{
    public readonly Sync<float> Exponent;
    public readonly Sync<colorHDR> NearColor;
    public readonly Sync<colorHDR> FarColor;
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;

    protected override MaterialType MaterialType => MaterialType.PBS_Metallic; // use StandardMaterial3D

    public FresnelMaterial()
    {
        Exponent = new Sync<float>(this, 1.0f);
        NearColor = new Sync<colorHDR>(this, new colorHDR(1f, 1f, 1f, 0f));
        FarColor = new Sync<colorHDR>(this, colorHDR.White);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);

        // Approximate Fresnel using rim lighting
        asset.SetColor("AlbedoColor", NearColor.Value);
        asset.SetColor("EmissiveColor", FarColor.Value);
        asset.SetFloat("Metallic", 0.0f);
        asset.SetFloat("Smoothness", 0.8f);
    }
}
