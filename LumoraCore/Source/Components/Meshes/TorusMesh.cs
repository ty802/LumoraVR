// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// The ring lies flat in the local XZ plane around the local Y axis, so it reads as a rotation ring by default
[ComponentCategory("Assets/Procedural Meshes")]
public class TorusMesh : ProceduralMesh
{
    // Sync Fields

    // center to the middle of the tube
    public readonly Sync<float> MajorRadius;

    public readonly Sync<float> MinorRadius;

    public readonly Sync<int> MajorSegments;

    public readonly Sync<int> MinorSegments;

    public readonly Sync<float2> UVScale;

    // Private State

    private PhosTorus? torus;
    private float _majorRadius;
    private float _minorRadius;
    private int _majorSegments;
    private int _minorSegments;
    private float2 _uvScale;

    // Constructor

    public TorusMesh()
    {
        MajorRadius = new Sync<float>(this, 0.25f);
        MinorRadius = new Sync<float>(this, 0.005f);
        MajorSegments = new Sync<int>(this, 48);
        MinorSegments = new Sync<int>(this, 8);
        UVScale = new Sync<float2>(this, float2.One);
    }

    // Lifecycle

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(MajorRadius);
        SubscribeToChanges(MinorRadius);
        SubscribeToChanges(MajorSegments);
        SubscribeToChanges(MinorSegments);
        SubscribeToChanges(UVScale);
    }

    // Mesh Generation

    protected override void PrepareAssetUpdateData()
    {
        _majorRadius = MajorRadius.Value;
        _minorRadius = MinorRadius.Value;
        _majorSegments = MajorSegments.Value;
        _minorSegments = MinorSegments.Value;
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        // Segment counts are baked into the shape's topology - changing either rebuilds
        bool rebuild = torus != null
            && (torus.MajorSegments != System.Math.Max(3, _majorSegments)
                || torus.MinorSegments != System.Math.Max(3, _minorSegments));

        uploadHint[MeshUploadHint.Flag.Geometry] = torus == null || rebuild;

        if (torus == null || rebuild)
        {
            if (torus != null)
            {
                mesh.Clear();
            }

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            torus = new PhosTorus(submesh, _majorSegments, _minorSegments);
        }

        torus.MajorRadius = _majorRadius;
        torus.MinorRadius = _minorRadius;
        torus.UVScale = _uvScale;

        torus.Update();
    }

    protected override void ClearMeshData()
    {
        torus = null;
    }
}
