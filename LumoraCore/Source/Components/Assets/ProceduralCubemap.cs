// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Base for cubemaps generated from a function of direction. Subclasses only implement
// Sample; face iteration, the direction convention and the RGBA8 packing live in
// CubemapProjector so every generator agrees on which way is up.
//
// Generation runs on the change pass, not per frame: a provider only regenerates when one of its
// fields moves. That matters more here than for a 2D texture because the work is six squares, so a
// 512 face is 1.5 million samples. Keep Size modest for anything that animates. -xlinka
public abstract class ProceduralCubemap : DynamicAssetProvider<CubemapAsset>
{
    // pixels
    public readonly Sync<int> Size;

    public readonly Sync<bool> GenerateMipmaps;

    protected ProceduralCubemap()
    {
        Size = new Sync<int>(this, 128);
        GenerateMipmaps = new Sync<bool>(this, true);
    }

    // direction is a unit vector in the cubemap's own space; face is for generators that want per-face
    // behavior. Called from several threads at once, so it must not touch mutable generator state.
    protected abstract color Sample(CubemapFace face, in float3 direction);

    // snapshot anything Sample reads before generation starts; sync fields read directly from worker
    // threads would race a concurrent write for the whole generation
    protected virtual void PrepareSample() { }

    protected override void OnAssetCreated(CubemapAsset asset) { }

    protected override void UpdateAsset(CubemapAsset asset)
    {
        int size = System.Math.Clamp(Size.Value, CubemapAsset.MinFaceSize, CubemapAsset.MaxFaceSize);
        bool mipmaps = GenerateMipmaps.Value;

        PrepareSample();
        var faces = CubemapProjector.Generate(size, (face, dir) => Sample(face, in dir));
        asset.SetFaces(faces, size, mipmaps);
    }

    protected override void OnAssetCleared() { }
}
