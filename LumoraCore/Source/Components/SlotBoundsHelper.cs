// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components;

// Visual bounds of a slot's subtree: unions the geometry bounds of every enabled mesh renderer
// (skinned included). Colliders serve as the fallback when nothing renders, so invisible objects
// still get usable bounds.
public static class SlotBoundsHelper
{
    public static bool TryComputeWorldBounds(Slot slot, out BoundingBox bounds)
        => TryComputeBounds(slot, space: null, out bounds, sources: null, out _);

    // expressed in slot's own local space, so moving or rotating the slot does not change the answer
    // and the box can be cached across frames; sources receives every component the measurement
    // actually read, which is the exact set worth watching for changes to know when the cache went stale
    public static bool TryComputeLocalBounds(Slot slot, out BoundingBox bounds, List<Component>? sources = null)
        => TryComputeBounds(slot, space: slot, out bounds, sources, out _);

    // As above, and reports whether anything under the slot can move the geometry WITHOUT changing
    // the structure or firing a component change: an animator driving child slots, a skinned mesh on
    // live bones, a soft body rewriting its own vertices. None of those invalidate a cached box, so a
    // caller that wants to stay correct on them has to re-measure on a clock, and this is what tells
    // it whether that clock is worth running at all. Free to ask: the walk that measures the bounds
    // is already visiting every component. -xlinka
    public static bool TryComputeLocalBounds(Slot slot, out BoundingBox bounds, List<Component>? sources,
        out bool animated)
        => TryComputeBounds(slot, space: slot, out bounds, sources, out animated);

    private static bool TryComputeBounds(Slot slot, Slot? space, out BoundingBox bounds,
        List<Component>? sources, out bool animated)
    {
        bounds = default;
        bounds.MakeEmpty();
        sources?.Clear();
        animated = false;
        if (slot == null || slot.IsDestroyed)
            return false;

        AccumulateRenderers(slot, space, ref bounds, sources, ref animated);
        if (!IsValid(bounds))
            AccumulateColliders(slot, space, ref bounds, sources);
        return IsValid(bounds);
    }

    // Cheap presence test, not a "is it moving right now" test: an animator that is enabled and idle
    // still counts. Getting that wrong costs one extra measurement every half second on a selected
    // object, and getting the other direction wrong leaves a stale box on screen. -xlinka
    private static bool IsAnimatedSource(Component component)
        => component is SkinnedMeshRenderer or Animator or SquishyBody;

    private static bool IsValid(in BoundingBox box)
        => box.Min.x <= box.Max.x && box.Min.y <= box.Max.y && box.Min.z <= box.Max.z;

    // Inactive branches below the queried slot are pruned by their OWN flag, so the result is the
    // same whether or not some far ancestor happens to be disabled right now. - xlinka
    private static void AccumulateRenderers(Slot slot, Slot? space, ref BoundingBox bounds,
        List<Component>? sources, ref bool animated, bool isRoot = true)
    {
        if (slot.IsDestroyed || (!isRoot && !slot.ActiveSelf.Value))
            return;

        foreach (var component in slot.Components)
        {
            if (component.IsDestroyed || !component.Enabled.Value)
                continue;
            if (IsAnimatedSource(component))
                animated = true;
            if (!TryGetRendererLocalBounds(component, out var local))
                continue;
            Encapsulate(slot, space, local, ref bounds);
            sources?.Add(component);
        }
        foreach (var child in slot.Children)
            AccumulateRenderers(child, space, ref bounds, sources, ref animated, isRoot: false);
    }

    private static void AccumulateColliders(Slot slot, Slot? space, ref BoundingBox bounds,
        List<Component>? sources, bool isRoot = true)
    {
        if (slot.IsDestroyed || (!isRoot && !slot.ActiveSelf.Value))
            return;

        foreach (var component in slot.Components)
        {
            if (component is Collider { IsDestroyed: false } collider && collider.Enabled.Value)
            {
                var local = collider.GetLocalBounds();
                if (!IsValid(local))
                    continue;
                Encapsulate(slot, space, local, ref bounds);
                sources?.Add(collider);
            }
        }
        foreach (var child in slot.Children)
            AccumulateColliders(child, space, ref bounds, sources, isRoot: false);
    }

    // in its own slot's local space; false when the component is not a renderer, or is one whose mesh
    // has not arrived yet
    public static bool TryGetRendererLocalBounds(Component component, out BoundingBox bounds)
    {
        switch (component)
        {
            case MeshRenderer renderer:
                switch (renderer.Mesh.Target)
                {
                    case ProceduralMesh procedural:
                        bounds = procedural.GetBoundingBox();
                        return IsValid(bounds);
                    case MeshProvider provider when provider.Asset != null:
                        bounds = provider.Asset.Bounds;
                        return IsValid(bounds);
                }
                break;

            case SkinnedMeshRenderer skinned when skinned.MeshAsset.Asset != null:
                // Bind-pose bounds; live deformation is not baked into the asset.
                bounds = skinned.MeshAsset.Asset.Bounds;
                return IsValid(bounds);
        }
        bounds = default;
        return false;
    }

    // EXACT GEOMETRY

    // Every vertex of every enabled mesh renderer at and under the slot, carried into space's local frame,
    // skinned meshes at their bind pose. The renderer bounds above are boxes around boxes, and those grow with
    // every rotation between a mesh and the space they are measured in; fitting a collider wants the geometry
    // itself. A switched-off child does not count, the same as the renderer walk. -xlinka
    public static void ForEachGeometryPoint(Slot slot, Slot space, Action<float3> visit)
    {
        if (slot == null || slot.IsDestroyed || space == null || space.IsDestroyed)
            return;
        VisitGeometry(slot, space, visit, isRoot: true);
    }

    public static bool TryComputeGeometryBounds(Slot slot, Slot space, out BoundingBox bounds)
    {
        var box = BoundingBox.Empty;
        ForEachGeometryPoint(slot, space, point => box.Encapsulate(point));
        bounds = box;
        return IsValid(box);
    }

    // The sphere around the geometry from the middle of its box: every vertex inside, no further out than
    // the farthest one.
    public static bool TryFitSphere(Slot slot, out float3 centre, out float radius)
    {
        radius = 0f;
        if (!TryComputeGeometryBounds(slot, slot, out var bounds))
        {
            centre = float3.Zero;
            return false;
        }
        var middle = bounds.Center;
        float farthest = 0f;
        ForEachGeometryPoint(slot, slot, point => farthest = MathF.Max(farthest, (point - middle).LengthSquared));
        centre = middle;
        radius = MathF.Sqrt(farthest);
        return radius > 0f;
    }

    // An upright cylinder around the geometry along the slot's Y axis: the box's middle, the farthest vertex
    // from that axis for the radius, the box's height for the height.
    public static bool TryFitUpright(Slot slot, out float3 centre, out float radius, out float height)
    {
        radius = 0f;
        height = 0f;
        if (!TryComputeGeometryBounds(slot, slot, out var bounds))
        {
            centre = float3.Zero;
            return false;
        }
        var middle = bounds.Center;
        float farthest = 0f;
        ForEachGeometryPoint(slot, slot, point =>
        {
            float dx = point.x - middle.x, dz = point.z - middle.z;
            farthest = MathF.Max(farthest, dx * dx + dz * dz);
        });
        centre = middle;
        radius = MathF.Sqrt(farthest);
        height = bounds.Size.y;
        return radius > 0f || height > 0f;
    }

    private static void VisitGeometry(Slot slot, Slot space, Action<float3> visit, bool isRoot)
    {
        if (slot.IsDestroyed || (!isRoot && !slot.ActiveSelf.Value))
            return;

        foreach (var component in slot.Components)
        {
            if (component.IsDestroyed || !component.Enabled.Value)
                continue;
            var mesh = GeometryOf(component);
            if (mesh == null || mesh.VertexCount == 0)
                continue;
            var positions = mesh.RawPositions;
            int count = mesh.VertexCount;
            if (ReferenceEquals(slot, space))
            {
                for (int i = 0; i < count; i++)
                    visit(positions[i]);
            }
            else
            {
                var toSpace = slot.GetLocalToSpaceMatrix(space);
                for (int i = 0; i < count; i++)
                    visit(toSpace.MultiplyPoint(positions[i]));
            }
        }
        foreach (var child in slot.Children)
            VisitGeometry(child, space, visit, isRoot: false);
    }

    private static PhosMesh? GeometryOf(Component component) => component switch
    {
        MeshRenderer renderer => ConvexHullPointSource.GetMesh(renderer.Mesh.Target),
        SkinnedMeshRenderer skinned => skinned.MeshAsset.Asset?.MeshData,
        _ => null,
    };

    // Transform all 8 corners rather than min/max alone, so rotated slots still produce a correct
    // box. A null space means world space; otherwise the corners land in that slot's local space.
    private static void Encapsulate(Slot slot, Slot? space, in BoundingBox local, ref BoundingBox bounds)
    {
        bool sameSpace = ReferenceEquals(slot, space);
        for (int i = 0; i < 8; i++)
        {
            var corner = new float3(
                (i & 1) == 0 ? local.Min.x : local.Max.x,
                (i & 2) == 0 ? local.Min.y : local.Max.y,
                (i & 4) == 0 ? local.Min.z : local.Max.z);
            if (sameSpace)
                bounds.Encapsulate(corner);
            else
            {
                var global = slot.LocalPointToGlobal(corner);
                bounds.Encapsulate(space == null ? global : space.GlobalPointToLocal(global));
            }
        }
    }
}
