// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Unlit wireframe: constant-width edge lines drawn over an optional filled face.
//
// The lines come from barycentric coordinates carried in the mesh's vertex COLOR channel. Godot 4
// has no geometry shader and no barycentric fragment builtin, and the mesh upload path carries only
// position/normal/tangent/color/uv0/index, so COLOR is the only per-vertex slot a triangle-local
// coordinate can travel in. Bake it with a procedural mesh's "Bake Wireframe Barycentrics" action,
// which unwelds the triangles first because a shared vertex cannot hold three different corner
// values. With no bake the surface still draws, it just has no wires. -xlinka
[ComponentCategory("Assets/Materials")]
public class WireframeMaterial : MaterialProvider, ICommonMaterial
{
    [Group("Lines")]
    public readonly Sync<colorHDR> LineColor;

    // pixels, held constant by screen-space derivatives
    [Group("Lines")]
    [Range(0.1f, 20f, "0.0")]
    public readonly Sync<float> LineWidth;

    // off means the mesh has no baked barycentrics; only the fill draws
    [Group("Lines")]
    public readonly Sync<bool> UseVertexBarycentric;

    [Group("Fill")]
    public readonly Sync<colorHDR> FillColor;

    [Group("Fill")]
    public readonly AssetRef<TextureAsset> FillTexture;

    [Group("Fill")]
    public readonly Sync<float2> TextureScale;

    [Group("Fill")]
    public readonly Sync<float2> TextureOffset;

    // switches to the depth-test-disabled shader variant; a compile-time render mode in Godot, so it
    // can't be a plain uniform
    [Group("Rendering")]
    public readonly Sync<bool> DrawOverDepth;

    [Group("Rendering")]
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Wireframe;

    public colorHDR Color
    {
        get => LineColor.Value;
        set => LineColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => FillTexture.Target;
        set => FillTexture.Target = value;
    }

    public WireframeMaterial()
    {
        LineColor = new Sync<colorHDR>(this, colorHDR.White);
        LineWidth = new Sync<float>(this, 1.5f);
        UseVertexBarycentric = new Sync<bool>(this, true);
        // Fully transparent fill by default: a wireframe that also paints solid faces is the exception.
        FillColor = new Sync<colorHDR>(this, new colorHDR(0f, 0f, 0f, 0f));
        FillTexture = new AssetRef<TextureAsset>(this);
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        DrawOverDepth = new Sync<bool>(this, false);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetColor("LineColor", LineColor.Value);
        asset.SetFloat("LineWidth", LineWidth.Value);
        asset.SetBool("UseVertexBarycentric", UseVertexBarycentric.Value);

        asset.SetColor("FillColor", FillColor.Value);
        asset.SetTexture("FillTexture", FillTexture.Asset);
        asset.SetBool("UseFillTexture", FillTexture.Asset != null);
        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetBool("DrawOverDepth", DrawOverDepth.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
