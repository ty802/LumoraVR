// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// The shader files of the slice, displace and distance lerp families. Workflow, blending and culling are all
// compile-time in a Godot shader, so each family ships one file per combination, named
// Mat_PBS_<Family><Metallic|Specular>[Alpha][FrontCull|DoubleSided]. The paths are built once per family, so
// naming the variant on every material update allocates nothing.
internal static class PBSEffectShaders
{
    public static string[] Paths(string family)
    {
        var paths = new string[12];
        string[] workflows = { "Metallic", "Specular" };
        string[] blends = { "", "Alpha" };
        string[] culls = { "", "FrontCull", "DoubleSided" };
        for (int w = 0; w < 2; w++)
            for (int b = 0; b < 2; b++)
                for (int c = 0; c < 3; c++)
                    paths[w * 6 + b * 3 + c] = $"res://Shaders/Mat_PBS_{family}{workflows[w]}{blends[b]}{culls[c]}.gdshader";
        return paths;
    }

    public static string Pick(string[] paths, bool specular, bool alphaBlend, Culling culling)
    {
        int cull = culling switch
        {
            Culling.Front => 1,
            Culling.None => 2,
            _ => 0
        };
        return paths[(specular ? 6 : 0) + (alphaBlend ? 3 : 0) + cull];
    }
}
