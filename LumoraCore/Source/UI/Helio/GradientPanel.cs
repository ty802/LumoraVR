// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

/// <summary>
/// Fills its rect with a 4-corner color gradient (no texture). Set the four corner
/// colors directly; for a simple linear gradient just match the two pairs. Maps to
/// a CSS gradient background.
/// </summary>
public sealed class GradientPanel : Graphic
{
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<color> TopLeft;
    public readonly Sync<color> TopRight;
    public readonly Sync<color> BottomRight;
    public readonly Sync<color> BottomLeft;

    private IAssetProvider<MaterialAsset>? _material;
    private color _tl;
    private color _tr;
    private color _br;
    private color _bl;

    public GradientPanel()
    {
        Material = new AssetRef<MaterialAsset>(this);
        TopLeft = new Sync<color>(this, color.White);
        TopRight = new Sync<color>(this, color.White);
        BottomRight = new Sync<color>(this, color.White);
        BottomLeft = new Sync<color>(this, color.White);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkGraphicDirty();
    }

    public override void PrepareCompute()
    {
        _material = Material.Target;
        _tl = TopLeft.Value;
        _tr = TopRight.Value;
        _br = BottomRight.Value;
        _bl = BottomLeft.Value;
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

        // Null texture -> default white sample, so the per-vertex corner colors show directly.
        var submesh = renderData.GetSubmesh(_material, null, GraphicsChunk.RenderData.ImageTexture);
        RawImage.GenerateGradient(submesh.Mesh, submesh, rect, in _tl, in _tr, in _br, in _bl, clipRect);
    }

    public override bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }
}
