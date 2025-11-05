using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;
using System.Collections.Generic;

namespace Aquamarine.Source.Tools;

/// <summary>
/// Manages developer tools that can be spawned and equipped.
/// Press number keys to spawn tools in hand.
/// </summary>
[GlobalClass]
public partial class DevToolManager : Node
{
    public static DevToolManager Instance { get; private set; }

    [Export] public NodePath LeftHandPath { get; set; }
    [Export] public NodePath RightHandPath { get; set; }

    private ToolSlot _leftHandSlot;
    private ToolSlot _rightHandSlot;
    private Dictionary<int, PackedScene> _toolRegistry = new();
    private Dictionary<int, string> _toolNames = new();

    public override void _Ready()
    {
        if (Instance != null)
        {
            AquaLogger.Warn("DevToolManager instance already exists!");
            QueueFree();
            return;
        }

        Instance = this;

        // Find or create hand slots
        SetupHandSlots();

        // Register default tools
        RegisterDefaultTools();

        AquaLogger.Log("DevToolManager initialized");
    }

    private void SetupHandSlots()
    {
        // Try to find existing hand slots
        if (LeftHandPath != null && !LeftHandPath.IsEmpty)
        {
            _leftHandSlot = GetNode<ToolSlot>(LeftHandPath);
        }

        if (RightHandPath != null && !RightHandPath.IsEmpty)
        {
            _rightHandSlot = GetNode<ToolSlot>(RightHandPath);
        }

        // If not found, try to find HandControllers by unique name
        if (_leftHandSlot == null)
        {
            var leftHand = GetNodeOrNull<Node>("%LeftHand");
            if (leftHand != null)
            {
                var handController = leftHand.GetNodeOrNull<Node>("HandController");
                if (handController != null)
                {
                    _leftHandSlot = handController.GetNodeOrNull<ToolSlot>("ToolSlot");
                }
            }
        }

        if (_rightHandSlot == null)
        {
            var rightHand = GetNodeOrNull<Node>("%RightHand");
            if (rightHand != null)
            {
                var handController = rightHand.GetNodeOrNull<Node>("HandController");
                if (handController != null)
                {
                    _rightHandSlot = handController.GetNodeOrNull<ToolSlot>("ToolSlot");
                }
            }
        }

        // Still not found? Log warning
        if (_leftHandSlot == null)
        {
            AquaLogger.Warn("Left hand ToolSlot not found - tools will not work on left hand");
        }

        if (_rightHandSlot == null)
        {
            AquaLogger.Warn("Right hand ToolSlot not found - tools will not work on right hand");
        }
    }

    private ToolSlot CreateHandSlot(Aquamarine.Source.Tools.HandSide hand)
    {
        var slot = new ToolSlot();
        slot.Name = $"{hand}HandToolSlot";
        slot.Hand = hand;
        AddChild(slot);
        return slot;
    }

    private void RegisterDefaultTools()
    {
        // Key 1: Inspector Tool
        RegisterTool(1, "res://Tools/InspectorTool.tscn", "Inspector");

        // Key 2: Creator Tool (for spawning objects)
        RegisterTool(2, "res://Tools/CreatorTool.tscn", "Creator");

        // Key 3: Material Tool
        RegisterTool(3, "res://Tools/MaterialTool.tscn", "Material");

        // Key 4: Transform Tool
        RegisterTool(4, "res://Tools/TransformTool.tscn", "Transform");

        AquaLogger.Log($"Registered {_toolRegistry.Count} developer tools");
    }

    /// <summary>
    /// Register a tool with a key binding.
    /// </summary>
    public void RegisterTool(int key, string scenePath, string toolName)
    {
        // For now, we'll create tools programmatically
        // Later these can be loaded from scenes
        _toolRegistry[key] = null; // Placeholder
        _toolNames[key] = toolName;
        AquaLogger.Log($"Registered tool '{toolName}' to key {key}");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            // Check for number keys 1-9
            if (keyEvent.Keycode >= Key.Key1 && keyEvent.Keycode <= Key.Key9)
            {
                int toolKey = (int)(keyEvent.Keycode - Key.Key1 + 1);
                SpawnTool(toolKey);
            }
            // Key 0 to unequip all tools
            else if (keyEvent.Keycode == Key.Key0)
            {
                UnequipAllTools();
            }
        }
    }

    /// <summary>
    /// Spawn a tool by its key number.
    /// </summary>
    public void SpawnTool(int key)
    {
        if (!_toolNames.ContainsKey(key))
        {
            AquaLogger.Warn($"No tool registered for key {key}");
            return;
        }

        string toolName = _toolNames[key];

        // Create the appropriate tool
        ITool tool = null;

        switch (key)
        {
            case 1: // Inspector
                tool = CreateInspectorTool();
                break;
            case 2: // Creator
                AquaLogger.Log("Creator tool not yet implemented");
                return;
            case 3: // Material
                AquaLogger.Log("Material tool not yet implemented");
                return;
            case 4: // Transform
                AquaLogger.Log("Transform tool not yet implemented");
                return;
            default:
                AquaLogger.Warn($"Tool {key} ({toolName}) not implemented");
                return;
        }

        if (tool != null)
        {
            // Equip to right hand by default
            _rightHandSlot?.EquipTool(tool);
            AquaLogger.Log($"Spawned {toolName} tool in right hand");
        }
    }

    private ITool CreateInspectorTool()
    {
        var inspector = new InspectorTool();
        AddChild(inspector);
        return inspector;
    }

    /// <summary>
    /// Unequip all tools from both hands.
    /// </summary>
    public void UnequipAllTools()
    {
        _leftHandSlot?.UnequipTool();
        _rightHandSlot?.UnequipTool();
        AquaLogger.Log("Unequipped all tools");
    }

    /// <summary>
    /// Get the left hand tool slot.
    /// </summary>
    public ToolSlot GetLeftHandSlot() => _leftHandSlot;

    /// <summary>
    /// Get the right hand tool slot.
    /// </summary>
    public ToolSlot GetRightHandSlot() => _rightHandSlot;

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
