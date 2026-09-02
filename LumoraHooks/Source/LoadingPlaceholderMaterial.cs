// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Godot.Hooks;

// The skin a surface wears while its material is still arriving.
//
// One material, one texture, for the whole process - it carries no per-renderer state, so there is
// nothing to make a copy of. It lives entirely on the renderer side: no slot, no component, no RefID,
// nothing that replicates or saves. A world full of half-loaded imports costs exactly one of these.
//
// The look is a deliberately QUIET dark checker. It has to read as "this is not finished yet" at a
// glance without competing with the world around it, so it sits below mid-grey and unshaded - a bright
// magenta grid would be louder than the content it is standing in for, and people would think it was
// the content. Unshaded also means it looks the same in a dark room as a lit one, which is the point:
// it is a status, not a surface. -xlinka
internal static class LoadingPlaceholderMaterial
{
    private const int TextureSize = 64;
    private const int CellSize = 8;

    private static StandardMaterial3D? _material;

    public static Material Get()
    {
        if (_material != null && GodotObject.IsInstanceValid(_material))
            return _material;

        var image = Image.CreateEmpty(TextureSize, TextureSize, false, Image.Format.Rgba8);
        var dark = new Color(0.105f, 0.105f, 0.125f);
        var light = new Color(0.185f, 0.185f, 0.215f);
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                bool cell = ((x / CellSize) + (y / CellSize)) % 2 == 0;
                image.SetPixel(x, y, cell ? dark : light);
            }
        }

        var texture = ImageTexture.CreateFromImage(image);

        _material = new StandardMaterial3D
        {
            ResourceName = "LumoraLoadingPlaceholder",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = texture,
            // The mesh under it may have no UVs at all (a raw import, a procedural surface). Triplanar
            // projects the checker in world space instead, so it reads as a checker either way.
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(2f, 2f, 2f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            // Both sides: an import whose winding is inside-out would otherwise show nothing at all,
            // which is the state we are trying to replace.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return _material;
    }
}
