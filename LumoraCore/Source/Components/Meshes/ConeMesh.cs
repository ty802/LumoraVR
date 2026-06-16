// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

/// <summary>
/// Procedural cone (conical-frustum) mesh component. Generates cone geometry from RadiusBase,
/// RadiusTop, Height and Segments. RadiusTop = 0 gives a true cone tapering to a point.
/// </summary>
public class ConeMesh : ProceduralMesh
{
    // Sync Fields

    /// <summary>Radius at the base (bottom) of the cone</summary>
    public readonly Sync<float> RadiusBase;

    /// <summary>Radius at the top (0 = a point)</summary>
    public readonly Sync<float> RadiusTop;

    /// <summary>Height of the cone</summary>
    public readonly Sync<float> Height;

    /// <summary>Number of radial segments</summary>
    public readonly Sync<int> Segments;

    /// <summary>UV scale for texture mapping</summary>
    public readonly Sync<float2> UVScale;

    // Private State

    private PhosCone? cone;
    private float _radiusBase;
    private float _radiusTop;
    private float _height;
    private int _segments;
    private float2 _uvScale;

    // Constructor

    public ConeMesh()
    {
        RadiusBase = new Sync<float>(this, 0.5f);
        RadiusTop = new Sync<float>(this, 0f);
        Height = new Sync<float>(this, 1f);
        Segments = new Sync<int>(this, 32);
        UVScale = new Sync<float2>(this, new float2(1f, 1f));
    }

    // Lifecycle

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(RadiusBase);
        SubscribeToChanges(RadiusTop);
        SubscribeToChanges(Height);
        SubscribeToChanges(Segments);
        SubscribeToChanges(UVScale);
    }

    // Mesh Generation

    protected override void PrepareAssetUpdateData()
    {
        _radiusBase = RadiusBase.Value;
        _radiusTop = RadiusTop.Value;
        _height = Height.Value;
        _segments = System.Math.Max(3, Segments.Value);
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool segmentsChanged = cone != null && cone.Segments != _segments;
        uploadHint[MeshUploadHint.Flag.Geometry] = cone == null || segmentsChanged;

        if (cone == null || segmentsChanged)
        {
            if (cone != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            cone = new PhosCone(submesh, _segments);
        }

        cone.RadiusBase = _radiusBase;
        cone.RadiusTop = _radiusTop;
        cone.Height = _height;
        cone.UVScale = _uvScale;
        cone.Update();
    }

    protected override void ClearMeshData()
    {
        cone = null;
    }
}
