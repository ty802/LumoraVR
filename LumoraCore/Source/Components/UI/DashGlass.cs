// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The frosted backing behind a floating panel: the world blurred and washed toward the theme's
// purple, on whatever mesh the caller gives it. Cards and text sit on a canvas in front and stay
// solid; only the backing is glass. -xlinka
public static class DashGlass
{
    public const float BlurRadius = 8f;
    public const float Opacity = 0.92f;
    public const float TintStrength = 0.6f;

    public static CustomShaderMaterial Attach(Slot slot)
    {
        var material = slot.GetComponent<CustomShaderMaterial>() ?? slot.AttachComponent<CustomShaderMaterial>();
        material.ShaderPath.Value = "res://Shaders/Glass.gdshader";
        material.BlendMode.Value = BlendMode.Alpha;
        material.Culling.Value = Culling.None;
        if (material.Parameters.Count == 0)
        {
            var tint = DashTheme.MeshTint(DashTheme.Surface);
            AddColor(material, "tint_color", new colorHDR(tint.r, tint.g, tint.b, TintStrength));
            AddFloat(material, "blur_radius", BlurRadius);
            AddFloat(material, "opacity", Opacity);
        }
        return material;
    }

    private static void AddColor(CustomShaderMaterial material, string name, colorHDR value)
    {
        var param = material.Parameters.Add();
        param.Name.Value = name;
        param.Type.Value = ShaderUniformType.Vec4;
        param.IsColor.Value = true;
        param.Value.Value = new float4(value.r, value.g, value.b, value.a);
    }

    private static void AddFloat(CustomShaderMaterial material, string name, float value)
    {
        var param = material.Parameters.Add();
        param.Name.Value = name;
        param.Type.Value = ShaderUniformType.Float;
        param.Value.Value = new float4(value, 0f, 0f, 0f);
    }
}
