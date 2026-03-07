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
    public SyncRef<CustomShaderMaterial> Material { get; private set; } = null!;

    public override void OnAwake()
    {
        base.OnAwake();

        Material = new SyncRef<CustomShaderMaterial>(this);
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
