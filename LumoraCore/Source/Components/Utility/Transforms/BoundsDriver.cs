// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Utility;

// Drives a size and a centre from the bounds of one component's geometry, in this slot's space: a collider
// that hugs a label, a backing plate that grows with its text. The source is a procedural mesh (a text
// renderer included), a mesh renderer or a collider.
//
// Measures only when the geometry can have moved. A procedural mesh is watched through its clear count and
// vertex count, which every rebuild moves; anything else through the source's own change events. The measure
// itself is one pass over the mesh, so an idle driver costs three compares a frame. -xlinka
[ComponentCategory("Utility/Transforms")]
[DefaultUpdateOrder(100)]
public class BoundsDriver : Component
{
    public readonly SyncRef<Component> Source;

    public readonly FieldDrive<float3> Size;

    public readonly FieldDrive<float3> Center;

    // Added to the measured size after scaling.
    public readonly Sync<float3> Padding;

    public readonly Sync<float3> Scale;

    private Component? _watched;
    private object? _stampMesh;
    private int _stampClears = -1;
    private int _stampVertices = -1;
    private bool _dirty = true;

    public BoundsDriver()
    {
        Source = new SyncRef<Component>(this);
        Size = new FieldDrive<float3>(this) { LocalValueOnly = true };
        Center = new FieldDrive<float3>(this) { LocalValueOnly = true };
        Padding = new Sync<float3>(this, float3.Zero);
        Scale = new Sync<float3>(this, float3.One);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        _dirty = true;
    }

    public override void OnDestroy()
    {
        Watch(null);
        base.OnDestroy();
    }

    public override void OnUpdate(float delta)
    {
        if (!Size.IsLinkValid && !Center.IsLinkValid)
            return;

        var source = Source.Target;
        if (!ReferenceEquals(source, _watched))
            Watch(source);

        if (!Stamp(source) && !_dirty)
            return;
        _dirty = false;

        if (source != null && !source.IsDestroyed && Measure(source, out var bounds))
        {
            Size.SetValue(Finite(bounds.Size * Scale.Value + Padding.Value));
            Center.SetValue(Finite(bounds.Center));
        }
        else
        {
            Size.SetValue(Finite(Padding.Value));
            Center.SetValue(float3.Zero);
        }
    }

    // True when the watched geometry moved since the last measure.
    private bool Stamp(Component? source)
    {
        object? mesh = null;
        int clears = 0;
        int vertices = 0;
        switch (source)
        {
            case ProceduralMesh procedural when procedural.PhosMesh is { } phos:
                mesh = phos;
                clears = phos.ClearCount;
                vertices = phos.VertexCount;
                break;
            case MeshRenderer renderer:
                mesh = renderer.Mesh.Target switch
                {
                    ProceduralMesh procedural => procedural.PhosMesh,
                    Lumora.Core.Assets.MeshProvider provider => provider.Asset,
                    _ => null,
                };
                if (mesh is PhosMesh rendered)
                {
                    clears = rendered.ClearCount;
                    vertices = rendered.VertexCount;
                }
                break;
            case SkinnedMeshRenderer skinned:
                mesh = skinned.MeshAsset.Asset;
                break;
        }

        if (ReferenceEquals(mesh, _stampMesh) && clears == _stampClears && vertices == _stampVertices)
            return false;
        _stampMesh = mesh;
        _stampClears = clears;
        _stampVertices = vertices;
        return true;
    }

    private bool Measure(Component source, out BoundingBox bounds)
    {
        BoundingBox local;
        bool found = source switch
        {
            ProceduralMesh procedural => Valid(local = procedural.GetBoundingBox()),
            Collider collider => Valid(local = collider.GetLocalBounds()),
            _ => SlotBoundsHelper.TryGetRendererLocalBounds(source, out local),
        };
        bounds = local;
        if (!found)
            return false;

        var from = source.Slot;
        if (from == null || ReferenceEquals(from, Slot))
            return true;

        bounds = BoundingBox.Empty;
        for (int i = 0; i < 8; i++)
        {
            var corner = new float3(
                (i & 1) == 0 ? local.Min.x : local.Max.x,
                (i & 2) == 0 ? local.Min.y : local.Max.y,
                (i & 4) == 0 ? local.Min.z : local.Max.z);
            bounds.Encapsulate(Slot.GlobalPointToLocal(from.LocalPointToGlobal(corner)));
        }
        return true;
    }

    private void Watch(Component? source)
    {
        if (_watched != null)
            _watched.Changed -= OnSourceChanged;
        _watched = source;
        if (_watched != null)
            _watched.Changed += OnSourceChanged;
        _dirty = true;
    }

    private void OnSourceChanged(IChangeable changed) => _dirty = true;

    private static bool Valid(in BoundingBox box)
        => box.Min.x <= box.Max.x && box.Min.y <= box.Max.y && box.Min.z <= box.Max.z;

    private static float3 Finite(in float3 value) => new(
        float.IsFinite(value.x) ? value.x : 0f,
        float.IsFinite(value.y) ? value.y : 0f,
        float.IsFinite(value.z) ? value.z : 0f);
}
