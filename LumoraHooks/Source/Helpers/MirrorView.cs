using System;
using Godot;

namespace Lumora.Godot.Helpers;

/// <summary>Off-axis projection through a rectangular mirror facing local -Z.</summary>
public readonly record struct MirrorView(Transform3D Transform, float Height, Vector2 Offset,
    float Near, float Far, Vector2I Resolution)
{
    public static bool TryCreate(Transform3D surface, Vector2 size, Vector3 eye, int pixels,
        float renderDistance, out MirrorView view)
    {
        view = default;
        if (!surface.Origin.IsFinite() || !eye.IsFinite() || !size.IsFinite()
            || MathF.Abs(surface.Basis.Determinant()) < 1e-8f || !float.IsFinite(renderDistance))
            return false;
        var basis = surface.Basis.Orthonormalized();
        var front = -basis.Z;
        var relative = eye - surface.Origin;
        float distance = relative.Dot(front);
        if (!float.IsFinite(distance) || distance < 0.005f || size.X <= 0 || size.Y <= 0)
            return false;
        size *= new Vector2(surface.Basis.X.Length(), surface.Basis.Y.Length());
        float near = distance + 0.001f;
        float scale = near / distance;
        float aspect = size.X / size.Y;
        pixels = Math.Clamp(pixels, Lumora.Core.Components.Mirror.MinimumResolution,
            Lumora.Core.Components.Mirror.MaximumResolution);
        var resolution = aspect >= 1f
            ? new Vector2I(pixels, Math.Max(16, (int)MathF.Round(pixels / aspect)))
            : new Vector2I(Math.Max(16, (int)MathF.Round(pixels * aspect)), pixels);
        // Match the physical aperture exactly despite pixel rounding of the viewport aspect.
        // The tiny difference in horizontal aperture is subpixel at ordinary mirror sizes.
        view = new MirrorView(new Transform3D(basis, eye - 2f * distance * front),
            size.Y * scale, new Vector2(-relative.Dot(basis.X), -relative.Dot(basis.Y)) * scale,
            near, near + Math.Max(0.1f, renderDistance), resolution);
        return true;
    }

    public void Apply(Camera3D camera, SubViewport viewport)
    {
        if (viewport.Size != Resolution) viewport.Size = Resolution;
        camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        camera.SetFrustum(Height, Offset, Near, Far);
        camera.GlobalTransform = Transform;
    }
}
