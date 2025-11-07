using System;
using System.Linq;
using Godot;
using Aquamarine.Source.Core.Components;

namespace Aquamarine.Source.Core.WorldTemplates;

/// <summary>
/// Base class for world templates.
/// Templates define starting configurations for new worlds (Grid, Empty, Social, etc.)
/// 
/// </summary>
public abstract class WorldTemplate
{
	/// <summary>
	/// Display name of this template.
	/// </summary>
	public abstract string Name { get; }

	/// <summary>
	/// Description of this template.
	/// </summary>
	public abstract string Description { get; }

	/// <summary>
	/// Icon/thumbnail path for this template.
	/// </summary>
	public virtual string IconPath => "res://icon.svg";

	/// <summary>
	/// Category (Basic, Social, Game, Creative, etc.)
	/// </summary>
	public virtual string Category => "Basic";

	/// <summary>
	/// Primary color used when generating preview textures.
	/// </summary>
	public virtual Color PreviewPrimaryColor => new Color(0.2f, 0.2f, 0.24f);

	/// <summary>
	/// Secondary color used when generating preview textures.
	/// </summary>
	public virtual Color PreviewSecondaryColor => new Color(0.05f, 0.05f, 0.07f);

	/// <summary>
	/// Resolution for generated preview textures.
	/// </summary>
	protected virtual Vector2I PreviewTextureSize => new Vector2I(512, 288);

	private Texture2D _cachedPreviewTexture;

	/// <summary>
	/// Apply this template to a world.
	/// This method is called by templates to customize the world.
	/// The base setup (UserRoot, etc.) is handled automatically.
	/// </summary>
	public abstract void Apply(World world);

	/// <summary>
	/// Apply the template with automatic setup of core components.
	/// This creates UserRoot and initial spawn points.
	/// </summary>
	public void ApplyWithSetup(World world)
	{
		// Apply template-specific content FIRST
		Apply(world);

		// Ensure at least one spawn point exists
		EnsureSpawnPoints(world);
		
		// NOTE: UserRoot is now created by WorldTemplate.ApplyWithSetup via GetOrCreateUserRoot
		// which is called by ConfigureWorld in WorldManager
		// We don't need to create it here anymore
	}

	/// <summary>
	/// Get (or generate) a preview texture representing this template.
	/// </summary>
	public Texture2D GetPreviewTexture()
	{
		if (_cachedPreviewTexture != null)
		{
			return _cachedPreviewTexture;
		}

		var size = PreviewTextureSize;
		var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
		var primary = PreviewPrimaryColor;
		var secondary = PreviewSecondaryColor;

		var heightMinusOne = Mathf.Max(1, size.Y - 1);
		for (int y = 0; y < size.Y; y++)
		{
			float t = (float)y / heightMinusOne;
			var rowColor = primary.Lerp(secondary, t);
			for (int x = 0; x < size.X; x++)
			{
				image.SetPixel(x, y, rowColor);
			}
		}

		_cachedPreviewTexture = ImageTexture.CreateFromImage(image);
		return _cachedPreviewTexture;
	}

	/// <summary>
	/// Ensure the world has at least one spawn point.
	/// If no spawn points exist, creates a default one.
	/// </summary>
	private void EnsureSpawnPoints(World world)
	{
		var spawnSlots = world.FindSlotsByTag("spawn");
		if (!spawnSlots.Any())
		{
			// Create default spawn point at origin
			var spawnSlot = CreateSpawnPoint(world.RootSlot, new Vector3(0, 1, 0));
			spawnSlot.AttachComponent<SpawnPointComponent>();
		}
		else
		{
			// Attach SpawnPointComponent to existing spawn slots that don't have it
			foreach (var spawnSlot in spawnSlots)
			{
				if (spawnSlot.GetComponent<SpawnPointComponent>() == null)
				{
					spawnSlot.AttachComponent<SpawnPointComponent>();
				}
			}
		}
	}

	/// <summary>
	/// Helper to create a grid floor.
	/// </summary>
	protected void CreateGridFloor(Slot parent, int size = 20, float spacing = 1.0f)
	{
		var gridSlot = parent.AddSlot("Grid Floor");
		gridSlot.LocalPosition.Value = new Vector3(0, 0, 0);

		// Create grid mesh
		var mesh = new MeshRendererComponent();
		gridSlot.AttachComponent<MeshRendererComponent>();

		// Create plane mesh for floor
		var planeMesh = new PlaneMesh();
		planeMesh.Size = new Vector2(size * spacing, size * spacing);
		planeMesh.SubdivideWidth = size;
		planeMesh.SubdivideDepth = size;

		var meshComp = gridSlot.GetComponent<MeshRendererComponent>();
		meshComp.MeshData.Value = planeMesh;

		// Grid material
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.2f, 0.2f, 0.2f);
		material.Metallic = 0.0f;
		material.Roughness = 1.0f;
		meshComp.MaterialData.Value = material;

		// Add collider
		var collider = gridSlot.AttachComponent<ColliderComponent>();
		collider.ShapeType.Value = ColliderComponent.ColliderType.Box;
		collider.Size.Value = new Vector3(size * spacing, 0.1f, size * spacing);
	}

	/// <summary>
	/// Helper to create a directional light (sun).
	/// </summary>
	protected void CreateSunLight(Slot parent)
	{
		var sunSlot = parent.AddSlot("Sun");
		sunSlot.LocalPosition.Value = new Vector3(0, 10, 0);
		var rotationDegrees = new Vector3(-45, 30, 0);
		sunSlot.LocalRotation.Value = Quaternion.FromEuler(rotationDegrees * (Mathf.Pi / 180));

		var light = sunSlot.AttachComponent<LightComponent>();
		light.Type.Value = LightComponent.LightType.Directional;
		light.LightColor.Value = Colors.White;
		light.Energy.Value = 1.0f;
		light.CastShadow.Value = true;
	}

	/// <summary>
	/// Helper to create ambient environment.
	/// </summary>
	protected void CreateEnvironment(Slot parent)
	{
		var envSlot = parent.AddSlot("Environment");
		// TODO: Add WorldEnvironment component when implemented
	}

	/// <summary>
	/// Helper to create spawn point.
	/// </summary>
	protected Slot CreateSpawnPoint(Slot parent, Vector3 position)
	{
		var spawnSlot = parent.AddSlot("Spawn Point");
		spawnSlot.LocalPosition.Value = position;
		spawnSlot.Tag.Value = "spawn";
		return spawnSlot;
	}
}
