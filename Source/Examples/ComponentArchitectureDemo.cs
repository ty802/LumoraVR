using Godot;
using Aquamarine.Source.Core;
using Aquamarine.Source.Core.Components;
// using Aquamarine.Source.Inspector; // REMOVED: Inspector system temporarily disabled
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Examples;

/// <summary>
/// Demonstration of the new Slot-Component architecture.
/// Shows how to create worlds, slots, and components programmatically.
/// </summary>
public partial class ComponentArchitectureDemo : Node3D
{
    private World _demoWorld;
    // private InspectorSystem _inspector; // REMOVED: Inspector system temporarily disabled
    // private HierarchyBrowser _hierarchy; // REMOVED: Inspector system temporarily disabled

    public override void _Ready()
    {
        AquaLogger.Log("=== Component Architecture Demo ===");
        AquaLogger.Log("Demonstrating Slot-Component architecture");

        CreateDemoWorld();
        // SetupInspectorUI(); // REMOVED: Inspector system temporarily disabled
        CreateDemoContent();
    }

    /// <summary>
    /// Create a new World instance.
    /// </summary>
    private void CreateDemoWorld()
    {
        _demoWorld = new World();
        _demoWorld.WorldName.Value = "Demo World";
        AddChild(_demoWorld);

        AquaLogger.Log($"Created world: {_demoWorld.WorldName.Value}");
    }

    // REMOVED: Inspector system temporarily disabled
    // /// <summary>
    // /// Setup the inspector UI for viewing and editing.
    // /// </summary>
    // private void SetupInspectorUI()
    // {
    //     // Create inspector panel
    //     _inspector = new InspectorSystem();
    //     _inspector.Position = new Vector2(10, 10);
    //
    //     // Create hierarchy browser
    //     _hierarchy = new HierarchyBrowser();
    //     _hierarchy.Position = new Vector2(10, 450);
    //     _hierarchy.SetWorld(_demoWorld);
    //
    //     // Connect signals
    //     _hierarchy.SlotSelected += (slot) => _inspector.SelectSlot(slot);
    //
    //     // Add to UI layer (would normally go in a CanvasLayer)
    //     var canvas = new CanvasLayer();
    //     AddChild(canvas);
    //     canvas.AddChild(_inspector);
    //     canvas.AddChild(_hierarchy);
    //
    //     AquaLogger.Log("Inspector UI created");
    // }

    /// <summary>
    /// Create demo content to showcase the architecture.
    /// </summary>
    private void CreateDemoContent()
    {
        // Example 1: Simple cube with mesh renderer
        var cubeSlot = _demoWorld.RootSlot.AddSlot("Cube");
        cubeSlot.LocalPosition.Value = new Vector3(0, 1, 0);

        var cubeMesh = cubeSlot.AttachComponent<MeshRendererComponent>();
        cubeMesh.MeshData.Value = new BoxMesh();
        cubeMesh.MaterialData.Value = new StandardMaterial3D { AlbedoColor = Colors.Red };

        AquaLogger.Log("Created cube with MeshRenderer");

        // Example 2: Point light
        var lightSlot = _demoWorld.RootSlot.AddSlot("Point Light");
        lightSlot.LocalPosition.Value = new Vector3(2, 3, 2);

        var light = lightSlot.AttachComponent<LightComponent>();
        light.Type.Value = LightComponent.LightType.Point;
        light.LightColor.Value = Colors.Yellow;
        light.Energy.Value = 2.0f;
        light.Range.Value = 10.0f;

        AquaLogger.Log("Created point light");

        // Example 3: Hierarchy of slots
        var parentSlot = _demoWorld.RootSlot.AddSlot("Parent");
        parentSlot.LocalPosition.Value = new Vector3(-2, 0, 0);
        parentSlot.Tag.Value = "demo";

        var child1 = parentSlot.AddSlot("Child 1");
        child1.LocalPosition.Value = new Vector3(0, 1, 0);

        var child2 = parentSlot.AddSlot("Child 2");
        child2.LocalPosition.Value = new Vector3(0, -1, 0);

        AquaLogger.Log("Created slot hierarchy");

        // Example 4: Sphere with collider
        var sphereSlot = _demoWorld.RootSlot.AddSlot("Sphere");
        sphereSlot.LocalPosition.Value = new Vector3(2, 1, 0);

        var sphereMesh = sphereSlot.AttachComponent<MeshRendererComponent>();
        sphereMesh.MeshData.Value = new SphereMesh();
        sphereMesh.MaterialData.Value = new StandardMaterial3D { AlbedoColor = Colors.Blue };

        var sphereCollider = sphereSlot.AttachComponent<ColliderComponent>();
        sphereCollider.ShapeType.Value = ColliderComponent.ColliderType.Sphere;
        sphereCollider.Size.Value = new Vector3(0.5f, 0.5f, 0.5f);

        AquaLogger.Log("Created sphere with collider");

        // Demonstrate Sync<T> system
        AquaLogger.Log("\n=== Demonstrating Sync<T> System ===");
        AquaLogger.Log($"Cube position: {cubeSlot.LocalPosition.Value}");

        // Subscribe to changes
        cubeSlot.LocalPosition.OnChanged += (pos) => {
            AquaLogger.Log($"Cube position changed to: {pos}");
        };

        // Change value (will trigger event)
        cubeSlot.LocalPosition.Value = new Vector3(0, 2, 0);

        AquaLogger.Log("\n=== Demo Content Created ===");
        AquaLogger.Log("Demo content ready!");
    }

    /// <summary>
    /// Demonstrate finding slots by tag.
    /// </summary>
    private void DemonstrateFinding()
    {
        AquaLogger.Log("\n=== Finding Slots Demo ===");

        // Find by tag
        var demoSlots = _demoWorld.FindSlotsByTag("demo");
        foreach (var slot in demoSlots)
        {
            AquaLogger.Log($"Found slot with 'demo' tag: {slot.SlotName.Value}");
        }

        // Find by name
        var cubeSlot = _demoWorld.FindSlotByName("Cube");
        if (cubeSlot != null)
        {
            AquaLogger.Log($"Found cube slot at position: {cubeSlot.LocalPosition.Value}");
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Press F1 to run finding demo
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.F1)
        {
            DemonstrateFinding();
        }

        // Press F2 to create a random cube
        if (@event is InputEventKey keyEvent2 && keyEvent2.Pressed && keyEvent2.Keycode == Key.F2)
        {
            CreateRandomCube();
        }

        // Press F3 to expand all hierarchy - REMOVED: Inspector system temporarily disabled
        // if (@event is InputEventKey keyEvent3 && keyEvent3.Pressed && keyEvent3.Keycode == Key.F3)
        // {
        //     _hierarchy.ExpandAll();
        // }
    }

    private void CreateRandomCube()
    {
        var random = new RandomNumberGenerator();
        random.Randomize();

        var slot = _demoWorld.RootSlot.AddSlot($"Random Cube {random.Randi()}");
        slot.LocalPosition.Value = new Vector3(
            random.RandfRange(-5, 5),
            random.RandfRange(0, 3),
            random.RandfRange(-5, 5)
        );

        var mesh = slot.AttachComponent<MeshRendererComponent>();
        mesh.MeshData.Value = new BoxMesh();
        mesh.MaterialData.Value = new StandardMaterial3D
        {
            AlbedoColor = new Color(
                random.Randf(),
                random.Randf(),
                random.Randf()
            )
        };

        AquaLogger.Log($"Created random cube at {slot.LocalPosition.Value}");
        // _hierarchy.FocusSlot(slot); // REMOVED: Inspector system temporarily disabled
    }
}
