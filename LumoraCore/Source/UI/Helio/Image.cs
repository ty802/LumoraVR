// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

public sealed class Image : Graphic
{
    public readonly SyncRef<Sprite> Sprite;
    public readonly AssetRef<TextureAsset> Texture;
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<Rect> UVRect;
    public readonly Sync<float4> Borders;
    public readonly Sync<color> Tint;
    public readonly Sync<bool> NineSlice;
    public readonly Sync<bool> PreserveAspect;

    private IAssetProvider<TextureAsset>? _texture;
    private IAssetProvider<MaterialAsset>? _material;
    private Rect _uvRect;
    private float4 _borders;
    private color _tint;
    private bool _nineSlice;
    private bool _preserveAspect;

    public Image()
    {
        Sprite = new SyncRef<Sprite>(this);
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        Borders = new Sync<float4>(this, float4.Zero);
        Tint = new Sync<color>(this, color.White);
        NineSlice = new Sync<bool>(this, false);
        PreserveAspect = new Sync<bool>(this, false);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        var sprite = Sprite.Target;
        _texture = Texture.Target ?? sprite?.Texture.Target;
        _material = Material.Target;
        _uvRect = sprite != null ? sprite.UVRect.Value : UVRect.Value;
        _borders = sprite != null ? sprite.Borders.Value : Borders.Value;
        _tint = Tint.Value;
        _nineSlice = NineSlice.Value && HasBorder(_borders);
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
        var clipRect = renderData.ClipRect;
        DumpButtonDiagnosticsOnce(rect, clipRect);

        if (rect.IsEmpty)
        {
            return;
        }

        if (clipRect.HasValue && !rect.Overlaps(clipRect.Value))
        {
            return;
        }

        var textureBlock = _texture != null ? renderData.GetSharedImageBlock(_texture) : null;
        var submesh = renderData.GetSubmesh(_material, textureBlock, MapMaterial);

        if (_nineSlice)
        {
            WriteNineSlice(submesh, rect, clipRect);
            return;
        }

        RawImage.GenerateImage(submesh.Mesh, submesh, rect, _uvRect, _texture?.Asset, _preserveAspect, in _tint, clipRect);
    }

    private bool _diagnosticsLogged;

    // One-shot dump for diagnosing why nav buttons render inconsistently. Prints
    // each Image's parent button name + own slot name + rect + tint + texture status.
    // Remove once the cause is found. - xlinka
    private void DumpButtonDiagnosticsOnce(in Rect rect, Rect? clipRect)
    {
        if (_diagnosticsLogged) return;
        var parent = Slot?.Parent;
        if (parent == null) return;
        var parentName = parent.SlotName.Value;
        if (parentName != "Home" && parentName != "Worlds" && parentName != "Session"
            && parentName != "Settings" && parentName != "Friends" && parentName != "Inventory"
            && parentName != "Files")
        {
            return;
        }
        _diagnosticsLogged = true;
        Lumora.Core.Logging.Logger.Log(
            $"[ButtonDiag] {parentName}/{Slot!.SlotName.Value} rect=({rect.xMin:F1},{rect.yMin:F1},{rect.width:F1}x{rect.height:F1}) " +
            $"empty={rect.IsEmpty} clip={(clipRect.HasValue ? $"({clipRect.Value.xMin:F1},{clipRect.Value.yMin:F1},{clipRect.Value.width:F1}x{clipRect.Value.height:F1})" : "none")} " +
            $"tint=({_tint.r:F2},{_tint.g:F2},{_tint.b:F2},{_tint.a:F2}) tex={(_texture?.Asset != null ? "yes" : "no")} " +
            $"nineSlice={_nineSlice} borders=({_borders.x:F1},{_borders.y:F1},{_borders.z:F1},{_borders.w:F1})");
    }

    public override bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }

    private static MaterialMap MapMaterial(GraphicsChunk.RenderData renderData, IAssetProvider<MaterialAsset>? baseMaterial, object? key, bool usingDefaultMaterial)
    {
        return new MaterialMap(baseMaterial, key as IAssetProvider<MaterialPropertyBlockAsset>);
    }

    private void WriteNineSlice(PhosTriangleSubmesh submesh, in Rect rect, Rect? clipRect)
    {
        var mesh = submesh.Mesh;

        var borders = ClampBorders(rect, _borders);
        var uvBorders = GetUVBorders(borders, _texture?.Asset);

        float[] xs =
        {
            rect.xMin,
            rect.xMin + borders.x,
            rect.xMax - borders.z,
            rect.xMax
        };

        float[] ys =
        {
            rect.yMin,
            rect.yMin + borders.y,
            rect.yMax - borders.w,
            rect.yMax
        };

        float[] us =
        {
            _uvRect.xMin,
            _uvRect.xMin + uvBorders.x,
            _uvRect.xMax - uvBorders.z,
            _uvRect.xMax
        };

        // `ys` ascends in Y-up world space (yMin to yMax = bottom to top of screen).
        // `vs` must ALSO ascend visually in atlas space, but atlas V is Y-down, so the
        // row that aligns with screen-bottom is the LARGEST V (uv.yMax). Walk V in
        // reverse so ys[i] and vs[i] always describe the same physical row. - xlinka
        float[] vs =
        {
            _uvRect.yMax,
            _uvRect.yMax - uvBorders.w,
            _uvRect.yMin + uvBorders.y,
            _uvRect.yMin
        };

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                var cellRect = Rect.FromMinMax(
                    new float2(xs[x], ys[y]),
                    new float2(xs[x + 1], ys[y + 1]));

                if (cellRect.IsEmpty)
                {
                    continue;
                }

                var cellUv = Rect.FromMinMax(
                    new float2(us[x], vs[y + 1]),
                    new float2(us[x + 1], vs[y]));

                RawImage.GenerateImage(mesh, submesh, cellRect, cellUv, _texture?.Asset, false, in _tint, clipRect);
            }
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
