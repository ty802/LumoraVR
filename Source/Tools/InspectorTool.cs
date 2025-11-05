using Godot;
using System.Linq;
using AquaLogger = Aquamarine.Source.Logging.Logger;
using Aquamarine.Source.Core;

namespace Aquamarine.Source.Tools;

/// <summary>
/// Inspector tool for examining and modifying objects in the world.
/// Shows detailed information about slots, components, and properties.
/// </summary>
public partial class InspectorTool : BaseTool
{
    private Panel _inspectorPanel;
    private VBoxContainer _contentContainer;
    private Label _titleLabel;
    private RichTextLabel _infoLabel;
    private Slot _inspectedSlot;
    private Node3D _inspectedNode;
    private bool _isPanelVisible;

    public override string ToolName => "Inspector";

    public override void _Ready()
    {
        CreateToolMesh();
        CreateInspectorUI();
    }

    private void CreateToolMesh()
    {
        // Create a simple tool mesh (magnifying glass)
        _toolMesh = new Node3D();
        _toolMesh.Name = "InspectorMesh";

        // Handle
        var handle = new MeshInstance3D();
        handle.Mesh = new CylinderMesh
        {
            TopRadius = 0.01f,
            BottomRadius = 0.01f,
            Height = 0.15f
        };
        handle.Position = new Vector3(0, -0.075f, 0);

        var handleMaterial = new StandardMaterial3D();
        handleMaterial.AlbedoColor = new Color(0.3f, 0.3f, 0.3f);
        handle.MaterialOverride = handleMaterial;

        // Lens
        var lens = new MeshInstance3D();
        lens.Mesh = new TorusMesh
        {
            InnerRadius = 0.03f,
            OuterRadius = 0.04f
        };
        lens.RotationDegrees = new Vector3(90, 0, 0);
        lens.Position = new Vector3(0, 0.03f, 0);

        var lensMaterial = new StandardMaterial3D();
        lensMaterial.AlbedoColor = new Color(0.6f, 0.8f, 1.0f);
        lensMaterial.Metallic = 0.8f;
        lens.MaterialOverride = lensMaterial;

        _toolMesh.AddChild(handle);
        _toolMesh.AddChild(lens);
    }

    private void CreateInspectorUI()
    {
        // Create inspector panel
        _inspectorPanel = new Panel();
        _inspectorPanel.Name = "InspectorPanel";
        _inspectorPanel.CustomMinimumSize = new Vector2(400, 300);
        _inspectorPanel.Position = new Vector2(100, 100);
        _inspectorPanel.Visible = false;

        // Style the panel
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        styleBox.BorderColor = new Color(0.3f, 0.7f, 1.0f);
        styleBox.BorderWidthLeft = 2;
        styleBox.BorderWidthRight = 2;
        styleBox.BorderWidthTop = 2;
        styleBox.BorderWidthBottom = 2;
        styleBox.CornerRadiusTopLeft = 8;
        styleBox.CornerRadiusTopRight = 8;
        styleBox.CornerRadiusBottomLeft = 8;
        styleBox.CornerRadiusBottomRight = 8;
        _inspectorPanel.AddThemeStyleboxOverride("panel", styleBox);

        // Content container
        var marginContainer = new MarginContainer();
        marginContainer.AddThemeConstantOverride("margin_left", 10);
        marginContainer.AddThemeConstantOverride("margin_right", 10);
        marginContainer.AddThemeConstantOverride("margin_top", 10);
        marginContainer.AddThemeConstantOverride("margin_bottom", 10);
        _inspectorPanel.AddChild(marginContainer);

        _contentContainer = new VBoxContainer();
        marginContainer.AddChild(_contentContainer);

        // Title
        _titleLabel = new Label();
        _titleLabel.Text = "Inspector";
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.7f, 1.0f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 20);
        _contentContainer.AddChild(_titleLabel);

        // Separator
        var separator = new HSeparator();
        _contentContainer.AddChild(separator);

        // Info text
        var scrollContainer = new ScrollContainer();
        scrollContainer.CustomMinimumSize = new Vector2(0, 200);
        _contentContainer.AddChild(scrollContainer);

        _infoLabel = new RichTextLabel();
        _infoLabel.BbcodeEnabled = true;
        _infoLabel.FitContent = true;
        scrollContainer.AddChild(_infoLabel);

        // Close button
        var closeButton = new Button();
        closeButton.Text = "Close";
        closeButton.Pressed += () => HideInspector();
        _contentContainer.AddChild(closeButton);
    }

    public override void OnEquipped(ToolSlot slot)
    {
        base.OnEquipped(slot);

        // Add inspector panel to scene tree when equipped
        if (!_inspectorPanel.IsInsideTree())
        {
            GetTree().Root.AddChild(_inspectorPanel);
        }

        AquaLogger.Log("Inspector tool equipped");
    }

    public override void OnUnequipped()
    {
        base.OnUnequipped();

        // Hide and remove panel
        HideInspector();
        if (_inspectorPanel.IsInsideTree())
        {
            GetTree().Root.RemoveChild(_inspectorPanel);
        }

        AquaLogger.Log("Inspector tool unequipped");
    }

    public override void OnPrimaryAction()
    {
        // Raycast to find object to inspect
        InspectObjectAtCursor();
    }

    public override void OnSecondaryAction()
    {
        // Toggle inspector visibility
        ToggleInspector();
    }

    private void InspectObjectAtCursor()
    {
        if (_equippedSlot == null)
            return;

        // Perform raycast from tool forward
        var spaceState = GetWorld3D().DirectSpaceState;
        var from = _equippedSlot.GlobalPosition;
        var to = from + (-_equippedSlot.GlobalTransform.Basis.Z * 10.0f);

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            var collider = result["collider"].AsGodotObject();
            if (collider is Node3D node)
            {
                InspectNode(node);
            }
        }
        else
        {
            AquaLogger.Log("No object found to inspect");
        }
    }

    private void InspectNode(Node3D node)
    {
        _inspectedNode = node;
        _inspectedSlot = null;

        // Check if it's a Slot
        if (node is Slot slot)
        {
            _inspectedSlot = slot;
            DisplaySlotInfo(slot);
        }
        else
        {
            DisplayNodeInfo(node);
        }

        ShowInspector();
        AquaLogger.Log($"Inspecting: {node.Name}");
    }

    private void DisplaySlotInfo(Slot slot)
    {
        _titleLabel.Text = $"Slot: {slot.Name}";

        var info = "[b]Slot Information[/b]\n\n";
        info += $"[color=cyan]Name:[/color] {slot.Name}\n";
        info += $"[color=cyan]Tag:[/color] {slot.Tag.Value}\n";
        info += $"[color=cyan]Active:[/color] {slot.ActiveSelf.Value}\n";
        info += $"[color=cyan]Persistent:[/color] {slot.Persistent.Value}\n";
        info += $"[color=cyan]Position:[/color] {slot.Position}\n";
        info += $"[color=cyan]Rotation:[/color] {slot.Rotation}\n";
        info += $"[color=cyan]Scale:[/color] {slot.Scale}\n\n";

        info += "[b]Components:[/b]\n";
        var components = slot.GetComponents<Component>().ToList();
        if (components.Count > 0)
        {
            foreach (var component in components)
            {
                info += $"  • {component.GetType().Name}\n";
            }
        }
        else
        {
            info += "  [color=gray]No components[/color]\n";
        }

        info += "\n[b]Children:[/b]\n";
        var children = slot.GetChildren();
        int slotChildren = 0;
        foreach (var child in children)
        {
            if (child is Slot childSlot)
            {
                slotChildren++;
                info += $"  • {childSlot.Name}\n";
            }
        }
        if (slotChildren == 0)
        {
            info += "  [color=gray]No child slots[/color]\n";
        }

        _infoLabel.Text = info;
    }

    private void DisplayNodeInfo(Node3D node)
    {
        _titleLabel.Text = $"Node: {node.Name}";

        var info = "[b]Node Information[/b]\n\n";
        info += $"[color=cyan]Type:[/color] {node.GetType().Name}\n";
        info += $"[color=cyan]Name:[/color] {node.Name}\n";
        info += $"[color=cyan]Position:[/color] {node.GlobalPosition}\n";
        info += $"[color=cyan]Rotation:[/color] {node.GlobalRotation}\n";
        info += $"[color=cyan]Scale:[/color] {node.Scale}\n\n";

        // Check if it's a RigidBody
        if (node is RigidBody3D rigidBody)
        {
            info += "[b]Physics:[/b]\n";
            info += $"  [color=cyan]Mass:[/color] {rigidBody.Mass}\n";
            info += $"  [color=cyan]Velocity:[/color] {rigidBody.LinearVelocity}\n";
            info += $"  [color=cyan]Frozen:[/color] {rigidBody.Freeze}\n\n";
        }

        info += "[b]Children:[/b]\n";
        var children = node.GetChildren();
        if (children.Count > 0)
        {
            foreach (var child in children)
            {
                info += $"  • {child.Name} ({child.GetType().Name})\n";
            }
        }
        else
        {
            info += "  [color=gray]No children[/color]\n";
        }

        _infoLabel.Text = info;
    }

    private void ShowInspector()
    {
        _inspectorPanel.Visible = true;
        _isPanelVisible = true;
    }

    private void HideInspector()
    {
        _inspectorPanel.Visible = false;
        _isPanelVisible = false;
    }

    private void ToggleInspector()
    {
        if (_isPanelVisible)
        {
            HideInspector();
        }
        else if (_inspectedNode != null)
        {
            ShowInspector();
        }
    }
}
