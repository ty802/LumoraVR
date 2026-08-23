// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Source.Godot.Extensions;
using Lumora.Godot.Hooks;

namespace Lumora.Godot.Helpers;

public static class GodotHelper
{
    public static Node3D GetGeneratedNode3D(this Slot slot, bool forceGenerate = false)
    {
        if (slot == null)
            return null!;

        var slotHook = slot.Hook as SlotHook;
        if (slotHook == null)
            return null!;

        if (forceGenerate)
            return slotHook.ForceGetNode3D();

        return slotHook.GeneratedNode3D;
    }

    public static void ConvertSlots(System.Collections.Generic.List<Slot> slots, System.Collections.Generic.List<Node3D> nodes)
    {
        if (slots == null || nodes == null)
            return;

        foreach (var slot in slots)
        {
            var node = slot.GetGeneratedNode3D();
            if (node != null)
                nodes.Add(node);
        }
    }

    // TODO: Implement when texture asset system is ready.
    // public static Texture2D GetGodot(this ITexture2D texture)
    // {
    //     if (texture == null)
    //         return null;
    //
    //     var hook = texture.Hook as TextureHook;
    //     return hook?.GodotTexture;
    // }

    // TODO: Implement when mesh asset system is ready.
    // public static Mesh GetGodot(this IMesh mesh)
    // {
    //     if (mesh == null)
    //         return null;
    //
    //     var hook = mesh.Hook as MeshHook;
    //     return hook?.GodotMesh;
    // }
}

