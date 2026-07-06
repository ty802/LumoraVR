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
    public readonly Sync<bool> FlipHorizontal;
    public readonly Sync<bool> FlipVertical;

    private IAssetProvider<TextureAsset>? _textureProvider;
    private IAssetProvider<MaterialAsset>? _material;
    private TextureAsset? _texture;
    private color _tint;
    private Rect _uvRect;
    private bool _preserveAspect;
    private bool _flipH;
    private bool _flipV;

    public RawImage()
    {
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        Tint = new Sync<color>(this, color.White);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        PreserveAspect = new Sync<bool>(this, false);
        FlipHorizontal = new Sync<bool>(this, false);
        FlipVertical = new Sync<bool>(this, false);
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
        _uvRect = UVRect.Value;
        _preserveAspect = PreserveAspect.Value;
        _flipH = FlipHorizontal.Value;
        _flipV = FlipVertical.Value;
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

        // Key by texture identity; the shared image block is created at submit (main).
        var submesh = renderData.GetSubmesh(_material, _textureProvider, GraphicsChunk.RenderData.ImageTexture);
        GenerateImage(submesh.Mesh, submesh, rect, _uvRect, _texture, _preserveAspect, in _tint, clipRect, _flipH, _flipV);
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
        in color tint,
        Rect? clipRect = null,
        bool flipHorizontal = false,
        bool flipVertical = false)
    {
        PrepareMesh(mesh);
        rect = FitRect(rect, uvRect, texture, preserveAspect);

        // Directional UV bounds keyed to the SCREEN edges (left/right/top/bottom).
        // A flip swaps which source edge maps to which screen edge; carrying the
        // bounds directionally lets clipping stay correct even when flipped.
        float uLeft = flipHorizontal ? uvRect.xMax : uvRect.xMin;
        float uRight = flipHorizontal ? uvRect.xMin : uvRect.xMax;
        float vTop = flipVertical ? uvRect.yMax : uvRect.yMin;
        float vBottom = flipVertical ? uvRect.yMin : uvRect.yMax;

        if (clipRect.HasValue && !ClipImage(ref rect, ref uLeft, ref uRight, ref vTop, ref vBottom, clipRect.Value))
        {
            return -1;
        }

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

        mesh.SetUV(0, first, new float2(uLeft, vTop));
        mesh.SetUV(0, first + 1, new float2(uRight, vTop));
        mesh.SetUV(0, first + 2, new float2(uRight, vBottom));
        mesh.SetUV(0, first + 3, new float2(uLeft, vBottom));

        submesh.AddQuadAsTriangles(first, first + 1, first + 2, first + 3);
        return first;
    }

    // Emit a quad filled with a 4-corner color gradient (no texture - the default
    // white sample lets the per-vertex colors show directly). When clipped, the
    // corner colors are bilinearly re-sampled at the clipped corners so the
    // gradient stays continuous.
    public static int GenerateGradient(
        PhosMesh mesh,
        PhosTriangleSubmesh submesh,
        Rect rect,
        in color topLeft,
        in color topRight,
        in color bottomRight,
        in color bottomLeft,
        Rect? clipRect = null)
    {
        PrepareMesh(mesh);

        var draw = rect;
        if (clipRect.HasValue)
        {
            draw = rect.Intersection(clipRect.Value);
            if (draw.IsEmpty)
            {
                return -1;
            }
        }

        color cTL = SampleGradient(rect, draw.xMin, draw.yMax, topLeft, topRight, bottomRight, bottomLeft);
        color cTR = SampleGradient(rect, draw.xMax, draw.yMax, topLeft, topRight, bottomRight, bottomLeft);
        color cBR = SampleGradient(rect, draw.xMax, draw.yMin, topLeft, topRight, bottomRight, bottomLeft);
        color cBL = SampleGradient(rect, draw.xMin, draw.yMin, topLeft, topRight, bottomRight, bottomLeft);

        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(4);

        mesh.RawPositions[first] = new float3(draw.xMin, draw.yMax, 0f);
        mesh.RawPositions[first + 1] = new float3(draw.xMax, draw.yMax, 0f);
        mesh.RawPositions[first + 2] = new float3(draw.xMax, draw.yMin, 0f);
        mesh.RawPositions[first + 3] = new float3(draw.xMin, draw.yMin, 0f);

        for (int i = 0; i < 4; i++)
        {
            mesh.RawNormals[first + i] = float3.Backward;
            mesh.RawTangents[first + i] = new float4(float3.Right, -1f);
        }

        mesh.RawColors[first] = cTL;
        mesh.RawColors[first + 1] = cTR;
        mesh.RawColors[first + 2] = cBR;
        mesh.RawColors[first + 3] = cBL;

        mesh.SetUV(0, first, new float2(0f, 0f));
        mesh.SetUV(0, first + 1, new float2(1f, 0f));
        mesh.SetUV(0, first + 2, new float2(1f, 1f));
        mesh.SetUV(0, first + 3, new float2(0f, 1f));

        submesh.AddQuadAsTriangles(first, first + 1, first + 2, first + 3);
        return first;
    }

    // Bilinearly sample the four corner colors at (x,y) within rect. v is 0 at the
    // bottom (yMin) and 1 at the top (yMax), matching the top/bottom corner inputs.
    private static color SampleGradient(in Rect rect, float x, float y,
        in color topLeft, in color topRight, in color bottomRight, in color bottomLeft)
    {
        float u = rect.width > 0f ? (x - rect.xMin) / rect.width : 0f;
        float v = rect.height > 0f ? (y - rect.yMin) / rect.height : 0f;
        var top = LerpColor(topLeft, topRight, u);
        var bottom = LerpColor(bottomLeft, bottomRight, u);
        return LerpColor(bottom, top, v);
    }

    private static color LerpColor(in color a, in color b, float t)
        => new color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);

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

    // Clip the quad to clipRect, lerping the DIRECTIONAL uv bounds to the clipped
    // region. Works for flipped images because uLeft/uRight (and vTop/vBottom)
    // already encode the source->screen direction.
    private static bool ClipImage(ref Rect rect, ref float uLeft, ref float uRight, ref float vTop, ref float vBottom, in Rect clipRect)
    {
        var clipped = rect.Intersection(clipRect);
        if (clipped.IsEmpty)
        {
            return false;
        }

        if (clipped == rect)
        {
            return true;
        }

        float left = (clipped.xMin - rect.xMin) / rect.width;
        float right = (clipped.xMax - rect.xMin) / rect.width;
        float top = (rect.yMax - clipped.yMax) / rect.height;
        float bottom = (rect.yMax - clipped.yMin) / rect.height;

        float nL = Lerp(uLeft, uRight, left);
        float nR = Lerp(uLeft, uRight, right);
        float nT = Lerp(vTop, vBottom, top);
        float nB = Lerp(vTop, vBottom, bottom);
        uLeft = nL;
        uRight = nR;
        vTop = nT;
        vBottom = nB;
        rect = clipped;
        return true;
    }

    private static float Lerp(float from, float to, float t)
    {
        return from + (to - from) * t;
    }

    private static void PrepareMesh(PhosMesh mesh)
    {
        mesh.HasNormals = true;
        mesh.HasTangents = true;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
    }
}
