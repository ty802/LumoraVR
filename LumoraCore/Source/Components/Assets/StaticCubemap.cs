// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Components;

namespace Lumora.Core.Assets;

// A cubemap loaded from one equirectangular (panorama) image. The image gathers and decodes itself
// through AssetManager exactly like any other URL asset, which is what gets it
// LocalDB storage, peer transfer by hash and sharing between every requester for the same URL and
// face size for free.
//
// Six separate face images are the job of FaceCubemap rather than a second mode here.
// A URL-backed provider is keyed on ONE url, and bolting five more onto it would mean either giving
// up the sharing that key buys or inventing a composite key nothing else in the asset system knows
// how to gather. The assembler instead references six ordinary image providers, so each face keeps
// its own variant, cache entry and transfer. -xlinka
[ComponentCategory("Assets/Cubemaps")]
public class StaticCubemap : StaticAssetProvider<CubemapAsset>, ICustomInspectorUI
{
    // pixels; 0 derives it from the panorama width (right almost always), set to pin a size regardless of source
    public readonly Sync<int> FaceSize;

    // off gives a hard, aliased sky at grazing angles
    public readonly Sync<bool> GenerateMipmaps;

    public StaticCubemap()
    {
        FaceSize = new Sync<int>(this, 0);
        GenerateMipmaps = new Sync<bool>(this, true);
    }

    public override void OnStart()
    {
        base.OnStart();
        EngineSettings.Changed += OnQualitySettingChanged;
    }

    public override void OnDestroy()
    {
        EngineSettings.Changed -= OnQualitySettingChanged;
        base.OnDestroy();
    }

    // Written from the settings world's thread, which is not ours, so bounce the re-resolve through
    // our own world instead of re-requesting the asset from under it.
    private void OnQualitySettingChanged()
    {
        var world = World;
        if (world == null || IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (!IsDestroyed)
                MarkChangeDirty();
        });
    }

    // obeys the same texture-quality cap as any other image, rather than a second one nobody would
    // think to check; 0 still means derive it from the panorama
    public int EffectiveFaceSize
    {
        get
        {
            int cap = EngineSettings.MaxTextureSize;
            int requested = FaceSize.Value;
            if (requested <= 0)
                return cap > 0 ? cap : 0;
            return cap > 0 ? System.Math.Min(requested, cap) : requested;
        }
    }

    protected override IAssetVariantDescriptor? GetVariantDescriptor() =>
        new CubemapVariantDescriptor(EffectiveFaceSize, GenerateMipmaps.Value);

    // Everything here is read off the loaded asset, not recomputed from the fields: the size actually
    // projected, the source it was projected from, and the resident bytes the renderer reported.
    public void BuildInspectorBody(UIBuilder ui)
    {
        var asset = Asset;
        if (asset == null)
        {
            InspectorStats.AddRow(ui, "Cubemap", URL.Value == null ? "no URL" : "not loaded");
            return;
        }

        InspectorStats.AddRow(ui, "Face size", $"{asset.FaceSize} px");
        InspectorStats.AddRow(ui, "Requested", EffectiveFaceSize > 0 ? $"{EffectiveFaceSize} px" : "from source");
        InspectorStats.AddRow(ui, "Source", asset.SourcePanoramaSize is { } size
            ? $"{size.Width}x{size.Height} equirect"
            : "not a panorama");
        InspectorStats.AddRow(ui, "Mips", asset.HasMipmaps ? "generated" : "none");
        InspectorStats.AddRow(ui, "Faces", InspectorStats.Bytes(asset.MemorySize));
        InspectorStats.AddRow(ui, "VRAM", asset.GpuBytes is { } vram
            ? InspectorStats.Bytes(vram)
            : "renderer has not reported yet");
    }
}
