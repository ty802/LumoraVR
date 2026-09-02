// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// A flat quad in the local XY plane with rounded corners cut into the GEOMETRY. Drop-in for QuadMesh
// on anything that was faking rounding with a rounded-rect texture: that trick stretches a square
// image over the panel's aspect and the corners come out as ellipses, which is a pill at 4:1 and
// looks nothing like the corner at 1:1. Here the corner is the same arc at every size.
[ComponentCategory("Assets/Procedural Meshes")]
public class RoundedQuadMesh : ProceduralMesh
{
    public readonly Sync<floatQ> Rotation;

    public readonly Sync<float2> Size;

    // Metres, clamped to half the short side so the arcs cannot cross.
    public readonly Sync<float> CornerRadius;

    // Negative follows CornerRadius, which is the default and every existing use. Set it and the two
    // BOTTOM corners take this radius instead. -xlinka
    public readonly Sync<float> BottomCornerRadius;

    [Range(1f, PhosRoundedRect.MaxCornerSegments, "0")]
    public readonly Sync<int> CornerSegments;

    public readonly Sync<float2> UVScale;

    public readonly Sync<float2> UVOffset;

    public readonly Sync<bool> DualSided;

    public readonly Sync<bool> UseVertexColors;

    public readonly Sync<color> UpperLeftColor;

    public readonly Sync<color> UpperRightColor;

    public readonly Sync<color> LowerLeftColor;

    public readonly Sync<color> LowerRightColor;

    private PhosRoundedRect? _rect;
    private floatQ _rotation;
    private float2 _size;
    private float _cornerRadius;
    private float _bottomCornerRadius;
    private int _cornerSegments;
    private float2 _uvScale;
    private float2 _uvOffset;
    private bool _dualSided;
    private bool _useColors;
    private color _ulColor, _urColor, _llColor, _lrColor;

    public RoundedQuadMesh()
    {
        Rotation = new Sync<floatQ>(this, floatQ.Identity);
        Size = new Sync<float2>(this, float2.One);
        CornerRadius = new Sync<float>(this, 0.1f);
        BottomCornerRadius = new Sync<float>(this, -1f);
        CornerSegments = new Sync<int>(this, 6);
        UVScale = new Sync<float2>(this, float2.One);
        UVOffset = new Sync<float2>(this, float2.Zero);
        DualSided = new Sync<bool>(this, false);
        UseVertexColors = new Sync<bool>(this, true);
        UpperLeftColor = new Sync<color>(this, color.White);
        UpperRightColor = new Sync<color>(this, color.White);
        LowerLeftColor = new Sync<color>(this, color.White);
        LowerRightColor = new Sync<color>(this, color.White);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Rotation);
        SubscribeToChanges(Size);
        SubscribeToChanges(CornerRadius);
        SubscribeToChanges(BottomCornerRadius);
        SubscribeToChanges(CornerSegments);
        SubscribeToChanges(UVScale);
        SubscribeToChanges(UVOffset);
        SubscribeToChanges(DualSided);
        SubscribeToChanges(UseVertexColors);
        SubscribeToChanges(UpperLeftColor);
        SubscribeToChanges(UpperRightColor);
        SubscribeToChanges(LowerLeftColor);
        SubscribeToChanges(LowerRightColor);
    }

    // What the mesh actually drew, after the clamp. Read it rather than CornerRadius when you need
    // the corner that is on screen.
    public float EffectiveCornerRadius => PhosRoundedRect.ClampRadius(Size.Value, CornerRadius.Value);

    public color Color
    {
        get => UpperLeftColor.Value;
        set
        {
            UpperLeftColor.Value = value;
            UpperRightColor.Value = value;
            LowerLeftColor.Value = value;
            LowerRightColor.Value = value;
        }
    }

    protected override void PrepareAssetUpdateData()
    {
        _rotation = Rotation.Value;
        _size = Size.Value;
        _cornerRadius = CornerRadius.Value;
        _bottomCornerRadius = BottomCornerRadius.Value;
        _cornerSegments = System.Math.Clamp(CornerSegments.Value, 1, PhosRoundedRect.MaxCornerSegments);
        _uvScale = UVScale.Value;
        _uvOffset = UVOffset.Value;
        _dualSided = DualSided.Value;
        _useColors = UseVertexColors.Value;
        _ulColor = UpperLeftColor.Value;
        _urColor = UpperRightColor.Value;
        _llColor = LowerLeftColor.Value;
        _lrColor = LowerRightColor.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        // Only the segment count and the sidedness move vertices around; size, radius and colour all
        // write into the buffers that are already there.
        bool rebuild = _rect != null
            && (_rect.CornerSegments != _cornerSegments || _rect.DualSided != _dualSided);
        uploadHint[MeshUploadHint.Flag.Geometry] = _rect == null || rebuild;

        if (_rect == null || rebuild)
        {
            if (_rect != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _rect = new PhosRoundedRect(submesh, _cornerSegments, ring: false, dualSided: _dualSided);
        }

        _rect.Rotation = _rotation;
        _rect.Size = _size;
        _rect.CornerRadius = _cornerRadius;
        _rect.BottomCornerRadius = _bottomCornerRadius;
        _rect.UVScale = _uvScale;
        _rect.UVOffset = _uvOffset;
        _rect.UseColors = _useColors;
        _rect.UpperLeftColor = _ulColor;
        _rect.UpperRightColor = _urColor;
        _rect.LowerLeftColor = _llColor;
        _rect.LowerRightColor = _lrColor;
        _rect.Update();
    }

    protected override void ClearMeshData()
    {
        _rect = null;
    }
}
