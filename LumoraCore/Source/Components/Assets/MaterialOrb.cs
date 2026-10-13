// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Magnets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Physics;

namespace Lumora.Core.Components;

// A material you can hold: a sphere drawn in the material it carries. It snaps into a material tool's
// cartridge, which then paints with it, and no material tool paints over one.
//
// What it carries is whatever its own sphere is drawn with, read from its renderer, so there is no
// second copy of the material to fall out of step with what everyone can see. -xlinka
[ComponentCategory("Assets/Materials")]
public class MaterialOrb : Component
{
    public const float Radius = 0.05f;

    // The tag a material tool's cartridge socket takes.
    public const string MagnetTag = "MaterialOrb";

    public IAssetProvider<MaterialAsset>? Material
    {
        get
        {
            var renderer = Slot?.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.IsDestroyed || renderer.Materials.Count == 0)
                return null;
            return renderer.Materials.GetElement(0).Target;
        }
    }

    // The material a held object hands to a material field: an orb's, or one sitting on the object itself,
    // which is how a shader graph orb carries its own.
    public static IAssetProvider<MaterialAsset>? CarriedBy(Slot? slot)
    {
        if (slot == null || slot.IsDestroyed)
            return null;
        if (slot.GetComponent<MaterialOrb>()?.Material is { IsDestroyed: false } carried)
            return carried;
        foreach (var component in slot.Components)
        {
            if (component is IAssetProvider<MaterialAsset> { IsDestroyed: false } material)
                return material;
        }
        return null;
    }

    public static MaterialOrb Build(Slot slot, IAssetProvider<MaterialAsset> material)
    {
        var mesh = slot.AttachComponent<SphereMesh>();
        mesh.Radius.Value = Radius;
        mesh.Segments.Value = 32;
        mesh.Rings.Value = 16;

        var collider = slot.AttachComponent<SphereCollider>();
        collider.Radius.Value = Radius;
        collider.Type.Value = ColliderType.Trigger;

        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;

        slot.AttachComponent<Grabbable>();
        var magnet = slot.AttachComponent<Magnet>();
        magnet.Tags.Add(MagnetTag);
        return slot.AttachComponent<MaterialOrb>();
    }
}
