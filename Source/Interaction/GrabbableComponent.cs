using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Interaction;

/// <summary>
/// Component that makes an object grabbable.
/// Attach this to any Node3D or RigidBody3D to allow it to be grabbed.
/// </summary>
[GlobalClass]
public partial class GrabbableComponent : Node, IGrabbable
{
    [Export] public Color HighlightColor { get; set; } = new Color(0.3f, 0.7f, 1.0f, 0.5f);
    [Export] public bool CanGrab { get; set; } = true;
    [Export] public bool PreserveVelocity { get; set; } = true;

    private Node3D _parentNode;
    private RigidBody3D _rigidBody;
    private Node3D _grabber;
    private bool _isGrabbed;
    private Transform3D _grabOffset;
    private Vector3 _lastPosition;
    private Vector3 _velocity;
    private MeshInstance3D _highlightMesh;

    public Node3D GrabbableNode => _parentNode;
    public bool IsGrabbed => _isGrabbed;

    public override void _Ready()
    {
        _parentNode = GetParent<Node3D>();
        if (_parentNode == null)
        {
            AquaLogger.Error("GrabbableComponent must be a child of a Node3D!");
            return;
        }

        // Check if parent is a RigidBody
        _rigidBody = _parentNode as RigidBody3D;

        _lastPosition = _parentNode.GlobalPosition;

        AquaLogger.Log($"GrabbableComponent initialized on {_parentNode.Name}");
    }

    public bool CanBeGrabbed()
    {
        return CanGrab && !_isGrabbed;
    }

    public void OnGrabbed(Node3D grabber)
    {
        if (!CanBeGrabbed())
            return;

        _grabber = grabber;
        _isGrabbed = true;

        // Calculate grab offset
        _grabOffset = grabber.GlobalTransform.AffineInverse() * _parentNode.GlobalTransform;

        // Disable physics if it's a RigidBody
        if (_rigidBody != null)
        {
            _rigidBody.Freeze = true;
        }

        AquaLogger.Log($"{_parentNode.Name} grabbed by {grabber.Name}");
    }

    public void OnReleased()
    {
        if (!_isGrabbed)
            return;

        _isGrabbed = false;

        // Re-enable physics if it's a RigidBody
        if (_rigidBody != null)
        {
            _rigidBody.Freeze = false;

            // Apply velocity if preserving momentum
            if (PreserveVelocity && _velocity.Length() > 0.01f)
            {
                _rigidBody.LinearVelocity = _velocity;
            }
        }

        AquaLogger.Log($"{_parentNode.Name} released");
        _grabber = null;
    }

    public override void _Process(double delta)
    {
        if (_isGrabbed && _grabber != null && IsInstanceValid(_grabber))
        {
            // Update object position to follow grabber
            Transform3D targetTransform = _grabber.GlobalTransform * _grabOffset;
            _parentNode.GlobalTransform = targetTransform;

            // Calculate velocity for throwing
            Vector3 currentPosition = _parentNode.GlobalPosition;
            _velocity = (currentPosition - _lastPosition) / (float)delta;
            _lastPosition = currentPosition;
        }
    }

    /// <summary>
    /// Show highlight effect when hovered.
    /// </summary>
    public void ShowHighlight()
    {
        if (_highlightMesh != null)
        {
            _highlightMesh.Visible = true;
            return;
        }

        // Create highlight mesh if it doesn't exist
        var meshInstance = _parentNode.GetNode<MeshInstance3D>("MeshInstance3D");
        if (meshInstance == null)
        {
            // Try to find any MeshInstance3D in children
            meshInstance = _parentNode.FindChild("*", true, false) as MeshInstance3D;
        }

        if (meshInstance != null && meshInstance.Mesh != null)
        {
            _highlightMesh = new MeshInstance3D();
            _highlightMesh.Name = "GrabHighlight";
            _highlightMesh.Mesh = meshInstance.Mesh;
            _highlightMesh.Scale = meshInstance.Scale * 1.05f; // Slightly larger

            // Create highlight material
            var material = new StandardMaterial3D();
            material.AlbedoColor = HighlightColor;
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.DisableReceiveShadows = true;
            material.NoDepthTest = true;

            _highlightMesh.MaterialOverride = material;
            _highlightMesh.Visible = true;

            meshInstance.AddChild(_highlightMesh);
        }
    }

    /// <summary>
    /// Hide highlight effect.
    /// </summary>
    public void HideHighlight()
    {
        if (_highlightMesh != null)
        {
            _highlightMesh.Visible = false;
        }
    }
}
