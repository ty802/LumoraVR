// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.Avatar;

// A shape an avatar names, found on its mesh. Model readers join a channel and its shape into one name
// ("Facial_Blends.Jaw_Down") or collapse a pair that matches ("Blink.Blink" to "Blink"), so a name taken from
// the file may reach the mesh in either form. -xlinka
internal static class FaceShapes
{
    public static int Find(SkinnedMeshRenderer? renderer, string? name)
    {
        if (renderer == null || renderer.IsDestroyed || string.IsNullOrEmpty(name))
            return -1;
        int index = renderer.GetBlendShapeIndex(name);
        if (index >= 0)
            return index;

        int dot = name.LastIndexOf('.');
        string? tail = dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : null;
        if (tail != null)
        {
            index = renderer.GetBlendShapeIndex(tail);
            if (index >= 0)
                return index;
        }
        for (int i = 0; i < renderer.BlendShapeCount; i++)
        {
            var have = renderer.BlendShapeName(i);
            if (string.IsNullOrEmpty(have))
                continue;
            if (have.EndsWith("." + name, StringComparison.Ordinal) || have.StartsWith(name + ".", StringComparison.Ordinal))
                return i;
            if (tail != null && have.EndsWith("." + tail, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
