// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// What a held object hands to an asset field (a clip, a mesh, a texture, a material) when it is let go over the
// field, or when the field is pressed with it in either hand. It is what the object carries itself: a provider of
// the field's kind sitting on the object, the way an imported sound carries its clip, or for a mesh field the
// shape its own renderer draws. Only what is on the object counts, not what is under it: a panel keeps its
// theme's sprite just under it, and letting go of a panel is not handing over that sprite. A mesh field makes one
// exception: an object with no mesh of its own and exactly one renderer inside it, an imported model held by its
// root, hands over that one. A panel has many and hands over none.
//
// A material field goes by the material orb's own rule (MaterialOrb.CarriedBy), and an orb hands its material to
// material fields and nothing else: its sphere is how it looks, not a mesh it is offering. A reference card is
// not read here; it means what it points at. -xlinka
public static class HeldAsset
{
    // A field that holds an asset provider of some kind.
    public static bool IsAssetField(ISyncRef? reference)
        => reference != null && typeof(IAssetProvider).IsAssignableFrom(reference.TargetType);

    public static bool IsMaterialField(ISyncRef? reference)
        => reference != null && typeof(IAssetProvider<MaterialAsset>).IsAssignableFrom(reference.TargetType);

    // A mesh field takes any component, so it is known by its name (see MeshSource.IsMeshField). A held object hands
    // it the mesh its own renderer draws, the way an orb hands over the material it is drawn in; a field that keeps
    // renderers (a soft body's) gets the renderer itself, a skinned one first.
    public static bool IsMeshField(ISyncRef? reference) => MeshSource.IsMeshField(reference);

    // The asset this held object offers to the field, or null when it carries none of that kind.
    public static IWorldElement? CarriedBy(Slot? slot, ISyncRef reference)
    {
        if (slot == null || slot.IsDestroyed)
            return null;
        if (IsMaterialField(reference))
            return MaterialOrb.CarriedBy(slot);
        if (slot.GetComponent<MaterialOrb>() != null)
            return null;
        if (IsMeshField(reference))
        {
            bool keeps = MeshSource.KeepsRenderers(reference);
            return MeshSource.OnSlot(slot, keeps) ?? OnlyMeshInside(slot, keeps);
        }
        return IsAssetField(reference) ? OnSlot(slot, reference) : null;
    }

    // The kind of asset a field takes, in words: "material", "texture", "sound".
    public static string KindName(ISyncRef reference)
    {
        var type = reference.TargetType;
        var asset = type.IsGenericType && type.GetGenericArguments() is { Length: 1 } args ? args[0] : null;
        return asset?.Name switch
        {
            null => "asset",
            "AudioClip" => "sound",
            "Texture2D" or "TextureAsset" => "texture",
            var name when name.EndsWith("Asset") => name[..^5].ToLowerInvariant(),
            var name => name.ToLowerInvariant(),
        };
    }

    // Why a held object gives a mesh field nothing, for the person holding it.
    public static string MeshRefusal(Slot? slot, ISyncRef reference)
    {
        if (slot == null || slot.IsDestroyed)
            return "nothing is held";
        if (slot.GetComponent<MaterialOrb>() != null)
            return "an orb carries a material, not a mesh";
        int renderers = 0;
        foreach (var child in slot.Children)
        {
            foreach (var _ in child.GetComponentsInChildren<MeshRenderer>())
                renderers++;
            foreach (var _ in child.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderers++;
        }
        if (renderers > 1)
            return $"'{slot.SlotName.Value}' holds {renderers} meshes; hold one part";
        return $"'{slot.SlotName.Value}' carries no mesh";
    }

    // The one renderer under an object, as a mesh field takes it; null when there are none or several.
    private static IWorldElement? OnlyMeshInside(Slot slot, bool keepsRenderers)
    {
        Component? found = null;
        foreach (var child in slot.Children)
        {
            foreach (var renderer in child.GetComponentsInChildren<MeshRenderer>())
            {
                if (found != null)
                    return null;
                found = renderer;
            }
            foreach (var skinned in child.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (found != null)
                    return null;
                found = skinned;
            }
        }
        return MeshSource.Accepted(found, keepsRenderers);
    }

    // A provider on the slot the field can take, first in component order.
    internal static IWorldElement? OnSlot(Slot slot, ISyncRef reference)
    {
        var type = reference.TargetType;
        foreach (var component in slot.Components)
        {
            if (component is IAssetProvider && !component.IsDestroyed && type.IsInstanceOfType(component))
                return component;
        }
        return null;
    }
}
