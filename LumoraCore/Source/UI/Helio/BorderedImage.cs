// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

// One Graphic, two layers, one submesh. Emits a BorderTint rect under a Tint rect inset
// by BorderThickness - same draw call. Replaces the Border/Fill child-slot pattern that
// produced inconsistent rendering across multiple buttons. - xlinka
public sealed class BorderedImage : Graphic
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<Rect> UVRect;
    public readonly Sync<float4> Borders;
    public readonly Sync<color> Tint;
    public readonly Sync<color> BorderTint;
    public readonly Sync<float> BorderThickness;
    public readonly Sync<bool> NineSlice;

    private IAssetProvider<TextureAsset>? _texture;
    private IAssetProvider<MaterialAsset>? _material;
    private Rect _uvRect;
    private float4 _borders;
    private color _tint;
    private color _borderTint;
    private float _borderThickness;
    private bool _nineSlice;

    public BorderedImage()
    {
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        Borders = new Sync<float4>(this, float4.Zero);
        Tint = new Sync<color>(this, color.White);
        BorderTint = new Sync<color>(this, new color(0f, 0f, 0f, 1f));
        BorderThickness = new Sync<float>(this, 2f);
        NineSlice = new Sync<bool>(this, false);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        _texture = Texture.Target;
        _material = Material.Target;
        _uvRect = UVRect.Value;
        _borders = Borders.Value;
        _tint = Tint.Value;
        _borderTint = BorderTint.Value;
        _borderThickness = BorderThickness.Value;
        _nineSlice = NineSlice.Value && HasBorder(_borders);
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null) return;
        var rect = rectTransform.LocalComputeRect;
        if (rect.IsEmpty) return;
        var clipRect = renderData.ClipRect;
        if (clipRect.HasValue && !rect.Overlaps(clipRect.Value)) return;

        var textureBlock = _texture != null ? renderData.GetSharedImageBlock(_texture) : null;
        var submesh = renderData.GetSubmesh(_material, textureBlock, MapMaterial);

        if (_borderTint.a > 0f)
        {
            EmitLayer(submesh, rect, clipRect, in _borderTint);
        }

        float inset = _borderThickness;
        if (inset > 0f && rect.width > inset * 2f && rect.height > inset * 2f && _tint.a > 0f)
        {
            var innerRect = new Rect(
                rect.xMin + inset, rect.yMin + inset,
                rect.width - inset * 2f, rect.height - inset * 2f);
            EmitLayer(submesh, innerRect, clipRect, in _tint);
        }
        else if (_borderTint.a <= 0f && _tint.a > 0f)
        {
            EmitLayer(submesh, rect, clipRect, in _tint);
        }
    }

    public override bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }

    private static MaterialMap MapMaterial(GraphicsChunk.RenderData renderData, IAssetProvider<MaterialAsset>? baseMaterial, object? key, bool usingDefaultMaterial)
    {
        return new MaterialMap(baseMaterial, key as IAssetProvider<MaterialPropertyBlockAsset>);
    }

    private void EmitLayer(PhosTriangleSubmesh submesh, in Rect rect, Rect? clipRect, in color tint)
    {
        var mesh = submesh.Mesh;
        if (_nineSlice)
        {
            var borders = ClampBorders(rect, _borders);
            var uvBorders = GetUVBorders(borders, _texture?.Asset);

            float[] xs = new[] { rect.xMin, rect.xMin + borders.x, rect.xMax - borders.z, rect.xMax };
            float[] ys = new[] { rect.yMin, rect.yMin + borders.y, rect.yMax - borders.w, rect.yMax };
            float[] us = new[] { _uvRect.xMin, _uvRect.xMin + uvBorders.x, _uvRect.xMax - uvBorders.z, _uvRect.xMax };
            float[] vs = new[] { _uvRect.yMax, _uvRect.yMax - uvBorders.w, _uvRect.yMin + uvBorders.y, _uvRect.yMin };

            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    var cellRect = Rect.FromMinMax(new float2(xs[x], ys[y]), new float2(xs[x + 1], ys[y + 1]));
                    if (cellRect.IsEmpty) continue;
                    var cellUv = Rect.FromMinMax(new float2(us[x], vs[y + 1]), new float2(us[x + 1], vs[y]));
                    RawImage.GenerateImage(mesh, submesh, cellRect, cellUv, _texture?.Asset, false, in tint, clipRect);
                }
            }
        }
        else
        {
            RawImage.GenerateImage(mesh, submesh, rect, _uvRect, _texture?.Asset, false, in tint, clipRect);
        }
    }

    private static bool HasBorder(in float4 borders)
    {
        return borders.x > 0f || borders.y > 0f || borders.z > 0f || borders.w > 0f;
    }

    private static float4 ClampBorders(in Rect rect, in float4 borders)
    {
        float halfWidth = rect.width * 0.5f;
        float halfHeight = rect.height * 0.5f;
        return new float4(
            Clamp(borders.x, 0f, halfWidth),
            Clamp(borders.y, 0f, halfHeight),
            Clamp(borders.z, 0f, halfWidth),
            Clamp(borders.w, 0f, halfHeight)
        );
    }

    private static float4 GetUVBorders(in float4 borders, TextureAsset? texture)
    {
        if (texture == null || texture.Width <= 0 || texture.Height <= 0)
        {
            return float4.Zero;
        }
        return new float4(
            borders.x / texture.Width,
            borders.y / texture.Height,
            borders.z / texture.Width,
            borders.w / texture.Height
        );
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
