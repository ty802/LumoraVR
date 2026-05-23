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

    private IAssetProvider<TextureAsset>? _texture;
    private IAssetProvider<MaterialAsset>? _material;
    private Rect _uvRect;
    private float4 _borders;
    private color _tint;
    private bool _nineSlice;
    private MainTexturePropertyBlock? _textureBlock;

    public Image()
    {
        Sprite = new SyncRef<Sprite>(this);
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        Borders = new Sync<float4>(this, float4.Zero);
        Tint = new Sync<color>(this, color.White);
        NineSlice = new Sync<bool>(this, false);
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

        if (_nineSlice)
        {
            WriteNineSlice(submesh, rect);
            return;
        }

        WriteQuad(submesh, rect, _uvRect);
    }

    public override bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }

    private MainTexturePropertyBlock? EnsureTextureBlock()
    {
        if (_texture == null)
        {
            return null;
        }

        _textureBlock ??= Slot.GetComponent<MainTexturePropertyBlock>() ?? Slot.AttachComponent<MainTexturePropertyBlock>();
        _textureBlock.Texture.Target = _texture;
        return _textureBlock;
    }

    private static MaterialMap MapMaterial(GraphicsChunk.RenderData renderData, IAssetProvider<MaterialAsset>? baseMaterial, object? key, bool usingDefaultMaterial)
    {
        return new MaterialMap(baseMaterial, key as IAssetProvider<MaterialPropertyBlockAsset>);
    }

    private void WriteQuad(PhosTriangleSubmesh submesh, in Rect rect, in Rect uv)
    {
        var mesh = submesh.Mesh;
        PrepareMesh(mesh);

        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(4);

        // Godot UV is Y-down: V=0 at texture top. Our world is Y-up: yMax is screen top.
        // Screen top vertex (yMax) needs the smaller V to land on texture top. - xlinka
        WriteVertex(mesh, first, new float3(rect.xMin, rect.yMax, 0f), new float2(uv.xMin, uv.yMin));
        WriteVertex(mesh, first + 1, new float3(rect.xMax, rect.yMax, 0f), new float2(uv.xMax, uv.yMin));
        WriteVertex(mesh, first + 2, new float3(rect.xMax, rect.yMin, 0f), new float2(uv.xMax, uv.yMax));
        WriteVertex(mesh, first + 3, new float3(rect.xMin, rect.yMin, 0f), new float2(uv.xMin, uv.yMax));

        submesh.AddQuadAsTriangles(first, first + 1, first + 2, first + 3);
    }

    private void WriteNineSlice(PhosTriangleSubmesh submesh, in Rect rect)
    {
        var mesh = submesh.Mesh;
        PrepareMesh(mesh);

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

        // `ys` ascends in Y-up world space (yMin → yMax = bottom → top of screen).
        // `vs` must ALSO ascend visually in atlas space — but atlas V is Y-down, so the
        // row that aligns with screen-bottom is the LARGEST V (uv.yMax). Walk V in
        // reverse so ys[i] and vs[i] always describe the same physical row. - xlinka
        float[] vs =
        {
            _uvRect.yMax,
            _uvRect.yMax - uvBorders.w,
            _uvRect.yMin + uvBorders.y,
            _uvRect.yMin
        };

        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(16);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int index = first + y * 4 + x;
                WriteVertex(mesh, index, new float3(xs[x], ys[y], 0f), new float2(us[x], vs[y]));
            }
        }

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                int bottomLeft = first + y * 4 + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + 4;
                int topRight = topLeft + 1;
                submesh.AddQuadAsTriangles(topLeft, topRight, bottomRight, bottomLeft);
            }
        }
    }

    private void WriteVertex(PhosMesh mesh, int index, in float3 position, in float2 uv)
    {
        mesh.RawPositions[index] = position;
        mesh.RawNormals[index] = float3.Backward;
        mesh.RawTangents[index] = new float4(float3.Right, -1f);
        mesh.RawColors[index] = _tint;
        mesh.SetUV(0, index, uv);
    }

    private static void PrepareMesh(PhosMesh mesh)
    {
        mesh.HasNormals = true;
        mesh.HasTangents = true;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
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
