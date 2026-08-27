// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// Writes triangle-local barycentric coordinates into a mesh's vertex color channel, which is what
// feeds the wireframe material.
//
// Godot 4 has no geometry shader and exposes no barycentric fragment builtin, and the mesh upload
// path carries position, normal, tangent, color, one UV set and indices - nothing else. Color is
// therefore the only per-vertex slot a triangle-local coordinate can ride in, and carrying it there
// means unwelding first: a corner needs the value (1,0,0) in one triangle and (0,1,0) in its
// neighbour, and a shared vertex only holds one.
//
// So this costs the mesh its vertex sharing and its existing vertex colors. Both are stated up front
// rather than worked around, because there is no honest way to have the wires without them. -xlinka
public static class PhosBarycentrics
{
    // Rebuilds the mesh with one vertex per triangle corner and the barycentric basis in the color
    // channel. Triangle submeshes are preserved in order; any non-triangle submesh is dropped,
    // because points and lines have no triangle to be local to. Returns false and leaves the mesh
    // alone when there is nothing to bake.
    public static bool Bake(PhosMesh mesh)
    {
        if (mesh == null || mesh.VertexCount == 0 || mesh.Submeshes.Count == 0)
            return false;

        var submeshIndices = new List<int[]>();
        int totalTriangles = 0;
        foreach (var submesh in mesh.Submeshes)
        {
            if (submesh.Topology != PhosTopology.Triangles)
                continue;

            int count = submesh.IndexCount;
            var indices = new int[count];
            System.Array.Copy(submesh.RawIndices, indices, count);
            submeshIndices.Add(indices);
            totalTriangles += count / 3;
        }

        if (totalTriangles == 0)
            return false;

        // Snapshot before Clear, which drops the backing arrays outright.
        int sourceCount = mesh.VertexCount;
        bool hasNormals = mesh.HasNormals;
        bool hasTangents = mesh.HasTangents;
        bool hasUVs = mesh.HasUV0s;

        var positions = Copy(mesh.RawPositions, sourceCount);
        var normals = hasNormals ? Copy(mesh.RawNormals, sourceCount) : null;
        var tangents = hasTangents ? Copy(mesh.RawTangents, sourceCount) : null;
        var uvs = hasUVs ? Copy(mesh.RawUV0s, sourceCount) : null;

        mesh.Clear();
        mesh.HasNormals = hasNormals;
        mesh.HasTangents = hasTangents;
        mesh.HasUV0s = hasUVs;
        mesh.HasColors = true;
        mesh.IncreaseVertexCount(totalTriangles * 3);

        var basis = new[]
        {
            new color(1f, 0f, 0f, 1f),
            new color(0f, 1f, 0f, 1f),
            new color(0f, 0f, 1f, 1f),
        };

        int write = 0;
        foreach (var indices in submeshIndices)
        {
            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int start = write;
                for (int corner = 0; corner < 3; corner++)
                {
                    int source = indices[i + corner];
                    // An index past the source buffer would read garbage; fall back to vertex 0 rather
                    // than dropping the triangle and leaving a hole in the wire cage.
                    if ((uint)source >= (uint)sourceCount)
                        source = 0;

                    mesh.RawPositions[write] = positions[source];
                    if (normals != null) mesh.RawNormals[write] = normals[source];
                    if (tangents != null) mesh.RawTangents[write] = tangents[source];
                    if (uvs != null) mesh.RawUV0s[write] = uvs[source];
                    mesh.RawColors[write] = basis[corner];
                    write++;
                }

                PhosTriangle triangle = submesh.AddTriangle();
                submesh.SetTriangle(triangle.IndexUnsafe, start, start + 1, start + 2);
            }
        }

        return true;
    }

    private static T[] Copy<T>(T[] source, int count)
    {
        var result = new T[count];
        System.Array.Copy(source, result, count);
        return result;
    }
}
