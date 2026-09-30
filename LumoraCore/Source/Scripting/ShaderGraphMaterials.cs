// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Scripting;

// Material orbs whose shader is a shader graph. A new one is a sphere wearing a CustomShaderMaterial whose
// shader is the graph above it, holding only a Material Output, open to build on. It is all one object on
// the orb's own slot, so it is carried, copied, saved and sampled by the material tool like any material,
// and its graph goes with it. Closing the graph parks it; the material keeps wearing what it made. -xlinka
public static class ShaderGraphMaterials
{
    public const string GraphSlotName = "Shader Graph";
    public const string OrbName = "Shader Graph Material";
    public const float OrbRadius = 0.12f;

    // Built on a slot a Create New entry or a script made for it. The whole thing is one undo step for
    // whoever made the slot.
    public static ShaderGraphSource BuildOrb(Slot slot, bool open = true)
    {
        slot.Name.Value = OrbName;
        slot.AttachComponent<Grabbable>();

        var mesh = slot.AttachComponent<SphereMesh>();
        mesh.Radius.Value = OrbRadius;
        mesh.Segments.Value = 32;
        mesh.Rings.Value = 16;

        var collider = slot.AttachComponent<SphereCollider>();
        collider.Radius.Value = OrbRadius;
        collider.Type.Value = ColliderType.Trigger;

        var source = slot.AttachComponent<ShaderGraphSource>();
        var material = slot.AttachComponent<CustomShaderMaterial>();
        material.Shader.Target = source;
        source.Material.Target = material;

        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;

        var space = slot.AddSlot(GraphSlotName).AttachComponent<ScriptGraphSpace>();
        space.Kind.Value = GraphKind.Shader;
        space.ShaderSource.Target = source;
        source.Graph.Target = space;
        Place(space, slot.World?.LocalUser?.Root?.HeadPosition);

        // The output a little right of the graph's middle, so new nodes have room on its left.
        var output = ScriptGraphEditing.MakeNode(space, "MaterialOutput", null, new float3(0.02f, 0f, 0f), out var error);
        if (output == null)
            Logging.Logger.Warn($"Shader graph: a new material's output was not made: {error}");

        space.Slot.ActiveSelf.Value = open;
        return source;
    }

    // Opens a material's graph above it, turned to whoever opens it, or parks it.
    public static bool SetOpen(ShaderGraphSource source, bool open)
    {
        if (source?.Graph.Target is not { IsDestroyed: false } space)
            return false;
        try
        {
            if (open)
                Place(space, source.World?.LocalUser?.Root?.HeadPosition);
            if (space.Slot.ActiveSelf.Value != open)
                space.Slot.ActiveSelf.Value = open;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        return space.Slot.ActiveSelf.Value == open;
    }

    // The material a graph makes, found from anything on or under its object.
    public static ShaderGraphSource? FindOn(Slot? slot)
    {
        for (var at = slot; at != null && !at.IsRootSlot; at = at.Parent)
        {
            if (at.GetComponent<ShaderGraphSource>() is { IsDestroyed: false } source)
                return source;
        }
        return null;
    }

    // Above the orb, a metre across at any orb scale, turned to face the given point.
    private static void Place(ScriptGraphSpace space, float3? facing)
    {
        var orb = space.Slot.Parent;
        if (orb == null)
            return;
        var at = orb.GlobalPosition + new float3(0f, OrbRadius + 0.3f, 0f);
        space.Slot.GlobalPosition = at;
        if (facing is float3 point)
        {
            var toward = point - at;
            toward.y = 0f;
            if (toward.LengthSquared > 1e-6f)
                space.Slot.GlobalRotation = floatQ.LookRotation(toward.Normalized, float3.Up).Inverse;
        }
        space.Slot.GlobalScale = float3.One;
    }
}
