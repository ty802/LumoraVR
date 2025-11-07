using System;
using System.Linq;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Input.Drivers;
using Aquamarine.Source.Core;
using Aquamarine.Source.Scene.RootObjects;
using Bones.Core;
using Godot;

namespace Aquamarine.Source.Input;

/// <summary>
/// Desktop input provider using MouseDriver and KeyboardDriver.
/// </summary>
public partial class DesktopInput : Node3D, IInputProvider
{
    private Vector2 _headRotation;
    private float _currentHeadHeight = 1.8f;
    private bool _movementLocked;
    private Camera3D _camera; // Camera lives here rather than on the player character.
    
    // Drivers (created by Engine, injected here)
    public MouseDriver MouseDriver { get; set; }
    public KeyboardDriver KeyboardDriver { get; set; }

    public override void _Ready()
    {
        try
        {
            base._Ready();

            // Set the global singleton - this is critical!
            IInputProvider.Instance = this;

            ProcessPriority = -9;
            
            // Capture mouse
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
            
            // Create camera anchored to this input node
            _camera = new Camera3D();
            _camera.Name = "DesktopCamera";
            _camera.Current = true;
            _camera.Near = 0.05f; // 5cm near clip (prevents seeing nearby geometry)
            AddChild(_camera);

            Logging.Logger.Log("DesktopInput initialized with camera.");
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Error during DesktopInput initialization: {ex.Message}");
        }
    }
    
    public override void _Process(double delta)
    {
        base._Process(delta);
        
        if (MouseDriver == null || KeyboardDriver == null)
            return;
            
        var deltaf = (float)delta;
        
        // Update head rotation from mouse + arrow keys
        var mouseDelta = MouseDriver.NormalizedMouseDelta;
        var keyboardCamera = KeyboardDriver.CameraInput * Mathf.Pi;
        var camMovement = (mouseDelta + keyboardCamera) * deltaf;
        
        var horizontal = Mathf.Wrap(_headRotation.X + camMovement.X, 0, Mathf.Tau);
        var vertical = Mathf.Clamp(_headRotation.Y + camMovement.Y, -Mathf.Pi / 2, Mathf.Pi / 2);

        _headRotation = new Vector2(horizontal, vertical);

        // Update head height for crouching
        var isCrouching = !_movementLocked && KeyboardDriver.CrouchHeld;
        _currentHeadHeight = _currentHeadHeight.Damp((isCrouching ? 0.45f : 1) * GetHeight, 10, deltaf);
        
        // Update camera position/rotation
        if (_camera != null)
        {
            _camera.Quaternion = Quaternion.FromEuler(new Vector3(_headRotation.Y, _headRotation.X, 0));
            // Position at head height + offset 10cm forward in camera's local space
            _camera.Position = (Vector3.Up * _currentHeadHeight) + (_camera.Transform.Basis * new Vector3(0, 0, -0.22f));// this is a fucking hack this is disgusting and needs to be fixed 
        }
        
        // Follow the local player so the camera tracks their position
        UpdatePositioning();
    }
    
    private void UpdatePositioning()
    {
        // Find the local player so the camera can follow them
        var localPlayer = GetTree().GetFirstNodeInGroup("players") as PlayerCharacterController;
        
        if (localPlayer != null && IsInstanceValid(localPlayer))
        {
            // Move DesktopInput to player's position (camera follows automatically)
            GlobalPosition = localPlayer.GlobalPosition;
        }
    }

    public static readonly PackedScene PackedScene = ResourceLoader.Load<PackedScene>("res://Scenes/DesktopInput.tscn");
    public bool IsVR => false;
    public Vector3 GetPlayspaceMovementDelta => Vector3.Zero;
    public Vector2 GetMovementInputAxis => KeyboardDriver?.MovementInput ?? Vector2.Zero;
    public bool GetJumpInput => !_movementLocked && (KeyboardDriver?.JumpPressed ?? false);
    public bool GetSprintInput => KeyboardDriver?.SprintHeld ?? false;
    public float GetHeight => 1.8f;
    
    public Vector3 GetLimbPosition(IInputProvider.InputLimb limb)
    {
        var headRotation = Quaternion.FromEuler(new Vector3(_headRotation.Y, _headRotation.X, 0));
        
        return limb switch
        {
            IInputProvider.InputLimb.Head => Vector3.Up * _currentHeadHeight,
            IInputProvider.InputLimb.LeftHand => (Vector3.Up * _currentHeadHeight / 2) + (headRotation * new Vector3(-0.25f, 0, 0)),
            IInputProvider.InputLimb.RightHand => (Vector3.Up * _currentHeadHeight / 2) + (headRotation * new Vector3(0.25f, 0, 0)),
            IInputProvider.InputLimb.Hip => (Vector3.Up * _currentHeadHeight) / 2,
            IInputProvider.InputLimb.LeftFoot => headRotation * new Vector3(-0.125f, 0, 0),
            IInputProvider.InputLimb.RightFoot => headRotation * new Vector3(0.125f, 0, 0),
            _ => Vector3.Zero
        };
    }
    
    public Quaternion GetLimbRotation(IInputProvider.InputLimb limb) => limb switch
    {
        IInputProvider.InputLimb.Head => Quaternion.FromEuler(new Vector3(_headRotation.Y, _headRotation.X, 0)),
        IInputProvider.InputLimb.LeftHand => Quaternion.FromEuler(new Vector3(-(Mathf.Pi / 2), _headRotation.X, 0)),
        IInputProvider.InputLimb.RightHand => Quaternion.FromEuler(new Vector3(-(Mathf.Pi / 2), _headRotation.X, 0)),
        _ => Quaternion.FromEuler(new Vector3(0, _headRotation.X, 0)),
    };
    
    public void MoveTransform(Transform3D transform) => GlobalTransform = transform;
}
