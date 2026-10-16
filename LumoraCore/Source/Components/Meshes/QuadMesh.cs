// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

[ComponentCategory("Assets/Procedural Meshes")]
public class QuadMesh : ProceduralMesh
{
    public readonly Sync<floatQ> Rotation;

    public readonly Sync<float2> Size;

    public readonly Sync<float2> UVScale;

    public readonly Sync<bool> ScaleUVWithSize;

    public readonly Sync<float2> UVOffset;

    public readonly Sync<bool> DualSided;

    public readonly Sync<bool> UseVertexColors;

    public readonly Sync<color> UpperLeftColor;

    public readonly Sync<color> UpperRightColor;

    public readonly Sync<color> LowerLeftColor;

    public readonly Sync<color> LowerRightColor;

    private PhosQuad? quad;
    private PhosQuad? otherSide;

    private floatQ _rotation;
    private float2 _size;
    private float2 _uvScale;
    private bool _scaleUVWithSize;
    private float2 _uvOffset;
    private bool _dualSided;
    private bool _useColors;
    private color _ulColor, _urColor, _llColor, _lrColor;

    public QuadMesh()
    {
        Rotation = new Sync<floatQ>(this, floatQ.Identity);
        Size = new Sync<float2>(this, float2.One);
        UVScale = new Sync<float2>(this, float2.One);
        ScaleUVWithSize = new Sync<bool>(this, false);
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
        SubscribeToChanges(UVScale);
        SubscribeToChanges(ScaleUVWithSize);
        SubscribeToChanges(UVOffset);
        SubscribeToChanges(DualSided);
        SubscribeToChanges(UseVertexColors);
        SubscribeToChanges(UpperLeftColor);
        SubscribeToChanges(UpperRightColor);
        SubscribeToChanges(LowerLeftColor);
        SubscribeToChanges(LowerRightColor);
    }

    protected override void PrepareAssetUpdateData()
    {
        // Copy sync values to local variables (thread-safe)
        _rotation = Rotation.Value;
        _size = Size.Value;
        _uvScale = UVScale.Value;
        _scaleUVWithSize = ScaleUVWithSize.Value;
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
        bool geometryChanged = false;

        if (quad == null)
        {
            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            quad = new PhosQuad(submesh);
            geometryChanged = true;
        }

        // Color mode changes the vertex data.
        if (quad.UseColors != _useColors)
        {
            quad.UseColors = _useColors;
            geometryChanged = true;
        }

        quad.Size = _size;
        quad.Rotation = _rotation;

        if (_scaleUVWithSize)
        {
            quad.UVScale = _uvScale * _size;
        }
        else
        {
            quad.UVScale = _uvScale;
        }

        quad.UVOffset = _uvOffset;

        quad.UpperLeftColor = _ulColor;
        quad.UpperRightColor = _urColor;
        quad.LowerLeftColor = _llColor;
        quad.LowerRightColor = _lrColor;

        quad.Update();

        if (_dualSided)
        {
            if (otherSide == null)
            {
                var submesh = new PhosTriangleSubmesh(mesh);
                mesh.Submeshes.Add(submesh);
                otherSide = new PhosQuad(submesh);
                geometryChanged = true;
            }

            if (otherSide.UseColors != _useColors)
            {
                otherSide.UseColors = _useColors;
                geometryChanged = true;
            }

            otherSide.Size = _size;

            otherSide.Rotation = _rotation * floatQ.AxisAngleRad(float3.Up, MathF.PI);

            if (_scaleUVWithSize)
            {
                otherSide.UVScale = _uvScale * _size;
            }
            else
            {
                otherSide.UVScale = _uvScale;
            }

            otherSide.UVOffset = _uvOffset;

            otherSide.UpperLeftColor = _ulColor;
            otherSide.UpperRightColor = _urColor;
            otherSide.LowerLeftColor = _llColor;
            otherSide.LowerRightColor = _lrColor;

            otherSide.Update();
        }
        else if (otherSide != null)
        {
            // Dual sided switched off. Removing the back side's vertices moves the mesh's vertex version on,
            // which invalidates the front quad's handles as well, and its next update threw. The front is
            // built again on its own instead. -xlinka
            mesh.Clear();
            quad = null;
            otherSide = null;
            UpdateMeshData(mesh);
            return;
        }

        uploadHint[MeshUploadHint.Flag.Geometry] = geometryChanged;
    }

    protected override void ClearMeshData()
    {
        quad = null;
        otherSide = null;
    }

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

    public float3 Facing
    {
        get
        {
            floatQ q = Rotation.Value;
            return q * float3.Backward;
        }
        set
        {
            Rotation.Value = floatQ.LookRotation(-value, float3.Up);
        }
    }
}
