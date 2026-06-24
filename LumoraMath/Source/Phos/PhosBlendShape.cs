// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

/// <summary>
/// Blend shape (morph target) data.
/// Stores position/normal/tangent deltas for animation.
/// PhosMeshSys blend shape definition.
/// </summary>
public class PhosBlendShape
{
    /// <summary>Name of this blend shape (e.g., "Smile", "Blink")</summary>
    public string Name { get; set; } = "";

    /// <summary>Frames for this blend shape (usually just one)</summary>
    public PhosBlendShapeFrame[] Frames { get; set; } = Array.Empty<PhosBlendShapeFrame>();

    public PhosBlendShape(string name, int frameCount = 1)
    {
        Name = name;
        // PhosBlendShapeFrame is a CLASS, so `new PhosBlendShapeFrame[frameCount]` leaves every slot NULL.
        // Callers (PhosMesh.GetBlendShape, PhosVertex blendshape access) write straight into
        // Frames[i].positions/normals/tangents without null-checking, so instantiate each frame here or they NRE.
        // This was the "MeshDecoder: Failed to decode mesh - Object reference not set" crash on any model with
        // morphs/blendshapes (e.g. a facial-expression avatar). -xlinka
        Frames = new PhosBlendShapeFrame[frameCount];
        for (int i = 0; i < frameCount; i++)
            Frames[i] = new PhosBlendShapeFrame();
    }
}

/// <summary>
/// Single frame of a blend shape.
/// Stores delta values that are added to base vertex data.
/// </summary>
public class PhosBlendShapeFrame
{
    /// <summary>Position deltas (added to vertex positions)</summary>
    public float3[] positions = Array.Empty<float3>();

    /// <summary>Normal deltas (added to vertex normals)</summary>
    public float3[] normals = Array.Empty<float3>();

    /// <summary>Tangent deltas (added to vertex tangents)</summary>
    public float3[] tangents = Array.Empty<float3>();

    public void SetNormalDelta(int index, float3 delta)
    {
        if (normals.Length == 0)
            normals = new float3[positions.Length];
        normals[index] = delta;
    }

    public void SetTangentDelta(int index, float3 delta)
    {
        if (tangents.Length == 0)
            tangents = new float3[positions.Length];
        tangents[index] = delta;
    }
}
