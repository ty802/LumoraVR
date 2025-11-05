using Godot;
using Aquamarine.Source.Core.Components;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core.UserSpace;

/// <summary>
/// UserSpace - personal UI space that follows the user.
/// Contains the dash/menu and personal UI elements.
/// 
/// </summary>
public partial class UserSpace : Node3D
{
    private Slot _userSpaceRoot;
    private Slot _dashSlot;
    private World _world;
    private Node3D _targetTransform; // What this userspace follows (usually player head)

    /// <summary>
    /// Distance from the target (usually 0.5-1.0m in front of face)
    /// </summary>
    public float Distance { get; set; } = 0.6f;

    /// <summary>
    /// Whether the UserSpace is currently visible.
    /// </summary>
    public bool IsVisible
    {
        get => _userSpaceRoot?.ActiveSelf.Value ?? false;
        set
        {
            if (_userSpaceRoot != null)
            {
                _userSpaceRoot.ActiveSelf.Value = value;
            }
        }
    }

    public override void _Ready()
    {
        AquaLogger.Log("UserSpace initialized");
    }

    /// <summary>
    /// Initialize the UserSpace for a world and target.
    /// </summary>
    public void Initialize(World world, Node3D target)
    {
        _world = world;
        _targetTransform = target;

        // Create UserSpace root slot
        _userSpaceRoot = _world.RootSlot.AddSlot("UserSpace");
        _userSpaceRoot.Tag.Value = "userspace";
        _userSpaceRoot.Persistent.Value = false; // Don't save userspace

        // Create dash
        CreateDash();

        // Start hidden
        IsVisible = false;

        AquaLogger.Log("UserSpace created for user");
    }

    /// <summary>
    /// Create the dash (main menu panel).
    /// </summary>
    private void CreateDash()
    {
        _dashSlot = _userSpaceRoot.AddSlot("Dash");
        _dashSlot.LocalPosition.Value = new Vector3(0, 0, 0);

        // Create dash background panel
        var panelSlot = _dashSlot.AddSlot("Panel");
        var panelMesh = panelSlot.AttachComponent<MeshRendererComponent>();

        var quadMesh = new QuadMesh();
        quadMesh.Size = new Vector2(0.6f, 0.4f);
        panelMesh.MeshData.Value = quadMesh;

        var panelMaterial = new StandardMaterial3D();
        panelMaterial.AlbedoColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        panelMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        panelMesh.MaterialData.Value = panelMaterial;

        // Add some glow
        var glowSlot = _dashSlot.AddSlot("Glow");
        glowSlot.LocalPosition.Value = new Vector3(0, 0, -0.01f);
        var glowLight = glowSlot.AttachComponent<LightComponent>();
        glowLight.Type.Value = LightComponent.LightType.Point;
        glowLight.LightColor.Value = new Color(0.3f, 0.6f, 1.0f);
        glowLight.Energy.Value = 0.5f;
        glowLight.Range.Value = 1.0f;

        AquaLogger.Log("Dash created");
    }

    /// <summary>
    /// Toggle UserSpace visibility.
    /// </summary>
    public void Toggle()
    {
        IsVisible = !IsVisible;
        AquaLogger.Log($"UserSpace toggled: {IsVisible}");
    }

    /// <summary>
    /// Update UserSpace position to follow target.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_userSpaceRoot == null || _targetTransform == null || !IsVisible) return;

        // Position in front of target
        var targetPos = _targetTransform.GlobalPosition;
        var targetRot = _targetTransform.GlobalRotation;

        // Calculate position in front of the target
        var forward = targetRot * Vector3.Forward;
        var targetPosition = targetPos + forward * Distance;

        // Look at target
        _userSpaceRoot.LocalPosition.Value = targetPosition;

        // Face the target
        var lookRot = Quaternion.Identity; // Calculate proper look-at rotation
        _userSpaceRoot.LocalRotation.Value = lookRot;
    }

    /// <summary>
    /// Add a custom UI element to the UserSpace.
    /// </summary>
    public Slot AddUIElement(string name, Vector3 localPosition)
    {
        var slot = _userSpaceRoot.AddSlot(name);
        slot.LocalPosition.Value = localPosition;
        return slot;
    }

    /// <summary>
    /// Get the dash slot for adding UI.
    /// </summary>
    public Slot GetDashSlot() => _dashSlot;
}
