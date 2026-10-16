// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core.Phos;

namespace Lumora.Godot.Hooks;

// UV1/UV2 use Godot's UV/UV2 attributes. UV3/UV4 share CUSTOM1.xy/zw, leaving
// CUSTOM0 available for point billboard data. Missing sets read as (0, 0).
internal static class MeshUVChannels
{
    public static readonly Mesh.ArrayFormat Format =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);

    public static Mesh.ArrayFormat FormatFor(global::Godot.Collections.Array arrays)
    {
        using var value = arrays[(int)Mesh.ArrayType.Custom1];
        return value.VariantType == Variant.Type.Nil ? 0 : Format;
    }

    public static Mesh.ArrayFormat Set(global::Godot.Collections.Array arrays, PhosMesh mesh,
        ref float[]? scratch, ReadOnlySpan<int> vertices = default, bool uv3 = true, bool uv4 = true, int repeats = 1)
    {
        var third = mesh.RawUV2s;
        var fourth = mesh.RawUV3s;
        uv3 &= third.Length >= mesh.VertexCount && third.Length > 0;
        uv4 &= fourth.Length >= mesh.VertexCount && fourth.Length > 0;
        if (!uv3 && !uv4) return 0;

        int count = vertices.IsEmpty ? mesh.VertexCount : vertices.Length;
        var packed = SurfaceSubset.Scratch(ref scratch, checked(count * repeats * 4));
        for (int k = 0; k < count; k++)
        {
            int source = vertices.IsEmpty ? k : vertices[k];
            var a = uv3 ? third[source] : default;
            var b = uv4 ? fourth[source] : default;
            for (int r = 0; r < repeats; r++)
            {
                int at = (k * repeats + r) * 4;
                packed[at] = a.x;
                packed[at + 1] = a.y;
                packed[at + 2] = b.x;
                packed[at + 3] = b.y;
            }
        }
        using var value = Variant.CreateFrom(packed);
        arrays[(int)Mesh.ArrayType.Custom1] = value;
        return Format;
    }
}
