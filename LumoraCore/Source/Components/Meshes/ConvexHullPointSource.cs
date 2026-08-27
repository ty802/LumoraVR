// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// Shared point gathering for everything that hulls something: the hull mesh and the hull collider
// both accept either a referenced mesh or a hand-written point list, and both need the same answer to
// "is the source ready yet", so neither grows its own copy of it.
public static class ConvexHullPointSource
{
    // Ceiling on how many source vertices are read. The hull of a dense mesh is decided by a handful
    // of extreme points, but the solver still has to visit every input once, and an unbounded read off
    // a multi-million-vertex import would stall the frame that changed the reference. Points past this
    // are skipped with an even stride so the sample still spans the whole mesh instead of stopping at
    // whatever the first chunk happened to cover. -xlinka
    public const int MaxSourcePoints = 65536;

    // asset-backed meshes decode asynchronously, so a false here means "not yet", not "never"
    public static bool IsReady(Component? source)
    {
        return source switch
        {
            ProceduralMesh procedural => procedural.PhosMesh != null,
            MeshProvider provider => provider.Asset?.MeshData != null,
            null => false,
            _ => false
        };
    }

    // null when there is none yet
    public static PhosMesh? GetMesh(Component? source)
    {
        return source switch
        {
            ProceduralMesh procedural => procedural.PhosMesh,
            MeshProvider provider => provider.Asset?.MeshData,
            _ => null
        };
    }

    // a referenced mesh contributes its vertex positions and an explicit list contributes its entries;
    // both together contribute both, which is how you pad a mesh hull out to a few hand-placed extremes
    public static void Gather(Component? source, SyncFieldList<float3>? explicitPoints, List<float3> output)
    {
        output.Clear();

        PhosMesh? mesh = GetMesh(source);
        if (mesh != null && mesh.VertexCount > 0)
        {
            var positions = mesh.RawPositions;
            int count = mesh.VertexCount;
            int stride = count > MaxSourcePoints ? (count + MaxSourcePoints - 1) / MaxSourcePoints : 1;
            for (int i = 0; i < count; i += stride)
                output.Add(positions[i]);
        }

        if (explicitPoints != null)
        {
            for (int i = 0; i < explicitPoints.Count; i++)
                output.Add(explicitPoints[i]);
        }
    }
}
