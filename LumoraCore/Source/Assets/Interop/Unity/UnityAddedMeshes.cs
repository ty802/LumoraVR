// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Lumora.Core.Components;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Assets.Interop.Unity;

// Meshes on objects the project made rather than in a model file: the editor's built-in shapes (a counter's
// plane, a credits card, a blinder sphere) and mesh assets saved as text. Cube, sphere, capsule and cylinder
// become our own shapes at the same size. Plane and quad are drawn with the editor's own layout, since a
// picture on them has to read the right way round: the plane is ten units square facing up with its
// texture's origin at the +X +Z corner, the quad one unit facing -Z. A mesh asset is read from its vertex
// streams. Both go into the local database as baked meshes.
//
// Everything here is in the project's left-handed space until it is built: X flips and V counts down from the
// top. The index order stands: a front face turns clockwise on screen in the project and in our meshes alike,
// and the flip changes the handedness along with the points, so each triangle keeps its turn. Sizes are the
// project's units, scaled into the local units of the object they land on. -xlinka
internal static class UnityAddedMeshes
{
    private const string BuiltinMeshes = "0000000000000000e000000000000000";

    private enum Builtin { None, Cube = 10202, Cylinder = 10206, Sphere = 10207, Capsule = 10208, Plane = 10209, Quad = 10210 }

    private static Builtin BuiltinOf(UnityMeshRef mesh)
        => string.Equals(mesh.MeshGuid, BuiltinMeshes, StringComparison.OrdinalIgnoreCase) && Enum.IsDefined(typeof(Builtin), (int)mesh.MeshFileId)
            ? (Builtin)(int)mesh.MeshFileId
            : Builtin.None;

    // Off the world thread: the baked meshes every plane, quad and mesh asset needs, by mesh reference.
    public static async Task<Dictionary<UnityMeshRef, Uri>> BakeAsync(UnityPackageArchive archive, UnityImportCandidate candidate,
        LocalDB? localDB, List<string> notes)
    {
        var baked = new Dictionary<UnityMeshRef, Uri>();
        if (localDB == null || candidate.Meshes.Count == 0)
            return baked;
        var byKey = new Dictionary<string, Uri?>(StringComparer.OrdinalIgnoreCase);
        var unread = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mesh in candidate.Meshes)
        {
            var builtin = BuiltinOf(mesh);
            if (builtin is Builtin.Cube or Builtin.Cylinder or Builtin.Sphere or Builtin.Capsule)
                continue;
            string key = builtin != Builtin.None ? builtin.ToString() : mesh.MeshGuid + ":" + mesh.MeshFileId;
            if (!byKey.TryGetValue(key, out var uri))
            {
                PhosMesh? phos = null;
                if (builtin == Builtin.Plane)
                    phos = Plane();
                else if (builtin == Builtin.Quad)
                    phos = Quad();
                else if (archive.Find(mesh.MeshGuid) is { File: not null } asset)
                {
                    try
                    {
                        phos = UnityMeshAsset.Read(archive.ReadText(asset), mesh.MeshFileId);
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"{asset.Path}: the mesh could not be read ({ex.Message}).");
                    }
                    if (phos == null)
                        unread.Add(asset.Path);
                }
                uri = null;
                if (phos != null)
                {
                    var local = await localDB.SaveMeshAsync(phos).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(local))
                        uri = new Uri(local);
                }
                byKey[key] = uri;
            }
            if (uri != null)
                baked[mesh] = uri;
        }
        if (unread.Count > 0)
            notes.Add("Meshes on added objects that could not be read: " + string.Join(", ", unread) + ".");
        return baked;
    }

    // World thread: each mesh on the slot its object landed on, with its renderer's materials.
    public static int Build(UnityImportCandidate candidate, UnityAvatarBuilder.Assembly assembly, Dictionary<UnityMeshRef, Uri> baked,
        Func<string, MaterialProvider?> materialFor)
    {
        int built = 0;
        foreach (var mesh in candidate.Meshes)
        {
            if (assembly.Nodes.Resolve(mesh.Node) is not { } slot)
                continue;
            // One of the project's units in the slot's own.
            float size = 1f / MathF.Max(1e-6f, assembly.Nodes.UnitOf(slot));
            Component? source = BuiltinOf(mesh) switch
            {
                Builtin.Cube => Box(slot, size),
                Builtin.Sphere => Sphere(slot, size),
                Builtin.Capsule => Capsule(slot, size),
                Builtin.Cylinder => Cylinder(slot, size),
                _ => baked.TryGetValue(mesh, out var uri) ? Baked(slot, uri, size) : null,
            };
            if (source == null)
                continue;
            var renderer = slot.AttachComponent<MeshRenderer>();
            renderer.Mesh.Target = source;
            var materials = new List<MaterialProvider>();
            foreach (var guid in mesh.Materials)
                if (guid != null && materialFor(guid) is { } material)
                    materials.Add(material);
            // A one-surface mesh draws extra materials as more passes over that surface; the first is the look.
            if (materials.Count > 0)
                renderer.Material.Target = materials[0];
            renderer.Enabled.Value = mesh.Enabled;
            built++;
        }
        return built;
    }

    private static Component Box(Slot slot, float size)
    {
        var box = slot.AttachComponent<BoxMesh>();
        box.Size.Value = new float3(size, size, size);
        return box;
    }

    private static Component Sphere(Slot slot, float size)
    {
        var sphere = slot.AttachComponent<SphereMesh>();
        sphere.Radius.Value = 0.5f * size;
        return sphere;
    }

    private static Component Capsule(Slot slot, float size)
    {
        var capsule = slot.AttachComponent<CapsuleMesh>();
        capsule.Radius.Value = 0.5f * size;
        capsule.Height.Value = 2f * size;
        return capsule;
    }

    private static Component Cylinder(Slot slot, float size)
    {
        var cylinder = slot.AttachComponent<CylinderMesh>();
        cylinder.Radius.Value = 0.5f * size;
        cylinder.Height.Value = 2f * size;
        return cylinder;
    }

    private static Component Baked(Slot slot, Uri uri, float size)
    {
        var provider = slot.AttachComponent<MeshProvider>();
        provider.URL.Value = uri;
        if (MathF.Abs(size - 1f) > 1e-5f)
            provider.ImportScale.Value = size;
        return provider;
    }

    // Ten units square in eleven rows of eleven, facing up; the texture's origin is at the +X +Z corner in
    // the project's space, which is -X +Z here.
    private static PhosMesh Plane()
    {
        var mesh = NewMesh(121);
        for (int j = 0; j <= 10; j++)
        {
            for (int i = 0; i <= 10; i++)
            {
                int at = j * 11 + i;
                mesh.RawPositions[at] = new float3(-5f + i, 0f, -5f + j);
                mesh.RawNormals[at] = float3.Up;
                mesh.RawTangents[at] = new float4(1f, 0f, 0f, 1f);
                mesh.SetUV(0, at, new float2(i / 10f, j / 10f));
            }
        }
        var submesh = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(submesh);
        for (int j = 0; j < 10; j++)
        {
            for (int i = 0; i < 10; i++)
            {
                int a = j * 11 + i, b = a + 1, c = a + 11, d = c + 1;
                submesh.AddTriangle(a, b, c);
                submesh.AddTriangle(b, d, c);
            }
        }
        return mesh;
    }

    // One unit square facing -Z; the texture reads upright and unmirrored from in front.
    private static PhosMesh Quad()
    {
        var mesh = NewMesh(4);
        var corners = new[]
        {
            (new float3(0.5f, -0.5f, 0f), new float2(0f, 1f)),
            (new float3(-0.5f, -0.5f, 0f), new float2(1f, 1f)),
            (new float3(0.5f, 0.5f, 0f), new float2(0f, 0f)),
            (new float3(-0.5f, 0.5f, 0f), new float2(1f, 0f)),
        };
        for (int i = 0; i < 4; i++)
        {
            mesh.RawPositions[i] = corners[i].Item1;
            mesh.RawNormals[i] = new float3(0f, 0f, -1f);
            mesh.RawTangents[i] = new float4(-1f, 0f, 0f, 1f);
            mesh.SetUV(0, i, corners[i].Item2);
        }
        var submesh = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(submesh);
        submesh.AddTriangle(0, 3, 1);
        submesh.AddTriangle(3, 0, 2);
        return mesh;
    }

    internal static PhosMesh NewMesh(int vertices)
    {
        var mesh = new PhosMesh { HasNormals = true, HasTangents = true };
        mesh.HasUV0s = true;
        mesh.IncreaseVertexCount(vertices);
        return mesh;
    }
}
