using Godot;
using Aquamarine.Source.Tools;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Interaction;

/// <summary>
/// Controls a VR hand with grab laser, tool slot, and input handling.
/// </summary>
[GlobalClass]
public partial class HandController : Node3D
{
    [Export] public HandSide Hand { get; set; } = HandSide.Right;
    [Export] public bool EnableGrabLaser { get; set; } = true;
    [Export] public bool EnableToolSlot { get; set; } = true;

    // Input action names
    [Export] public string GripAction { get; set; } = "grip";
    [Export] public string TriggerAction { get; set; } = "trigger";

    private GrabLaser _grabLaser;
    private ToolSlot _toolSlot;
    private XRController3D _xrController;
    private bool _isGripping;
    private bool _isTriggeringTool;

    public GrabLaser GrabLaser => _grabLaser;
    public ToolSlot ToolSlot => _toolSlot;

    public override void _Ready()
    {
        // Try to find XRController3D parent
        _xrController = GetParentOrNull<XRController3D>();
        if (_xrController == null)
        {
            AquaLogger.Warn($"HandController {Hand} not under XRController3D!");
        }

        SetupGrabLaser();
        SetupToolSlot();

        AquaLogger.Log($"HandController {Hand} initialized");
    }

    private void SetupGrabLaser()
    {
        if (!EnableGrabLaser)
            return;

        _grabLaser = GetNodeOrNull<GrabLaser>("GrabLaser");
        if (_grabLaser == null)
        {
            _grabLaser = new GrabLaser();
            _grabLaser.Name = "GrabLaser";
            AddChild(_grabLaser);
            AquaLogger.Log($"Created GrabLaser for {Hand} hand");
        }
    }

    private void SetupToolSlot()
    {
        if (!EnableToolSlot)
            return;

        _toolSlot = GetNodeOrNull<ToolSlot>("ToolSlot");
        if (_toolSlot == null)
        {
            _toolSlot = new ToolSlot();
            _toolSlot.Name = "ToolSlot";
            _toolSlot.Hand = Hand;
            AddChild(_toolSlot);
            AquaLogger.Log($"Created ToolSlot for {Hand} hand");
        }
    }

    public override void _Process(double delta)
    {
        HandleGripInput();
        HandleTriggerInput();
    }

    private void HandleGripInput()
    {
        if (_grabLaser == null)
            return;

        string actionName = Hand == HandSide.Left ? $"{GripAction}_left" : $"{GripAction}_right";

        // Check if grip button is pressed
        bool gripPressed = Godot.Input.IsActionPressed(actionName);

        if (gripPressed && !_isGripping)
        {
            // Grip button just pressed - try to grab
            _grabLaser.TriggerGrab();
            _isGripping = true;
        }
        else if (!gripPressed && _isGripping)
        {
            // Grip button released - release grab
            _grabLaser.ReleaseGrab();
            _isGripping = false;
        }
    }

    private void HandleTriggerInput()
    {
        if (_toolSlot == null || !_toolSlot.HasTool)
            return;

        string actionName = Hand == HandSide.Left ? $"{TriggerAction}_left" : $"{TriggerAction}_right";

        // Check if trigger is pressed
        bool triggerPressed = Godot.Input.IsActionPressed(actionName);

        if (triggerPressed && !_isTriggeringTool)
        {
            // Trigger just pressed
            _toolSlot.TriggerPrimaryAction();
            _isTriggeringTool = true;
        }
        else if (!triggerPressed && _isTriggeringTool)
        {
            // Trigger released
            _toolSlot.ReleasePrimaryAction();
            _isTriggeringTool = false;
        }
    }

    /// <summary>
    /// Set whether the grab laser is visible.
    /// </summary>
    public void SetGrabLaserVisible(bool visible)
    {
        if (_grabLaser != null)
        {
            _grabLaser.SetLaserVisible(visible);
        }
    }

    /// <summary>
    /// Equip a tool to this hand.
    /// </summary>
    public void EquipTool(ITool tool)
    {
        _toolSlot?.EquipTool(tool);
    }

    /// <summary>
    /// Unequip the current tool from this hand.
    /// </summary>
    public void UnequipTool()
    {
        _toolSlot?.UnequipTool();
    }

    /// <summary>
    /// Force release any grabbed object.
    /// </summary>
    public void ForceReleaseGrab()
    {
        if (_grabLaser != null && _grabLaser.IsGrabbing)
        {
            _grabLaser.ReleaseGrab();
            _isGripping = false;
        }
    }
}
