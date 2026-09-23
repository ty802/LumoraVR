// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// A colour matrix over whatever is BEHIND the surface. Tinted visors, night-vision lenses, camera
// filters: the thing you look THROUGH rather than at.
//
// Each output channel is a weighted sum of the three input channels plus an offset, which is enough
// to do a great deal: RedFromRed alone dims red, RedFromGreen feeds green into red, dropping all of
// Blue* kills blue entirely. A gas mask visor is usually two numbers - half the red, half the green,
// no blue - and reads as dark green glass.
//
// The result is clamped per channel afterwards, so a matrix that would blow out stays inside a range
// the author picked.
//
// NOT LIT. The filtered pixels are the answer; shading them again would tint the room's light into a
// lens that is supposed to be showing the room. -xlinka
[ComponentCategory("Assets/Materials")]
public class ChannelMatrixMaterial : MaterialProvider
{
    // RED out
    public readonly Sync<float> RedFromRed;
    public readonly Sync<float> RedFromGreen;
    public readonly Sync<float> RedFromBlue;
    public readonly Sync<float> RedOffset;

    // GREEN out
    public readonly Sync<float> GreenFromRed;
    public readonly Sync<float> GreenFromGreen;
    public readonly Sync<float> GreenFromBlue;
    public readonly Sync<float> GreenOffset;

    // BLUE out
    public readonly Sync<float> BlueFromRed;
    public readonly Sync<float> BlueFromGreen;
    public readonly Sync<float> BlueFromBlue;
    public readonly Sync<float> BlueOffset;

    // Applied after the matrix, per channel.
    public readonly Sync<float> ClampRedMin;
    public readonly Sync<float> ClampGreenMin;
    public readonly Sync<float> ClampBlueMin;
    public readonly Sync<float> ClampRedMax;
    public readonly Sync<float> ClampGreenMax;
    public readonly Sync<float> ClampBlueMax;

    // The glass's own tint and opacity over the filtered result.
    public readonly Sync<colorHDR> Tint;

    // Filters draw late, after the world they are filtering has been laid down. 5000 is past every
    // ordinary transparent surface.
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.ChannelMatrix;

    public ChannelMatrixMaterial()
    {
        // Identity: every channel passes through untouched, so a fresh material is a clear pane
        // rather than a black one.
        RedFromRed = new Sync<float>(this, 1f);
        RedFromGreen = new Sync<float>(this, 0f);
        RedFromBlue = new Sync<float>(this, 0f);
        RedOffset = new Sync<float>(this, 0f);

        GreenFromRed = new Sync<float>(this, 0f);
        GreenFromGreen = new Sync<float>(this, 1f);
        GreenFromBlue = new Sync<float>(this, 0f);
        GreenOffset = new Sync<float>(this, 0f);

        BlueFromRed = new Sync<float>(this, 0f);
        BlueFromGreen = new Sync<float>(this, 0f);
        BlueFromBlue = new Sync<float>(this, 1f);
        BlueOffset = new Sync<float>(this, 0f);

        ClampRedMin = new Sync<float>(this, 0f);
        ClampGreenMin = new Sync<float>(this, 0f);
        ClampBlueMin = new Sync<float>(this, 0f);
        ClampRedMax = new Sync<float>(this, 2f);
        ClampGreenMax = new Sync<float>(this, 2f);
        ClampBlueMax = new Sync<float>(this, 2f);

        Tint = new Sync<colorHDR>(this, colorHDR.White);
        RenderQueue = new Sync<int>(this, 5000);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        // Packed a row at a time, w carrying that row's offset, which is the shape the shader wants.
        asset.SetColor("LevelsR", new colorHDR(RedFromRed.Value, RedFromGreen.Value, RedFromBlue.Value, RedOffset.Value));
        asset.SetColor("LevelsG", new colorHDR(GreenFromRed.Value, GreenFromGreen.Value, GreenFromBlue.Value, GreenOffset.Value));
        asset.SetColor("LevelsB", new colorHDR(BlueFromRed.Value, BlueFromGreen.Value, BlueFromBlue.Value, BlueOffset.Value));

        asset.SetFloat3("ClampMin", new float3(ClampRedMin.Value, ClampGreenMin.Value, ClampBlueMin.Value));
        asset.SetFloat3("ClampMax", new float3(ClampRedMax.Value, ClampGreenMax.Value, ClampBlueMax.Value));

        asset.SetColor("Tint", Tint.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);
    }
}
