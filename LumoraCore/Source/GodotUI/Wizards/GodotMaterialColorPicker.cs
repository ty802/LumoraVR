using System;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.GodotUI.Wizards;

/// <summary>
/// Standalone in-world color picker panel for editing a single shader color uniform.
/// </summary>
[ComponentCategory("GodotUI/Wizards")]
public sealed class GodotMaterialColorPicker : GodotUIPanel
{
    protected override string DefaultScenePath => LumAssets.UI.MaterialColorPicker;
    protected override float2 DefaultSize => new float2(340, 460);
    protected override float DefaultPixelsPerUnit => 900f;

    /// <summary>
    /// Material containing the target uniform.
    /// </summary>
    public SyncRef<CustomShaderMaterial> Material { get; private set; } = null!;

    /// <summary>
    /// Target shader uniform name to edit.
    /// </summary>
    public Sync<string> ParameterName { get; private set; } = null!;

    public override void OnAwake()
    {
        base.OnAwake();

        Material = new SyncRef<CustomShaderMaterial>(this);
        ParameterName = new Sync<string>(this, string.Empty);

        Material.OnTargetChange += _ => NotifyChanged();
        ParameterName.OnChanged += _ => NotifyChanged();
    }

    public override void OnAttach()
    {
        base.OnAttach();

        // Picker should be movable as an independent world panel.
        if (Slot.GetComponent<Grabbable>() == null)
        {
            Slot.AttachComponent<Grabbable>();
        }
    }

    public ShaderUniformParam? FindParameter()
    {
        var material = Material.Target;
        var parameterName = ParameterName.Value;
        if (material == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        foreach (var param in material.Parameters)
        {
            if (string.Equals(param.Name.Value, parameterName, StringComparison.Ordinal))
            {
                return param;
            }
        }

        return null;
    }
}
