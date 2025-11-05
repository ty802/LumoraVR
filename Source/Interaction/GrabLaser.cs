using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;
using System.Collections.Generic;

namespace Aquamarine.Source.Interaction;

/// <summary>
/// Laser beam that extends from the hand for grabbing and interacting with objects.
/// </summary>
[GlobalClass]
public partial class GrabLaser : Node3D
{
    [Export] public float MaxDistance { get; set; } = 10.0f;
    [Export] public Color LaserColor { get; set; } = new Color(0.3f, 0.7f, 1.0f, 0.8f);
    [Export] public Color LaserHitColor { get; set; } = new Color(0.3f, 1.0f, 0.5f, 0.8f);
    [Export] public float LaserWidth { get; set; } = 0.002f;
    [Export] public bool ShowLaser { get; set; } = true;

    private MeshInstance3D _laserBeam;
    private MeshInstance3D _laserDot;
    private IGrabbable _hoveredObject;
    private IGrabbable _grabbedObject;
    private PhysicsDirectSpaceState3D _spaceState;
    private bool _isGrabbing;

    public IGrabbable HoveredObject => _hoveredObject;
    public IGrabbable GrabbedObject => _grabbedObject;
    public bool IsGrabbing => _isGrabbing;

    public override void _Ready()
    {
        CreateLaserVisuals();
        _spaceState = GetWorld3D().DirectSpaceState;

        AquaLogger.Log("GrabLaser initialized");
    }

    private void CreateLaserVisuals()
    {
        // Create laser beam
        _laserBeam = new MeshInstance3D();
        _laserBeam.Name = "LaserBeam";

        var beamMesh = new CylinderMesh();
        beamMesh.TopRadius = LaserWidth;
        beamMesh.BottomRadius = LaserWidth;
        beamMesh.Height = MaxDistance;
        _laserBeam.Mesh = beamMesh;

        var beamMaterial = new StandardMaterial3D();
        beamMaterial.AlbedoColor = LaserColor;
        beamMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        beamMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        beamMaterial.DisableReceiveShadows = true;
        beamMaterial.NoDepthTest = true;
        _laserBeam.MaterialOverride = beamMaterial;

        // Position beam to extend forward
        _laserBeam.RotationDegrees = new Vector3(90, 0, 0);
        _laserBeam.Position = new Vector3(0, 0, -MaxDistance / 2);
        _laserBeam.Visible = ShowLaser;

        AddChild(_laserBeam);

        // Create laser dot at the end
        _laserDot = new MeshInstance3D();
        _laserDot.Name = "LaserDot";

        var dotMesh = new SphereMesh();
        dotMesh.Radius = 0.01f;
        dotMesh.Height = 0.02f;
        _laserDot.Mesh = dotMesh;

        var dotMaterial = new StandardMaterial3D();
        dotMaterial.AlbedoColor = LaserColor;
        dotMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        dotMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        dotMaterial.DisableReceiveShadows = true;
        dotMaterial.NoDepthTest = true;
        _laserDot.MaterialOverride = dotMaterial;

        _laserDot.Position = new Vector3(0, 0, -MaxDistance);
        _laserDot.Visible = ShowLaser;

        AddChild(_laserDot);
    }

    public override void _Process(double delta)
    {
        if (!ShowLaser && !_isGrabbing)
        {
            _laserBeam.Visible = false;
            _laserDot.Visible = false;
            return;
        }

        UpdateLaser();
        UpdateGrabbedObject();
    }

    private void UpdateLaser()
    {
        // Perform raycast from hand forward
        var from = GlobalPosition;
        var to = GlobalPosition + (-GlobalTransform.Basis.Z * MaxDistance);

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;

        var result = _spaceState.IntersectRay(query);

        IGrabbable newHovered = null;
        float distance = MaxDistance;

        if (result.Count > 0)
        {
            var hitPosition = (Vector3)result["position"];
            var collider = result["collider"].AsGodotObject();
            distance = GlobalPosition.DistanceTo(hitPosition);

            // Check if the hit object is grabbable
            if (collider is Node node)
            {
                newHovered = FindGrabbableInNode(node);
            }

            // Update laser dot position
            _laserDot.Position = new Vector3(0, 0, -distance);
        }
        else
        {
            _laserDot.Position = new Vector3(0, 0, -MaxDistance);
        }

        // Update laser beam length
        var beamMesh = _laserBeam.Mesh as CylinderMesh;
        if (beamMesh != null)
        {
            beamMesh.Height = distance;
        }
        _laserBeam.Position = new Vector3(0, 0, -distance / 2);

        // Update laser color based on hover
        var beamMaterial = _laserBeam.MaterialOverride as StandardMaterial3D;
        var dotMaterial = _laserDot.MaterialOverride as StandardMaterial3D;

        if (newHovered != null && newHovered.CanBeGrabbed())
        {
            if (beamMaterial != null) beamMaterial.AlbedoColor = LaserHitColor;
            if (dotMaterial != null) dotMaterial.AlbedoColor = LaserHitColor;
        }
        else
        {
            if (beamMaterial != null) beamMaterial.AlbedoColor = LaserColor;
            if (dotMaterial != null) dotMaterial.AlbedoColor = LaserColor;
        }

        // Handle hover changes
        if (newHovered != _hoveredObject)
        {
            if (_hoveredObject != null && _hoveredObject is GrabbableComponent oldComp)
            {
                oldComp.HideHighlight();
            }

            _hoveredObject = newHovered;

            if (_hoveredObject != null && _hoveredObject is GrabbableComponent newComp)
            {
                newComp.ShowHighlight();
            }
        }

        // Show/hide laser
        _laserBeam.Visible = ShowLaser || _isGrabbing;
        _laserDot.Visible = ShowLaser || _isGrabbing;
    }

    private IGrabbable FindGrabbableInNode(Node node)
    {
        // Check the node itself
        if (node is IGrabbable grabbable)
            return grabbable;

        // Check for GrabbableComponent in node or parents
        var current = node;
        while (current != null)
        {
            foreach (var child in current.GetChildren())
            {
                if (child is IGrabbable childGrabbable)
                    return childGrabbable;
            }

            current = current.GetParent();
            if (current == GetTree().Root)
                break;
        }

        return null;
    }

    private void UpdateGrabbedObject()
    {
        if (_isGrabbing && _grabbedObject != null)
        {
            // Object is being held - update continues in GrabbableComponent
        }
    }

    /// <summary>
    /// Attempt to grab the hovered object.
    /// </summary>
    public void TriggerGrab()
    {
        if (_isGrabbing)
            return;

        if (_hoveredObject != null && _hoveredObject.CanBeGrabbed())
        {
            _grabbedObject = _hoveredObject;
            _grabbedObject.OnGrabbed(this);
            _isGrabbing = true;

            AquaLogger.Log($"Grabbed {_grabbedObject.GrabbableNode.Name}");
        }
    }

    /// <summary>
    /// Release the currently grabbed object.
    /// </summary>
    public void ReleaseGrab()
    {
        if (!_isGrabbing || _grabbedObject == null)
            return;

        _grabbedObject.OnReleased();
        AquaLogger.Log($"Released {_grabbedObject.GrabbableNode.Name}");

        _grabbedObject = null;
        _isGrabbing = false;
    }

    /// <summary>
    /// Toggle laser visibility.
    /// </summary>
    public void SetLaserVisible(bool visible)
    {
        ShowLaser = visible;
    }
}
