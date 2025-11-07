using Godot;
using Aquamarine.Source.Core.Components;

namespace Aquamarine.Source.Core.WorldTemplates;

/// <summary>
/// Grid world template - infinite grid floor with spawn point.
/// The classic VR starting template.
/// </summary>
public class GridTemplate : WorldTemplate
{
	public override string Name => "Grid";
	public override string Description => "Infinite grid floor perfect for building and testing";
	public override string Category => "Basic";
	public override Color PreviewPrimaryColor => new Color(0.12f, 0.36f, 0.42f);
	public override Color PreviewSecondaryColor => new Color(0.01f, 0.08f, 0.11f);

	public override void Apply(World world)
	{
		world.WorldName.Value = "Grid World";

		// Create environment slot for organization
		var environment = world.RootSlot.AddSlot("Environment");

		// Create grid floor with mesh renderer
		var gridFloor = environment.AddSlot("Grid Floor");
		var meshRenderer = gridFloor.AttachComponent<MeshRendererComponent>();
		
		// Use PlaneMesh for the floor
		var planeMesh = new PlaneMesh();
		planeMesh.Size = new Vector2(50, 50);
		planeMesh.SubdivideWidth = 50;
		planeMesh.SubdivideDepth = 50;
		meshRenderer.MeshData.Value = planeMesh;
		
		// Grid material
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.2f, 0.2f, 0.2f);
		material.Metallic = 0.0f;
		material.Roughness = 1.0f;
		meshRenderer.MaterialData.Value = material;
		
		// Add collision
		var collider = gridFloor.AttachComponent<ColliderComponent>();
		collider.ShapeType.Value = ColliderComponent.ColliderType.Box;
		collider.Size.Value = new Vector3(50, 0.1f, 50);

		// Create sun light
		var sun = environment.AddSlot("Sun");
		sun.LocalPosition.Value = new Vector3(0, 10, 0);
		sun.LocalRotation.Value = Quaternion.FromEuler(new Vector3(-45, 30, 0) * (Mathf.Pi / 180));
		
		var sunLight = sun.AttachComponent<LightComponent>();
		sunLight.Type.Value = LightComponent.LightType.Directional;
		sunLight.LightColor.Value = Colors.White;
		sunLight.Energy.Value = 1.0f;
		sunLight.CastShadow.Value = true;

		// Create spawn point with SpawnPointComponent
		var spawnSlot = world.RootSlot.AddSlot("Spawn Point");
		spawnSlot.LocalPosition.Value = new Vector3(0, 1, 0);
		spawnSlot.Tag.Value = "spawn";
		spawnSlot.AttachComponent<SpawnPointComponent>();

		// Add ambient light
		var ambient = environment.AddSlot("Ambient Light");
		var ambientLight = ambient.AttachComponent<LightComponent>();
		ambientLight.Type.Value = LightComponent.LightType.Directional;
		ambientLight.LightColor.Value = new Color(0.3f, 0.3f, 0.4f);
		ambientLight.Energy.Value = 0.3f;
		ambientLight.CastShadow.Value = false;
	}
}
