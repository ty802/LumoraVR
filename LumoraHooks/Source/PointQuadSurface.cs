// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core.Phos;

namespace Lumora.Godot.Hooks;

// A points submesh as Godot can draw it. Billboard materials grow every point into a camera-facing quad,
// a job for a geometry stage Godot does not have, so the quad's four corners are laid down here as four
// copies of the point, in the order (0,0) (1,0) (1,1) (0,1), and the vertex stage spreads them apart by
// VERTEX_ID (Unlit_Core.gdshaderinc). Every corner carries the point's position, UV, UV2 and colour; its
// normal, which on a point holds the billboard's width and height scale and its roll, rides in CUSTOM0
// with 1 in W to mark the vertex as a point.
//
// A material that is not a billboard sees four vertices in one place and draws nothing, which is what a
// point surface drew before it was uploaded at all. -xlinka
internal static class PointQuadSurface
{
    public static readonly Mesh.ArrayFormat Format =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);

    public static global::Godot.Collections.Array? Build(PhosMesh mesh, PhosSubmesh submesh)
    {
        var rawIndices = submesh.RawIndices;
        var rawPositions = mesh.RawPositions;
        int points = submesh.IndexCount;
        if (rawIndices == null || rawPositions == null || rawPositions.Length == 0 || points == 0)
            return null;

        var rawNormals = mesh.RawNormals;
        var rawUV0 = mesh.RawUV0s;
        var rawUV1 = mesh.RawUV1s;
        var rawColors = mesh.RawColors;
        bool hasNormals = rawNormals != null && rawNormals.Length > 0;
        bool hasUV0 = rawUV0 != null && rawUV0.Length > 0;
        bool hasUV1 = rawUV1 != null && rawUV1.Length > 0;
        bool hasColors = rawColors != null && rawColors.Length > 0;

        int count = points * 4;
        var positions = new Vector3[count];
        var custom = new float[count * 4];
        var uv0 = hasUV0 ? new Vector2[count] : null;
        var uv1 = hasUV1 ? new Vector2[count] : null;
        var colors = hasColors ? new Color[count] : null;
        var indices = new int[points * 6];

        for (int p = 0; p < points; p++)
        {
            int source = rawIndices[p];
            var pos = rawPositions[source];
            var data = hasNormals ? rawNormals![source] : default;
            for (int corner = 0; corner < 4; corner++)
            {
                int v = p * 4 + corner;
                positions[v] = new Vector3(pos.x, pos.y, pos.z);
                custom[v * 4] = data.x;
                custom[v * 4 + 1] = data.y;
                custom[v * 4 + 2] = data.z;
                custom[v * 4 + 3] = 1f;
                if (uv0 != null)
                    uv0[v] = new Vector2(rawUV0![source].x, rawUV0[source].y);
                if (uv1 != null)
                    uv1[v] = new Vector2(rawUV1![source].x, rawUV1[source].y);
                if (colors != null)
                {
                    var c = rawColors![source];
                    colors[v] = new Color(c.r, c.g, c.b, c.a);
                }
            }
            int b = p * 4;
            int i = p * 6;
            indices[i] = b;
            indices[i + 1] = b + 2;
            indices[i + 2] = b + 1;
            indices[i + 3] = b;
            indices[i + 4] = b + 3;
            indices[i + 5] = b + 2;
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Custom0] = custom;
        float[]? extraUV = null;
        MeshUVChannels.Set(arrays, mesh, ref extraUV, rawIndices.AsSpan(0, points), repeats: 4);
        if (uv0 != null)
            arrays[(int)Mesh.ArrayType.TexUV] = uv0;
        if (uv1 != null)
            arrays[(int)Mesh.ArrayType.TexUV2] = uv1;
        if (colors != null)
            arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        return arrays;
    }
}
