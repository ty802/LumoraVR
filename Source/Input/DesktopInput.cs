using System;
using Aquamarine.Source.Logging;
using Godot;

namespace Aquamarine.Source.Input;

public partial class DesktopInput : Node3D, IInputProvider
{
    [Export] private Camera3D _camera;

    private Vector2 _headRotation;

    public override void _Ready()
    {
        try
        {
            base._Ready();

            IInputProvider.Instance = this;
            
            ProcessPriority = -9;
            
            Logger.Log("DesktopInput initialized successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error during DesktopInput initialization: {ex.Message}");
        }
    }
    public override void _Process(double delta)
    {
        base._Process(delta);
        var deltaf = (float)delta;
        var camMovement = InputManager.CameraMovement * deltaf;
        var horizontal = Mathf.Wrap(_headRotation.X + camMovement.X, 0, Mathf.Tau);
        var vertical = Mathf.Clamp(_headRotation.Y + camMovement.Y, -Mathf.Pi / 2, Mathf.Pi / 2);

        _headRotation = new Vector2(horizontal, vertical);

        _camera.Quaternion = Quaternion.FromEuler(new Vector3(_headRotation.Y, _headRotation.X, 0));
        _camera.Position = Vector3.Up * 1.8f;
    }

    public static readonly PackedScene PackedScene = ResourceLoader.Load<PackedScene>("res://Scenes/DesktopInput.tscn");
    public bool IsVR => false;
    public Vector3 GetPlayspaceMovementDelta => Vector3.Zero;
    public Vector2 GetMovementInputAxis => InputManager.Movement;
    public Vector3 GetLimbPosition(IInputProvider.InputLimb limb) => limb switch
    {
        IInputProvider.InputLimb.Head => Vector3.Up * 1.8f,
        //IInputProvider.InputLimb.LeftHand => expr,
        //IInputProvider.InputLimb.RightHand => expr,
        //IInputProvider.InputLimb.Hip => expr,
        //IInputProvider.InputLimb.LeftFoot => expr,
        //IInputProvider.InputLimb.RightFoot => expr,
        _ => Vector3.Zero
    };
    public Quaternion GetLimbRotation(IInputProvider.InputLimb limb) => limb switch
    {
        IInputProvider.InputLimb.Head => Quaternion.FromEuler(new Vector3(_headRotation.Y, _headRotation.X, 0)),
        //IInputProvider.InputLimb.LeftHand => expr,
        //IInputProvider.InputLimb.RightHand => expr,
        //IInputProvider.InputLimb.Hip => expr,
        //IInputProvider.InputLimb.LeftFoot => expr,
        //IInputProvider.InputLimb.RightFoot => expr,
        _ => Quaternion.Identity
    };
    public void MoveTransform(Transform3D transform) => GlobalTransform = transform;
}
