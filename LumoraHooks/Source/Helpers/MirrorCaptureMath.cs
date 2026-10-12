// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;

namespace Lumora.Godot.Helpers;

internal static class MirrorCaptureMath
{
    // A physical window into the reflected world. Its lens follows the edges of the glass,
    // not the viewer's head rotation or display aspect. Each eye keeps its own reflected position.
    public static bool TryFrame(Vector3 eye, Transform3D glass, Vector2 size, float distance,
        out Transform3D camera, out Projection projection)
    {
        camera = Transform3D.Identity;
        projection = Projection.Identity;
        var right = glass.Basis.X.Normalized();
        var normal = glass.Basis.X.Cross(glass.Basis.Y).Normalized();
        var up = normal.Cross(right);
        float depth = -(eye - glass.Origin).Dot(normal);
        if (!eye.IsFinite() || !normal.IsFinite() || !size.IsFinite() || size.X <= 0f || size.Y <= 0f
            || !(depth > 0.001f) || !float.IsFinite(distance) || !(distance > 0f))
            return false;

        camera = new Transform3D(new Basis(right, up, normal), eye + normal * (2f * depth));
        var centre = camera.AffineInverse() * glass.Origin;
        float halfWidth = glass.Basis.X.Length() * size.X * 0.5f;
        float halfHeight = glass.Basis.Y.Dot(up) * size.Y * 0.5f;
        // Leave a tiny allowance for geometry touching the glass. Clip everything behind it.
        float near = MathF.Max(0.001f, depth - 0.0005f);
        float scale = near / depth;
        projection = Projection.CreateFrustum(
            (centre.X - halfWidth) * scale, (centre.X + halfWidth) * scale,
            (centre.Y - halfHeight) * scale, (centre.Y + halfHeight) * scale,
            near, depth + distance);
        // Non-uniformly scaled, rotated parents may shear the glass. Cancel that shear in
        // clip space so its four corners still coincide with the four texture corners.
        float shear = glass.Basis.Y.Dot(right) / glass.Basis.Y.Dot(up);
        projection.Y.X -= projection.X.X * shear;
        projection.Z.X -= projection.X.X * shear * centre.Y / depth;
        return projection.X.IsFinite() && projection.Y.IsFinite()
            && projection.Z.IsFinite() && projection.W.IsFinite();
    }

    public static Vector2I TextureSize(Vector2 size, int resolution)
    {
        if (!size.IsFinite() || size.X <= 0f || size.Y <= 0f)
            size = Vector2.One;
        float scale = resolution / MathF.Max(size.X, size.Y);
        return new Vector2I(Math.Max(16, (int)MathF.Round(size.X * scale)),
            Math.Max(16, (int)MathF.Round(size.Y * scale)));
    }
}
