// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.GodotUI.Wizards;

/// <summary>
/// Godot UI-based material inspector that loads the MaterialOrbInspector.tscn scene.
/// </summary>
[ComponentCategory("GodotUI/Wizards")]
public sealed class GodotMaterialInspector : GodotUIPanel
{
    protected override string DefaultScenePath => LumAssets.UI.MaterialOrbInspector;
    protected override float2 DefaultSize => new float2(420, 560);
    protected override float DefaultPixelsPerUnit => 900f;

    /// <summary>
    /// Material to inspect.
    /// </summary>
    public readonly SyncRef<CustomShaderMaterial> Material;

    public override void OnAwake()
    {
        base.OnAwake();

        Material.OnTargetChange += _ => NotifyChanged();
    }

    public override void OnAttach()
    {
        base.OnAttach();

        // Inspectors should always be movable in-world.
        if (Slot.GetComponent<Grabbable>() == null)
        {
            Slot.AttachComponent<Grabbable>();
        }
    }
}
