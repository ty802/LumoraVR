// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Godot.Hooks;

[ImplementableHook(typeof(RespawnPlane))]
public class RespawnPlaneHook : ComponentHook<RespawnPlane>
{
    private MeshInstance3D _visualMesh = null!;
    private PlaneMesh _planeMesh = null!;
    private StandardMaterial3D _visualMaterial = null!;
    private MeshInstance3D _debugMesh = null!;
    private StandardMaterial3D _debugMaterial = null!;
    private float2 _lastDebugSize;
    private bool _hasDebugSize;

    public static IHook<RespawnPlane> Constructor()
    {
        return new RespawnPlaneHook();
    }

    public override void Initialize()
    {
        base.Initialize();
        CreateVisuals();
        UpdateVisuals();
    }


    public override void ApplyChanges()
    {
        UpdateVisuals();
    }

    private void CreateVisuals()
    {
        _visualMesh = new MeshInstance3D { Name = "RespawnPlaneVisual" };
        _planeMesh = new PlaneMesh();
        _visualMesh.Mesh = _planeMesh;

        _visualMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _visualMesh.MaterialOverride = _visualMaterial;

        _debugMesh = new MeshInstance3D { Name = "RespawnPlaneDebug" };
        _debugMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _debugMesh.MaterialOverride = _debugMaterial;

        attachedNode.AddChild(_visualMesh);
        attachedNode.AddChild(_debugMesh);
    }

    private void UpdateVisuals()
    {
        var size = Owner.Size.Value;
        if (_planeMesh != null)
        {
            _planeMesh.Size = new Vector2(size.x, size.y);
        }

        if (_visualMaterial != null)
        {
            var color = Owner.VisualColor.Value;
            _visualMaterial.AlbedoColor = new Color(color.r, color.g, color.b, color.a);
        }

        if (_visualMesh != null)
        {
            _visualMesh.Visible = Owner.ShowVisual.Value;
        }

        if (_debugMesh != null)
        {
            _debugMesh.Visible = Owner.ShowDebug.Value;
            _debugMesh.Position = new Vector3(0f, 0.02f, 0f);

            if (!_hasDebugSize || _lastDebugSize.x != size.x || _lastDebugSize.y != size.y)
            {
                _debugMesh.Mesh = BuildPlaneWireMesh(new Vector2(size.x, size.y));
                _lastDebugSize = size;
                _hasDebugSize = true;
            }

            if (_debugMaterial != null)
            {
                var debugColor = Owner.DebugColor.Value;
                _debugMaterial.AlbedoColor = new Color(debugColor.r, debugColor.g, debugColor.b, debugColor.a);
            }
        }
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld)
        {
            _visualMesh?.QueueFree();
            _debugMesh?.QueueFree();
            _visualMaterial?.Dispose();
            _debugMaterial?.Dispose();
            _planeMesh?.Dispose();
        }

        _visualMesh = null!;
        _debugMesh = null!;
        _visualMaterial = null!;
        _debugMaterial = null!;
        _planeMesh = null!;
        _hasDebugSize = false;

        base.Destroy(destroyingWorld);
    }

    private static ArrayMesh? BuildPlaneWireMesh(Vector2 size)
    {
        float hx = size.X * 0.5f;
        float hz = size.Y * 0.5f;

        var corners = new[]
        {
            new Vector3(-hx, 0f, -hz),
            new Vector3(hx, 0f, -hz),
            new Vector3(hx, 0f, hz),
            new Vector3(-hx, 0f, hz)
        };

        var vertices = new System.Collections.Generic.List<Vector3>();
        var indices = new System.Collections.Generic.List<int>();

        AddLine(vertices, indices, corners[0], corners[1]);
        AddLine(vertices, indices, corners[1], corners[2]);
        AddLine(vertices, indices, corners[2], corners[3]);
        AddLine(vertices, indices, corners[3], corners[0]);

        return BuildLineMesh(vertices, indices);
    }

    private static ArrayMesh? BuildLineMesh(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> indices)
    {
        if (vertices.Count == 0) return null;
        var mesh = new ArrayMesh();
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
    }

    private static void AddLine(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> indices, Vector3 from, Vector3 to)
    {
        int start = vertices.Count;
        vertices.Add(from);
        vertices.Add(to);
        indices.Add(start);
        indices.Add(start + 1);
    }
}

