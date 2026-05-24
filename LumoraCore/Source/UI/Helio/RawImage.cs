// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

public sealed class RawImage : Graphic
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<color> Tint;
    public readonly Sync<Rect> UVRect;
    public readonly Sync<bool> PreserveAspect;

    private IAssetProvider<TextureAsset>? _textureProvider;
    private IAssetProvider<MaterialAsset>? _material;
    private TextureAsset? _texture;
    private color _tint;
    private Rect _uvRect;
    private bool _preserveAspect;
    private MainTexturePropertyBlock? _textureBlock;

    public RawImage()
    {
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        Tint = new Sync<color>(this, color.White);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        PreserveAspect = new Sync<bool>(this, false);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        _textureProvider = Texture.Target;
        _material = Material.Target;
        _texture = Texture.Asset;
        _tint = Tint.Value;
        _uvRect = UVRect.Value;
        _preserveAspect = PreserveAspect.Value;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null)
        {
            return;
        }

        var rect = rectTransform.LocalComputeRect;
        if (rect.IsEmpty)
        {
            return;
        }

        var textureBlock = EnsureTextureBlock();
        var submesh = renderData.GetSubmesh(_material, textureBlock, MapMaterial);
        GenerateImage(submesh.Mesh, submesh, rect, _uvRect, _texture, _preserveAspect, in _tint);
    }

    public override bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }

    public static int GenerateImage(
        PhosMesh mesh,
        PhosTriangleSubmesh submesh,
        Rect rect,
        Rect uvRect,
        TextureAsset? texture,
        bool preserveAspect,
        in color tint)
    {
        PrepareMesh(mesh);
        rect = FitRect(rect, uvRect, texture, preserveAspect);

        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(4);

        mesh.RawPositions[first] = new float3(rect.xMin, rect.yMax, 0f);
        mesh.RawPositions[first + 1] = new float3(rect.xMax, rect.yMax, 0f);
        mesh.RawPositions[first + 2] = new float3(rect.xMax, rect.yMin, 0f);
        mesh.RawPositions[first + 3] = new float3(rect.xMin, rect.yMin, 0f);

        for (int i = 0; i < 4; i++)
        {
            mesh.RawNormals[first + i] = float3.Backward;
            mesh.RawTangents[first + i] = new float4(float3.Right, -1f);
            mesh.RawColors[first + i] = tint;
        }

        mesh.SetUV(0, first, new float2(uvRect.xMin, uvRect.yMin));
        mesh.SetUV(0, first + 1, new float2(uvRect.xMax, uvRect.yMin));
        mesh.SetUV(0, first + 2, new float2(uvRect.xMax, uvRect.yMax));
        mesh.SetUV(0, first + 3, new float2(uvRect.xMin, uvRect.yMax));

        submesh.AddQuadAsTriangles(first, first + 1, first + 2, first + 3);
        return first;
    }

    private MainTexturePropertyBlock? EnsureTextureBlock()
    {
        if (_textureProvider == null)
        {
            return null;
        }

        _textureBlock ??= Slot.GetComponent<MainTexturePropertyBlock>() ?? Slot.AttachComponent<MainTexturePropertyBlock>();
        _textureBlock.Texture.Target = _textureProvider;
        return _textureBlock;
    }

    private static MaterialMap MapMaterial(GraphicsChunk.RenderData renderData, IAssetProvider<MaterialAsset>? baseMaterial, object? key, bool usingDefaultMaterial)
    {
        return new MaterialMap(baseMaterial, key as IAssetProvider<MaterialPropertyBlockAsset>);
    }

    private static Rect FitRect(Rect rect, in Rect uvRect, TextureAsset? texture, bool preserveAspect)
    {
        if (!preserveAspect || texture == null || texture.Width <= 0 || texture.Height <= 0 || uvRect.IsEmpty || rect.IsEmpty)
        {
            return rect;
        }

        float imageWidth = texture.Width * uvRect.width;
        float imageHeight = texture.Height * uvRect.height;
        if (imageWidth <= 0f || imageHeight <= 0f)
        {
            return rect;
        }

        float imageAspect = imageWidth / imageHeight;
        float rectAspect = rect.width / rect.height;

        if (rectAspect > imageAspect)
        {
            float width = rect.height * imageAspect;
            rect.x += (rect.width - width) * 0.5f;
            rect.width = width;
        }
        else
        {
            float height = rect.width / imageAspect;
            rect.y += (rect.height - height) * 0.5f;
            rect.height = height;
        }

        return rect;
    }

    private static void PrepareMesh(PhosMesh mesh)
    {
        mesh.HasNormals = true;
        mesh.HasTangents = true;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
    }
}
