// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// In the local XY plane facing +Z: a ribbon running along Y with an optional half-round cap at each
// end. Length measures between the two cap centres, so the outer extent is Length + Width once the
// caps are rounded.
[ComponentCategory("Assets/Procedural Meshes")]
public class StripeMesh : ProceduralMesh
{
    public readonly Sync<float> Width;

    public readonly Sync<float> Length;

    public readonly Sync<bool> RoundedEnds;

    // ignored when the ends are square
    [Range(2f, PhosStripe.MaxCapSegments, "0")]
    public readonly Sync<int> CapSegments;

    public readonly Sync<bool> DualSided;

    public readonly Sync<float2> UVScale;

    private PhosStripe? _stripe;
    private float _width;
    private float _length;
    private int _capSegments;
    private bool _dualSided;
    private float2 _uvScale;

    public StripeMesh()
    {
        Width = new Sync<float>(this, 0.1f);
        Length = new Sync<float>(this, 1f);
        RoundedEnds = new Sync<bool>(this, true);
        CapSegments = new Sync<int>(this, 8);
        DualSided = new Sync<bool>(this, false);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Width);
        SubscribeToChanges(Length);
        SubscribeToChanges(RoundedEnds);
        SubscribeToChanges(CapSegments);
        SubscribeToChanges(DualSided);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _width = Width.Value;
        _length = Length.Value;
        // A square end is the same fan with the arc collapsed to its two corners, so the two shapes
        // share one code path and only the cap subdivision differs.
        _capSegments = RoundedEnds.Value
            ? System.Math.Clamp(CapSegments.Value, 2, PhosStripe.MaxCapSegments)
            : 1;
        _dualSided = DualSided.Value;
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool rebuild = _stripe != null
            && (_stripe.CapSegments != _capSegments || _stripe.DualSided != _dualSided);
        uploadHint[MeshUploadHint.Flag.Geometry] = _stripe == null || rebuild;

        if (_stripe == null || rebuild)
        {
            if (_stripe != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _stripe = new PhosStripe(submesh, _capSegments, _dualSided);
        }

        _stripe.Width = _width;
        _stripe.Length = _length;
        _stripe.UVScale = _uvScale;
        _stripe.Update();
    }

    protected override void ClearMeshData()
    {
        _stripe = null;
    }
}
