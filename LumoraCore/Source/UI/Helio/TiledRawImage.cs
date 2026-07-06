// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

public sealed class TiledRawImage : Graphic
{
    public enum TileSizeBasis
    {
        Absolute,
        RectWidth,
        RectHeight
    }

    public readonly AssetRef<TextureAsset> Texture;
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<color> Tint;
    public readonly Sync<TileSizeBasis> SizeBasis;
    public readonly Sync<float2> TileSize;
    public readonly Sync<float2> TileOffset;
    public readonly Sync<bool> InteractionTarget;

    private IAssetProvider<TextureAsset>? _textureProvider;
    private IAssetProvider<MaterialAsset>? _material;
    private TextureAsset? _texture;
    private color _tint;
    private TileSizeBasis _sizeBasis;
    private float2 _tileSize;
    private float2 _tileOffset;
    private bool _interactionTarget;

    public TiledRawImage()
    {
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        Tint = new Sync<color>(this, color.White);
        SizeBasis = new Sync<TileSizeBasis>(this, TileSizeBasis.Absolute);
        TileSize = new Sync<float2>(this, new float2(32f, 32f));
        TileOffset = new Sync<float2>(this, float2.Zero);
        InteractionTarget = new Sync<bool>(this, true);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkGraphicDirty();
    }

    public override void PrepareCompute()
    {
        _textureProvider = Texture.Target;
        _material = Material.Target;
        _texture = Texture.Asset;
        _tint = Tint.Value;
        _sizeBasis = SizeBasis.Value;
        _tileSize = TileSize.Value;
        _tileOffset = TileOffset.Value;
        _interactionTarget = InteractionTarget.Value;
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

        var clipRect = renderData.GeometryClipRect;
        if (clipRect.HasValue && !rect.Overlaps(clipRect.Value))
        {
            return;
        }

        var tileSize = ResolveTileSize(rect);
        if (tileSize.x <= 0f || tileSize.y <= 0f)
        {
            return;
        }

        // Key by texture identity; the shared image block is created at submit (main).
        var submesh = renderData.GetSubmesh(_material, _textureProvider, GraphicsChunk.RenderData.ImageTexture);
        GenerateTiles(submesh.Mesh, submesh, rect, tileSize, _tileOffset, _texture, in _tint, clipRect);
    }

    public override bool IsPointInside(in float2 point)
    {
        return _interactionTarget && (RectTransform?.LocalComputeRect.Contains(point) ?? false);
    }

    private static void GenerateTiles(
        PhosMesh mesh,
        PhosTriangleSubmesh submesh,
        in Rect rect,
        in float2 tileSize,
        in float2 tileOffset,
        TextureAsset? texture,
        in color tint,
        Rect? clipRect)
    {
        float startX = rect.xMin + PositiveModulo(tileOffset.x, tileSize.x);
        float startY = rect.yMin + PositiveModulo(tileOffset.y, tileSize.y);

        while (startX > rect.xMin)
        {
            startX -= tileSize.x;
        }

        while (startY > rect.yMin)
        {
            startY -= tileSize.y;
        }

        for (float y = startY; y < rect.yMax; y += tileSize.y)
        {
            float tileYMin = MathF.Max(y, rect.yMin);
            float tileYMax = MathF.Min(y + tileSize.y, rect.yMax);
            if (tileYMax <= tileYMin)
            {
                continue;
            }

            for (float x = startX; x < rect.xMax; x += tileSize.x)
            {
                float tileXMin = MathF.Max(x, rect.xMin);
                float tileXMax = MathF.Min(x + tileSize.x, rect.xMax);
                if (tileXMax <= tileXMin)
                {
                    continue;
                }

                var tileRect = Rect.FromMinMax(new float2(tileXMin, tileYMin), new float2(tileXMax, tileYMax));
                var uvRect = new Rect(
                    (tileXMin - x) / tileSize.x,
                    (y + tileSize.y - tileYMax) / tileSize.y,
                    (tileXMax - tileXMin) / tileSize.x,
                    (tileYMax - tileYMin) / tileSize.y
                );
                RawImage.GenerateImage(mesh, submesh, tileRect, uvRect, texture, false, in tint, clipRect);
            }
        }
    }

    private float2 ResolveTileSize(in Rect rect)
    {
        return _sizeBasis switch
        {
            TileSizeBasis.RectWidth => _tileSize * rect.width,
            TileSizeBasis.RectHeight => _tileSize * rect.height,
            _ => _tileSize
        };
    }

    private static float PositiveModulo(float value, float modulus)
    {
        float result = value % modulus;
        return result < 0f ? result + modulus : result;
    }
}
