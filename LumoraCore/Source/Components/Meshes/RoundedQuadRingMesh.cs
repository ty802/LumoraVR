// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// The outline band of a RoundedQuadMesh: same rounded rect, hollow, extruded RimWidth metres OUTWARD
// from Size. Set it to the same Size and CornerRadius as the panel it trims and it hugs the panel
// edge exactly, with no overdraw behind the panel and no second alpha layer to darken it.
//
// Its own component rather than a mode on RoundedQuadMesh: half the fields would be dead in whichever
// mode you were not in, which is exactly the inspector row nobody can read, and "rim" is a thing you
// go looking for by name in the component list. -xlinka
[ComponentCategory("Assets/Procedural Meshes")]
public class RoundedQuadRingMesh : ProceduralMesh
{
    public readonly Sync<floatQ> Rotation;

    // The INNER rect: the hole the band goes around.
    public readonly Sync<float2> Size;

    public readonly Sync<float> CornerRadius;

    // Band width in metres, outward from Size.
    public readonly Sync<float> RimWidth;

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
    private float _rimWidth;
    private int _cornerSegments;
    private float2 _uvScale;
    private float2 _uvOffset;
    private bool _dualSided;
    private bool _useColors;
    private color _ulColor, _urColor, _llColor, _lrColor;

    public RoundedQuadRingMesh()
    {
        Rotation = new Sync<floatQ>(this, floatQ.Identity);
        Size = new Sync<float2>(this, float2.One);
        CornerRadius = new Sync<float>(this, 0.1f);
        RimWidth = new Sync<float>(this, 0.01f);
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
        SubscribeToChanges(RimWidth);
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
        _rimWidth = RimWidth.Value;
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
        bool rebuild = _rect != null
            && (_rect.CornerSegments != _cornerSegments || _rect.DualSided != _dualSided);
        uploadHint[MeshUploadHint.Flag.Geometry] = _rect == null || rebuild;

        if (_rect == null || rebuild)
        {
            if (_rect != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _rect = new PhosRoundedRect(submesh, _cornerSegments, ring: true, dualSided: _dualSided);
        }

        _rect.Rotation = _rotation;
        _rect.Size = _size;
        _rect.CornerRadius = _cornerRadius;
        _rect.RimWidth = _rimWidth;
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
