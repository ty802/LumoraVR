// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// What a field that reads a mesh can be pointed at, and the geometry behind it.
//
// A mesh is a procedural mesh or a mesh asset. A renderer is not a mesh, but it names one, so a field asking for a
// mesh reads a renderer as the mesh it draws: a skinned renderer as its mesh at the bind pose. Fields that do more
// with a renderer than read its mesh (a soft body takes a skinned one's pose and hides what it replaces) keep the
// renderer itself; every other mesh field takes the mesh out of it when it is handed one. -xlinka
public static class MeshSource
{
    public static bool IsMesh(Component? component)
        => component is ProceduralMesh || component is IAssetProvider<MeshDataAsset>;

    public static bool IsRenderer(Component? component)
        => component is MeshRenderer || component is SkinnedMeshRenderer;

    // The mesh behind the target: itself when it is one, the mesh a renderer draws, null when there is none.
    public static Component? MeshOf(Component? component) => component switch
    {
        null => null,
        { IsDestroyed: true } => null,
        MeshRenderer renderer => IsMesh(renderer.Mesh.Target) ? renderer.Mesh.Target : null,
        SkinnedMeshRenderer skinned => skinned.MeshAsset.Target as Component,
        _ => IsMesh(component) ? component : null,
    };

    // The geometry, or null until it exists: a provider decodes its mesh after it is first referenced, and a
    // procedural mesh builds on its first update.
    public static PhosMesh? Geometry(Component? component) => MeshOf(component) switch
    {
        ProceduralMesh procedural => procedural.PhosMesh,
        IAssetProvider<MeshDataAsset> provider => provider.Asset?.MeshData,
        _ => null,
    };

    // What a reader holds a reference on, so the mesh loads at all and its arrival is heard.
    public static IAssetProvider? Provider(Component? component) => MeshOf(component) as IAssetProvider;

    // A mesh field: a component reference read as a mesh. A renderer's, a collider's, a hull's, a deformable mesh's
    // and a soft body's mesh fields are all named after what they read.
    public static bool IsMeshField(ISyncRef? reference)
        => reference is ISyncMember { Name: "Mesh" or "SourceMesh" or "ParticleMesh" } && reference.TargetType == typeof(Component);

    // A mesh field that keeps a renderer as such instead of the mesh it draws.
    public static bool KeepsRenderers(ISyncRef? reference)
    {
        if (reference is not ISyncMember { Name: "SourceMesh" })
            return false;
        IWorldElement? walk = reference as IWorldElement;
        for (int depth = 0; walk != null && depth < 8; depth++)
        {
            if (walk is Component component)
                return component is IMeshSourceReader;
            walk = (walk as SyncElement)?.Parent;
        }
        return false;
    }

    // Why this cannot fill a mesh field, in words for the person who tried; null when it can.
    public static string? Refusal(Component? component, bool keepsRenderers)
    {
        switch (component)
        {
            case null:
                return null;
            case { IsDestroyed: true }:
                return "that is gone";
            case SkinnedMeshRenderer skinned:
                if (skinned.MeshAsset.Target != null)
                    return null;
                return skinned.Vertices.Count > 0
                    ? "an old import with no bind poses; import it again"
                    : "this skinned renderer has no mesh yet";
            case MeshRenderer renderer:
                if (IsMesh(renderer.Mesh.Target))
                    return null;
                return renderer.Mesh.Target == null
                    ? "this renderer has no mesh yet"
                    : $"its renderer draws a {renderer.Mesh.Target.GetType().Name}";
        }
        if (IsMesh(component))
            return null;
        return $"{component.GetType().Name} is not a mesh";
    }

    // A candidate that would feed the field's own component its own output: a deformable mesh built from itself, a
    // soft body bending the copy it draws.
    public static string? FeedsBack(ISyncRef reference, Component candidate)
    {
        IWorldElement? walk = reference as IWorldElement;
        Component? owner = null;
        for (int depth = 0; walk != null && depth < 8 && owner == null; depth++)
        {
            owner = walk as Component;
            walk = (walk as SyncElement)?.Parent;
        }
        switch (owner)
        {
            case DeformableMesh deformable:
            {
                var mesh = MeshOf(candidate);
                for (int depth = 0; mesh != null && depth < 16; depth++)
                {
                    if (ReferenceEquals(mesh, deformable))
                        return "a mesh cannot be built from itself";
                    mesh = mesh is DeformableMesh next ? MeshOf(next.Mesh.Target) : null;
                }
                return null;
            }
            case SquishyBody body:
                return body.IsOwnOutput(candidate) ? "that is this body's own output" : null;
        }
        return null;
    }

    // What a field takes when handed this component: the component itself where it fits as it is, else the mesh out
    // of a renderer, else nothing.
    public static Component? Accepted(Component? component, bool keepsRenderers)
    {
        if (component == null || component.IsDestroyed || Refusal(component, keepsRenderers) != null)
            return null;
        if (keepsRenderers || IsMesh(component))
            return component;
        return MeshOf(component);
    }

    // The best thing on a slot for a mesh field, in the order a person means it: for a field that keeps renderers a
    // skinned renderer first (it has the pose), then a mesh renderer, then a bare mesh; for any other mesh field the
    // mesh a renderer on it draws, then a bare mesh. Null when the slot carries no mesh.
    public static Component? OnSlot(Slot? slot, bool keepsRenderers)
    {
        if (slot == null || slot.IsDestroyed)
            return null;
        Component? bare = null;
        Component? drawn = null;
        Component? skinned = null;
        foreach (var component in slot.Components)
        {
            if (component == null || component.IsDestroyed)
                continue;
            if (component is SkinnedMeshRenderer s && Refusal(s, keepsRenderers) == null)
                skinned ??= s;
            else if (component is MeshRenderer r && Refusal(r, keepsRenderers) == null)
                drawn ??= r;
            else if (IsMesh(component))
                bare ??= component;
        }
        if (keepsRenderers)
            return skinned ?? drawn ?? bare;
        return MeshOf(drawn) ?? bare ?? MeshOf(skinned);
    }
}

// A component whose mesh field keeps a renderer as such, because it does more with one than read its mesh.
public interface IMeshSourceReader
{
}
