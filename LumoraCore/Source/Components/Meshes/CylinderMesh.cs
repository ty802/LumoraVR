// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

/// <summary>
/// Procedural cylinder mesh component.
/// Generates cylinder geometry based on Radius, Height, and Segments properties.
/// </summary>
public class CylinderMesh : ProceduralMesh
{
    // Sync Fields

    /// <summary>Radius of the cylinder</summary>
    public readonly Sync<float> Radius;

    /// <summary>Height of the cylinder</summary>
    public readonly Sync<float> Height;

    /// <summary>Number of radial segments</summary>
    public readonly Sync<int> Segments;

    /// <summary>UV scale for texture mapping</summary>
    public readonly Sync<float2> UVScale;

    /// <summary>Whether the top/bottom caps are generated (off = open tube).</summary>
    public readonly Sync<bool> Caps;

    // Private State

    private PhosCylinder? cylinder;
    private float _radius;
    private float _height;
    private int _segments;
    private float2 _uvScale;
    private bool _caps;

    // Constructor

    public CylinderMesh()
    {
        Radius = new Sync<float>(this, 1f);
        Height = new Sync<float>(this, 1f);
        Segments = new Sync<int>(this, 32);
        UVScale = new Sync<float2>(this, new float2(1f, 1f));
        Caps = new Sync<bool>(this, true);
    }

    // Lifecycle

    public override void OnAwake()
    {
        base.OnAwake();

        // Subscribe to property changes
        SubscribeToChanges(Radius);
        SubscribeToChanges(Height);
        SubscribeToChanges(Segments);
        SubscribeToChanges(UVScale);
        SubscribeToChanges(Caps);
    }

    // Mesh Generation

    protected override void PrepareAssetUpdateData()
    {
        // Copy sync values to local variables (thread-safe)
        _radius = Radius.Value;
        _height = Height.Value;
        _segments = Segments.Value;
        _uvScale = UVScale.Value;
        _caps = Caps.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        // Segments and caps are baked into the geometry layout - changing either rebuilds
        bool rebuild = cylinder != null && (cylinder.Segments != _segments || cylinder.Caps != _caps);

        uploadHint[MeshUploadHint.Flag.Geometry] = cylinder == null || rebuild;

        if (cylinder == null || rebuild)
        {
            // Clear existing mesh data if rebuilding
            if (cylinder != null)
            {
                mesh.Clear();
            }

            // Create submesh and cylinder
            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            cylinder = new PhosCylinder(submesh, _segments, _caps);
        }

        // Update cylinder properties
        cylinder.Radius = _radius;
        cylinder.Height = _height;
        cylinder.UVScale = _uvScale;

        // Regenerate vertex data
        cylinder.Update();
    }

    protected override void ClearMeshData()
    {
        cylinder = null;
    }
}
