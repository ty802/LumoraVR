// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;

namespace Lumora.Core.Components.Avatar;

// What every stand-in in a world draws with: one material and two shapes, a ball and a capsule.
// Found or built once per world on a local slot, like NameplateTheme, so thirty people arriving at once cost
// one of each; a piece is one of these shapes stretched by its slot's scale. -xlinka
[ComponentCategory("Hidden")]
public sealed class AvatarArrivalKit : Component
{
    public const string KitSlotName = "Avatar Arrival Kit";

    public AvatarArrivalMaterial? Material { get; private set; }
    public SphereMesh? Ball { get; private set; }
    public CapsuleMesh? Capsule { get; private set; }

    public override void OnStart()
    {
        base.OnStart();
        Build();
    }

    private void Build()
    {
        if (Slot == null || Material != null)
            return;

        Material = Slot.AttachComponent<AvatarArrivalMaterial>();

        // Unit sizes: a ball one metre across, a capsule one across and two long on Y.
        // Few segments: these are soft glowing shapes, and the rim term hides the facets.
        Ball = Slot.AddSlot("Ball").AttachComponent<SphereMesh>();
        Ball.Radius.Value = 0.5f;
        Ball.Segments.Value = 20;
        Ball.Rings.Value = 12;

        Capsule = Slot.AddSlot("Capsule").AttachComponent<CapsuleMesh>();
        Capsule.Radius.Value = 0.5f;
        Capsule.Height.Value = 2f;
        Capsule.Segments.Value = 16;
        Capsule.Rings.Value = 6;
    }

    // Find-or-build, on a local slot under the world root. Every peer builds its own and none of it
    // replicates or saves.
    public static AvatarArrivalKit? For(World? world)
    {
        var root = world?.RootSlot;
        if (root == null || root.IsDestroyed)
            return null;

        var slot = root.FindChild(KitSlotName, recursive: false);
        var kit = slot?.GetComponent<AvatarArrivalKit>();
        if (kit != null && !kit.IsDestroyed)
            return kit;

        slot?.Destroy();
        slot = root.AddLocalSlot(KitSlotName);
        slot.Persistent.Value = false;
        kit = slot.AttachComponent<AvatarArrivalKit>();
        kit.Build();
        return kit;
    }
}
