// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;
using SizingKind = Helio.UI.BorderSizing;

namespace Helio.UI;

[ComponentCategory("UI/Helio/Graphics")]
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
    public readonly Sync<bool> FlipHorizontal;
    public readonly Sync<bool> FlipVertical;
    // Untextured control backgrounds share RoundedPanel's geometry while retaining tint bindings. -xlinka
    public readonly Sync<float> CornerRadius;
    // How wide a nine-sliced sprite's frame draws; see BorderSizing.
    public readonly Sync<SizingKind> BorderSizing;
    // Off: never hit-tested, so a press on it passes through to whatever lies under it.
    public readonly Sync<bool> InteractionTarget;

    private IAssetProvider<TextureAsset>? _texture;
    private IAssetProvider<MaterialAsset>? _material;
    private Rect _uvRect;
    private float4 _borders;
    private color _tint;
    private bool _nineSlice;
    private bool _preserveAspect;
    private bool _flipH;
    private bool _flipV;
    private float _cornerRadius;
    private bool _fractional;
    private float _borderScale = 1f;
    private float _fixedBorder;
    private SizingKind _sizing;

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
        FlipHorizontal = new Sync<bool>(this, false);
        FlipVertical = new Sync<bool>(this, false);
        CornerRadius = new Sync<float>(this, 0f);
        BorderSizing = new Sync<SizingKind>(this, SizingKind.Texels);
        InteractionTarget = new Sync<bool>(this, true);
    }

    public override bool RequiresPreGraphicsCompute => false;

    // Trims its quads to RenderData.GeometryClipRect, so it can carry a clip window that rides the chunk.
    public override bool TrimsGeometryToClip => true;

    // Every quad it emits is inside its rect: nine-slice cuts the rect up, preserve-aspect only shrinks it,
    // and the clip trim only ever takes area away. -xlinka
    public override Rect? MeasureBounds() => RectTransform?.LocalComputeRect;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkGraphicDirty();
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
        _flipH = FlipHorizontal.Value;
        _flipV = FlipVertical.Value;
        _cornerRadius = CornerRadius.Value;
        _fractional = sprite != null && sprite.FractionalBorders.Value;
        _borderScale = sprite != null ? sprite.BorderScale.Value : 1f;
        _fixedBorder = sprite != null ? sprite.FixedBorderSize.Value : 0f;
        _sizing = BorderSizing.Value;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null)
        {
            return;
        }

        var rect = rectTransform.LocalComputeRect;
        // GeometryClipRect (not ClipRect) so scroll content bakes full quads - the material still carries the
        // real clip rect for the shader, but the geometry isn't culled/trimmed to the viewport. -xlinka
        var clipRect = renderData.GeometryClipRect;

        if (rect.IsEmpty)
        {
            return;
        }

        if (clipRect.HasValue && !rect.Overlaps(clipRect.Value))
        {
            return;
        }

        // Key by texture identity; the shared image block is created at submit (main).
        var submesh = renderData.GetSubmesh(_material, _texture, GraphicsChunk.RenderData.ImageTexture);

        if (_texture == null && _cornerRadius > 0f)
        {
            RoundedPanel.EmitRoundedRect(submesh.Mesh, submesh, rect, _cornerRadius, _tint, clipRect);
            return;
        }

        if (_nineSlice)
        {
            if (_fractional || _sizing != SizingKind.Texels || _borderScale != 1f)
                WriteSizedNineSlice(submesh, rect, clipRect);
            else
                WriteNineSlice(submesh, rect, clipRect);
            return;
        }

        RawImage.GenerateImage(submesh.Mesh, submesh, rect, _uvRect, _texture?.Asset, _preserveAspect, in _tint, clipRect, _flipH, _flipV);
    }

    public override bool IsPointInside(in float2 point)
    {
        return InteractionTarget.Value && (RectTransform?.LocalComputeRect.Contains(point) ?? false);
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

    // The frame measured the sprite's way: each border as a share of the sprite's region, drawn as wide as the
    // sizing says, and both sides of an axis shrunk together when they cannot fit, so a round corner stays
    // round rather than being cut. Bottom pairs with bottom: V runs down the atlas, so the frame's bottom row
    // is the largest V. -xlinka
    private void WriteSizedNineSlice(PhosTriangleSubmesh submesh, in Rect rect, Rect? clipRect)
    {
        var texture = _texture?.Asset;
        float texelsX = texture != null && texture.Width > 0 ? texture.Width * _uvRect.width : 0f;
        float texelsY = texture != null && texture.Height > 0 ? texture.Height * _uvRect.height : 0f;
        float4 share = _fractional ? _borders
            : texelsX > 0f && texelsY > 0f
                ? new float4(_borders.x / texelsX, _borders.y / texelsY, _borders.z / texelsX, _borders.w / texelsY)
                : float4.Zero;
        float4 drawn = _sizing switch
        {
            SizingKind.FixedSize => share * _fixedBorder,
            SizingKind.RectWidth => share * rect.width,
            SizingKind.RectHeight => share * rect.height,
            _ => new float4(share.x * texelsX, share.y * texelsY, share.z * texelsX, share.w * texelsY),
        } * (float.IsFinite(_borderScale) ? MathF.Max(_borderScale, 0f) : 1f);

        float across = drawn.x + drawn.z;
        if (across > rect.width && across > 0f)
        {
            float fit = rect.width / across;
            drawn = new float4(drawn.x * fit, drawn.y, drawn.z * fit, drawn.w);
        }
        float up = drawn.y + drawn.w;
        if (up > rect.height && up > 0f)
        {
            float fit = rect.height / up;
            drawn = new float4(drawn.x, drawn.y * fit, drawn.z, drawn.w * fit);
        }

        Span<float> xs = stackalloc float[] { rect.xMin, rect.xMin + drawn.x, rect.xMax - drawn.z, rect.xMax };
        Span<float> ys = stackalloc float[] { rect.yMin, rect.yMin + drawn.y, rect.yMax - drawn.w, rect.yMax };
        Span<float> us = stackalloc float[]
        {
            _uvRect.xMin, _uvRect.xMin + share.x * _uvRect.width, _uvRect.xMax - share.z * _uvRect.width, _uvRect.xMax,
        };
        Span<float> vs = stackalloc float[]
        {
            _uvRect.yMax, _uvRect.yMax - share.y * _uvRect.height, _uvRect.yMin + share.w * _uvRect.height, _uvRect.yMin,
        };

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                var cellRect = Rect.FromMinMax(new float2(xs[x], ys[y]), new float2(xs[x + 1], ys[y + 1]));
                if (cellRect.IsEmpty)
                    continue;
                var cellUv = Rect.FromMinMax(new float2(us[x], vs[y + 1]), new float2(us[x + 1], vs[y]));
                RawImage.GenerateImage(submesh.Mesh, submesh, cellRect, cellUv, texture, false, in _tint, clipRect);
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
