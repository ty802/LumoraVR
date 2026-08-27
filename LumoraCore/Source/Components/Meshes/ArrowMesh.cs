// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// a cylinder shaft along +Y from the origin with a cone tip on top; total length = ShaftLength + TipLength
public class ArrowMesh : ProceduralMesh
{
    // Sync Fields

    public readonly Sync<float> ShaftRadius;

    public readonly Sync<float> ShaftLength;

    public readonly Sync<float> TipRadius;

    public readonly Sync<float> TipLength;

    public readonly Sync<int> Segments;

    // Private State

    private PhosArrow? arrow;
    private float _shaftRadius;
    private float _shaftLength;
    private float _tipRadius;
    private float _tipLength;
    private int _segments;

    // Constructor

    public ArrowMesh()
    {
        ShaftRadius = new Sync<float>(this, 0.007f);
        ShaftLength = new Sync<float>(this, 0.12f);
        TipRadius = new Sync<float>(this, 0.02f);
        TipLength = new Sync<float>(this, 0.03f);
        Segments = new Sync<int>(this, 12);
    }

    // Lifecycle

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(ShaftRadius);
        SubscribeToChanges(ShaftLength);
        SubscribeToChanges(TipRadius);
        SubscribeToChanges(TipLength);
        SubscribeToChanges(Segments);
    }

    // Mesh Generation

    protected override void PrepareAssetUpdateData()
    {
        _shaftRadius = ShaftRadius.Value;
        _shaftLength = ShaftLength.Value;
        _tipRadius = TipRadius.Value;
        _tipLength = TipLength.Value;
        _segments = Segments.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        // Segment count is baked into the shape's topology - changing it rebuilds
        bool rebuild = arrow != null && arrow.Segments != System.Math.Max(3, _segments);

        uploadHint[MeshUploadHint.Flag.Geometry] = arrow == null || rebuild;

        if (arrow == null || rebuild)
        {
            if (arrow != null)
            {
                mesh.Clear();
            }

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            arrow = new PhosArrow(submesh, _segments);
        }

        arrow.ShaftRadius = _shaftRadius;
        arrow.ShaftLength = _shaftLength;
        arrow.TipRadius = _tipRadius;
        arrow.TipLength = _tipLength;

        arrow.Update();
    }

    protected override void ClearMeshData()
    {
        arrow = null;
    }
}
