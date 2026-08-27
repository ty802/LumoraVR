// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Per-face checkerboard in six distinguishable colors. This exists to answer one question quickly:
// is a cubemap oriented the way the person who wrote it thinks it is. Each axis gets its own hue and
// each face its own shade, so a mirrored or rotated sky shows up immediately instead of looking
// merely wrong. -xlinka
[ComponentCategory("Assets/Cubemaps")]
public class CheckerCubemap : ProceduralCubemap
{
    // pixels
    public readonly Sync<int> CellSize;

    public readonly Sync<color> PositiveX;
    public readonly Sync<color> NegativeX;
    public readonly Sync<color> PositiveY;
    public readonly Sync<color> NegativeY;
    public readonly Sync<color> PositiveZ;
    public readonly Sync<color> NegativeZ;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> DarkFactor;

    private readonly color[] _faceColors = new color[6];
    private float _cells;
    private float _dark;

    public CheckerCubemap()
    {
        CellSize = new Sync<int>(this, 16);
        PositiveX = new Sync<color>(this, new color(0.90f, 0.16f, 0.16f, 1f));
        NegativeX = new Sync<color>(this, new color(0.90f, 0.16f, 0.75f, 1f));
        PositiveY = new Sync<color>(this, new color(0.20f, 0.85f, 0.25f, 1f));
        NegativeY = new Sync<color>(this, new color(0.90f, 0.85f, 0.15f, 1f));
        PositiveZ = new Sync<color>(this, new color(0.18f, 0.35f, 0.95f, 1f));
        NegativeZ = new Sync<color>(this, new color(0.15f, 0.85f, 0.90f, 1f));
        DarkFactor = new Sync<float>(this, 0.25f);
        Size.Value = 64;
    }

    protected override void PrepareSample()
    {
        _faceColors[(int)CubemapFace.PositiveX] = PositiveX.Value;
        _faceColors[(int)CubemapFace.NegativeX] = NegativeX.Value;
        _faceColors[(int)CubemapFace.PositiveY] = PositiveY.Value;
        _faceColors[(int)CubemapFace.NegativeY] = NegativeY.Value;
        _faceColors[(int)CubemapFace.PositiveZ] = PositiveZ.Value;
        _faceColors[(int)CubemapFace.NegativeZ] = NegativeZ.Value;
        _dark = System.Math.Clamp(DarkFactor.Value, 0f, 1f);

        // The sampler is given a direction, not a pixel, so the cell count is what carries across:
        // face edge divided by cell size is how many squares that face should show.
        int size = System.Math.Clamp(Size.Value, CubemapAsset.MinFaceSize, CubemapAsset.MaxFaceSize);
        int cell = System.Math.Clamp(CellSize.Value, 1, size);
        _cells = System.Math.Max(1f, size / (float)cell);
    }

    protected override color Sample(CubemapFace face, in float3 direction)
    {
        // Back out the face-local coordinates from the direction: divide the two minor axes by the
        // major one and the result is the same sc/tc pair the projector started from.
        float u, v;
        switch (face)
        {
            case CubemapFace.PositiveX: u = -direction.z / direction.x; v = -direction.y / direction.x; break;
            case CubemapFace.NegativeX: u = direction.z / -direction.x; v = -direction.y / -direction.x; break;
            case CubemapFace.PositiveY: u = direction.x / direction.y; v = direction.z / direction.y; break;
            case CubemapFace.NegativeY: u = direction.x / -direction.y; v = -direction.z / -direction.y; break;
            case CubemapFace.PositiveZ: u = direction.x / direction.z; v = -direction.y / direction.z; break;
            default: u = -direction.x / -direction.z; v = -direction.y / -direction.z; break;
        }

        int cx = (int)System.Math.Floor((u * 0.5f + 0.5f) * _cells);
        int cy = (int)System.Math.Floor((v * 0.5f + 0.5f) * _cells);
        bool light = ((cx + cy) & 1) == 0;

        var c = _faceColors[(int)face];
        return light ? c : new color(c.r * _dark, c.g * _dark, c.b * _dark, c.a);
    }
}
