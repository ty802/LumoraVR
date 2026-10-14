// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;

namespace Lumora.Core.Components;

// Puts a material into renderer material slots by switch: the first of Switches that is on puts in its own
// entry of SwitchMaterials (OnMaterial when it has none), and with all of them off the slots get
// OffMaterial. With no switches the On field picks between OnMaterial and OffMaterial.
//
// One driver per slot with a material per switch, because a slot takes one driver and a selector's pages
// each put a different material into the same slot. A slot is driven, so the material it held before stays
// saved underneath and comes back the moment the driver goes. -xlinka
[ComponentCategory("Rendering")]
[DefaultUpdateOrder(-100)]
public class MaterialSwitchDriver : Component
{
    public readonly Sync<bool> On;

    public readonly SyncRefList<IField<bool>> Switches;

    public readonly SyncAssetList<MaterialAsset> SwitchMaterials;

    public readonly AssetRef<MaterialAsset> OffMaterial;

    public readonly AssetRef<MaterialAsset> OnMaterial;

    public readonly SyncList<DriveRef<IAssetProvider<MaterialAsset>>> Slots;

    public MaterialSwitchDriver()
    {
        On = new Sync<bool>(this, false);
        Switches = new SyncRefList<IField<bool>>(this);
        SwitchMaterials = new SyncAssetList<MaterialAsset>(this);
        OffMaterial = new AssetRef<MaterialAsset>(this);
        OnMaterial = new AssetRef<MaterialAsset>(this);
        Slots = new SyncList<DriveRef<IAssetProvider<MaterialAsset>>>();
    }

    public DriveRef<IAssetProvider<MaterialAsset>> AddSlot(SyncRef<IAssetProvider<MaterialAsset>>? materialSlot)
    {
        var drive = Slots.Add();
        drive.DriveTarget(materialSlot);
        return drive;
    }

    public void AddSwitch(IField<bool> on, IAssetProvider<MaterialAsset>? material)
    {
        Switches.Add(on);
        SwitchMaterials.Add(material!);
    }

    public IAssetProvider<MaterialAsset>? Current
    {
        get
        {
            if (Switches.Count == 0)
                return On.Value ? OnMaterial.Target : OffMaterial.Target;
            for (int i = 0; i < Switches.Count; i++)
            {
                if (Switches[i] is { } field && field.Value)
                    return i < SwitchMaterials.Count && SwitchMaterials[i] != null ? SwitchMaterials[i] : OnMaterial.Target;
            }
            return OffMaterial.Target;
        }
    }

    public override void OnUpdate(float delta)
    {
        int count = Slots.Count;
        if (count == 0)
            return;

        var material = Current;
        for (int i = 0; i < count; i++)
        {
            var drive = Slots[i];
            if (drive == null)
                continue;
            drive.LocalValueOnly = true;
            drive.SetValue(material);
        }
    }
}
