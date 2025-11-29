using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Components;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Logging;
using Lumora.Core.Physics;
using Lumora.Core.HelioUI;

namespace Lumora.Core.Templates;

/// <summary>
/// World templates for initializing worlds with default content.
/// Provides standard world initialization patterns.
///
/// NOTE: This is a cross-platform template definition.
/// Platform-specific component creation (MeshRenderer, BoxCollider, etc.)
/// needs to be handled by the platform layer (Aquamarine/Godot).
/// </summary>
public static class WorldTemplates
{
	/// <summary>
	/// Get a template by name.
	/// </summary>
	public static Action<World> GetTemplate(string name)
	{
		return name switch
		{
			"LocalHome" => LocalHome,
			"Grid" => GridSpace,
			"Empty" => Empty,
			"SocialSpace" => SocialSpace,
			_ => Empty
		};
	}

	/// <summary>
	/// Empty world - minimal setup.
	/// </summary>
	public static void Empty(World world)
	{
		Logger.Log("WorldTemplates: Initializing Empty world");
		// Just the basics - RootSlot is already created
	}

	/// <summary>
	/// LocalHome template - User's local home world.
	/// Standard local home environment pattern.
	/// </summary>
	public static void LocalHome(World world)
	{
		Logger.Log("WorldTemplates: Initializing LocalHome world");

		// Create spawn area
		var spawnSlot = world.RootSlot.AddSlot("SpawnArea");
		spawnSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
		spawnSlot.Tag.Value = "spawn";
		spawnSlot.AttachComponent<SimpleUserSpawn>();

		// Create directional light
		var lightSlot = world.RootSlot.AddSlot("DirectionalLight");
		lightSlot.LocalPosition.Value = new float3(0f, 5f, 0f);
		lightSlot.LocalRotation.Value = floatQ.Euler(0.785f, -0.785f, 0f); // 45° down, angled

		// Create ground plane
		var groundSlot = world.RootSlot.AddSlot("Ground");
		groundSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
		groundSlot.Tag.Value = "floor";

		// Create procedural box mesh for ground
		var groundMesh = groundSlot.AttachComponent<BoxMesh>();
		groundMesh.Size.Value = new float3(20f, 0.1f, 20f);
		groundMesh.UVScale.Value = new float3(20f, 1f, 20f);

		// Add static collider matching the ground mesh
		var groundCollider = groundSlot.AttachComponent<BoxCollider>();
		groundCollider.Type.Value = ColliderType.Static;
		groundCollider.Size.Value = groundMesh.Size.Value;
		groundCollider.Offset.Value = new float3(0f, -groundMesh.Size.Value.y * 0.5f, 0f);

		// Create test cubes to visualize the world
		CreateTestCube(world, "TestCube1", new float3(0f, 1f, 2f));
		CreateTestCube(world, "TestCube2", new float3(-2f, 1f, 2f));
		CreateTestCube(world, "TestCube3", new float3(2f, 1f, 2f));

		// Create ambient point light for extra visibility
		var ambientLightSlot = world.RootSlot.AddSlot("AmbientLight");
		ambientLightSlot.LocalPosition.Value = new float3(0f, 3f, 0f);

		// Create UI Panels container
		var uiPanelsSlot = world.RootSlot.AddSlot("UIPanels");
		uiPanelsSlot.LocalPosition.Value = new float3(0f, 1.4f, -1.2f);

		// Create User Inspector Wizard
		CreateUserInspectorPanel(uiPanelsSlot, new float3(-1.2f, 0f, 0f));

		// Create Engine Debug Wizard
		CreateEngineDebugPanel(uiPanelsSlot, new float3(1.2f, 0f, 0f));

		Logger.Log($"WorldTemplates: LocalHome initialized with {world.RootSlot.ChildCount} root slots");
	}

	/// <summary>
	/// Helper: Create User Inspector panel in world space.
	/// </summary>
	private static void CreateUserInspectorPanel(Slot parent, float3 offset)
	{
		var panelSlot = parent.AddSlot("UserInspector");
		panelSlot.LocalPosition.Value = offset;
		panelSlot.LocalRotation.Value = floatQ.Euler(0f, 0f, 0f);
		panelSlot.LocalScale.Value = new float3(0.35f, 0.35f, 0.35f); // World-space UI scale

		panelSlot.AttachComponent<UserInspectorWizard>();

		Logger.Log("WorldTemplates: Created User Inspector panel");
	}

	/// <summary>
	/// Helper: Create Engine Debug panel in world space.
	/// </summary>
	private static void CreateEngineDebugPanel(Slot parent, float3 offset)
	{
		var panelSlot = parent.AddSlot("EngineDebug");
		panelSlot.LocalPosition.Value = offset;
		panelSlot.LocalRotation.Value = floatQ.Euler(0f, 0f, 0f);
		panelSlot.LocalScale.Value = new float3(0.35f, 0.35f, 0.35f); // World-space UI scale

		panelSlot.AttachComponent<EngineDebugWizard>();

		Logger.Log("WorldTemplates: Created Engine Debug panel");
	}

	/// <summary>
	/// GridSpace template - Grid environment for building.
	/// Standard grid workspace pattern.
	/// </summary>
	public static void GridSpace(World world)
	{
		Logger.Log("WorldTemplates: Initializing GridSpace world");

		// Create spawn area with circle pattern
		var spawnSlot = world.RootSlot.AddSlot("SpawnArea");
		spawnSlot.LocalPosition.Value = new float3(0f, 0.01f, 0f);
		spawnSlot.Tag.Value = "spawn";

		// Create directional light
		var lightSlot = world.RootSlot.AddSlot("DirectionalLight");
		lightSlot.LocalPosition.Value = new float3(0f, 2f, 0f);
		lightSlot.LocalRotation.Value = floatQ.Euler(0.26f, 3.14f, 3.14f);

		// Create large ground grid (100x100)
		var groundSlot = world.RootSlot.AddSlot("Ground");
		groundSlot.Tag.Value = "floor";

		// Create procedural box mesh for ground
		var groundMesh = groundSlot.AttachComponent<BoxMesh>();
		groundMesh.Size.Value = new float3(100f, 0.1f, 100f);
		groundMesh.UVScale.Value = new float3(100f, 1f, 100f);

		// Add static collider matching the ground mesh
		var groundCollider = groundSlot.AttachComponent<BoxCollider>();
		groundCollider.Type.Value = ColliderType.Static;
		groundCollider.Size.Value = groundMesh.Size.Value;
		groundCollider.Offset.Value = new float3(0f, -groundMesh.Size.Value.y * 0.5f, 0f);

		Logger.Log($"WorldTemplates: GridSpace initialized with {world.RootSlot.ChildCount} root slots");
	}

	/// <summary>
	/// SocialSpace template - Social gathering environment.
	/// Standard social gathering space pattern.
	/// </summary>
	public static void SocialSpace(World world)
	{
		Logger.Log("WorldTemplates: Initializing SocialSpace world");

		// Create spawn area with larger radius for social gathering
		var spawnSlot = world.RootSlot.AddSlot("SpawnArea");
		spawnSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
		spawnSlot.Tag.Value = "spawn";

		// Create ambient lighting
		var lightSlot = world.RootSlot.AddSlot("DirectionalLight");
		lightSlot.LocalPosition.Value = new float3(0f, 5f, 0f);

		// Create ground platform
		var groundSlot = world.RootSlot.AddSlot("Ground");
		groundSlot.Tag.Value = "floor";

		// Create procedural box mesh for ground
		var groundMesh = groundSlot.AttachComponent<BoxMesh>();
		groundMesh.Size.Value = new float3(40f, 0.1f, 40f);
		groundMesh.UVScale.Value = new float3(40f, 1f, 40f);

		// Add static collider matching the ground mesh
		var groundCollider = groundSlot.AttachComponent<BoxCollider>();
		groundCollider.Type.Value = ColliderType.Static;
		groundCollider.Size.Value = groundMesh.Size.Value;
		groundCollider.Offset.Value = new float3(0f, -groundMesh.Size.Value.y * 0.5f, 0f);

		Logger.Log($"WorldTemplates: SocialSpace initialized with {world.RootSlot.ChildCount} root slots");
	}

	/// <summary>
	/// Apply a template to a world.
	/// </summary>
	public static void ApplyTemplate(World world, string templateName)
	{
		var template = GetTemplate(templateName);
		template?.Invoke(world);
	}

	/// <summary>
	/// Helper: Create a test cube with BoxMesh.
	/// Used for visualizing the world until full asset system is implemented.
	/// </summary>
	private static void CreateTestCube(World world, string name, float3 position)
	{
		var cubeSlot = world.RootSlot.AddSlot(name);
		cubeSlot.LocalPosition.Value = position;

		// Create procedural box mesh
		var boxMesh = cubeSlot.AttachComponent<BoxMesh>();
		boxMesh.Size.Value = new float3(0.5f, 0.5f, 0.5f);
		boxMesh.UVScale.Value = new float3(1f, 1f, 1f);

		// TODO: Platform-specific collider component
		// var collider = cubeSlot.AttachComponent<BoxCollider>();

		Logger.Log($"WorldTemplates: Created test cube '{name}' at position {position}");
	}
}
