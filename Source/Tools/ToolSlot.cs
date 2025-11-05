using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Tools;

/// <summary>
/// Represents a slot where a tool can be equipped (e.g., left or right hand).
/// </summary>
public enum HandSide
{
    Left,
    Right
}

public partial class ToolSlot : Node3D
{
    [Export] public HandSide Hand { get; set; }

    private ITool _currentTool;
    private Node3D _toolAttachPoint;

    public ITool CurrentTool => _currentTool;
    public bool HasTool => _currentTool != null;

    public override void _Ready()
    {
        // Create attach point for tool mesh
        _toolAttachPoint = new Node3D();
        _toolAttachPoint.Name = "ToolAttachPoint";
        AddChild(_toolAttachPoint);
    }

    /// <summary>
    /// Equip a tool to this slot.
    /// </summary>
    public void EquipTool(ITool tool)
    {
        if (_currentTool != null)
        {
            UnequipTool();
        }

        _currentTool = tool;

        if (tool != null)
        {
            // Attach tool mesh to slot
            if (tool.ToolMesh != null && !_toolAttachPoint.IsAncestorOf(tool.ToolMesh))
            {
                _toolAttachPoint.AddChild(tool.ToolMesh);
                tool.ToolMesh.Position = Vector3.Zero;
                tool.ToolMesh.Rotation = Vector3.Zero;
            }

            tool.OnEquipped(this);
            AquaLogger.Log($"Equipped {tool.ToolName} to {Hand} hand");
        }
    }

    /// <summary>
    /// Unequip the current tool.
    /// </summary>
    public void UnequipTool()
    {
        if (_currentTool != null)
        {
            _currentTool.OnUnequipped();

            // Remove tool mesh
            if (_currentTool.ToolMesh != null && _toolAttachPoint.IsAncestorOf(_currentTool.ToolMesh))
            {
                _toolAttachPoint.RemoveChild(_currentTool.ToolMesh);
            }

            AquaLogger.Log($"Unequipped {_currentTool.ToolName} from {Hand} hand");
            _currentTool = null;
        }
    }

    public override void _Process(double delta)
    {
        if (_currentTool != null)
        {
            _currentTool.OnUpdate(delta);
        }
    }

    /// <summary>
    /// Trigger primary action on the current tool.
    /// </summary>
    public void TriggerPrimaryAction()
    {
        _currentTool?.OnPrimaryAction();
    }

    /// <summary>
    /// Release primary action on the current tool.
    /// </summary>
    public void ReleasePrimaryAction()
    {
        _currentTool?.OnPrimaryActionRelease();
    }

    /// <summary>
    /// Trigger secondary action on the current tool.
    /// </summary>
    public void TriggerSecondaryAction()
    {
        _currentTool?.OnSecondaryAction();
    }

    /// <summary>
    /// Release secondary action on the current tool.
    /// </summary>
    public void ReleaseSecondaryAction()
    {
        _currentTool?.OnSecondaryActionRelease();
    }
}
