using Godot;
using Aquamarine.Source.Core.WorldTemplates;
using AquaLogger = Aquamarine.Source.Logging.Logger;
using Aquamarine.Source.Management;
using Aquamarine.Source.Scene.RootObjects;

namespace Aquamarine.Source.Core;

/// <summary>
/// Represents a single world instance in the scene tree.
/// Worlds can be enabled/disabled to switch between them.
/// 
/// </summary>
public partial class WorldInstance : Node
{
	public enum WorldPrivacyLevel
	{
		Hidden,
		Private,
		Public
	}

	public World World { get; private set; }
	public string WorldId { get; private set; }
	public string WorldName { get; set; }
	public string TemplateName { get; set; }
	public bool IsActive { get; private set; }
	public Texture2D PreviewTexture { get; private set; }
	public WorldPrivacyLevel Privacy { get; set; } = WorldPrivacyLevel.Hidden;

	public WorldInstance()
	{
		WorldId = System.Guid.NewGuid().ToString();
	}

	public override void _Ready()
	{
		base._Ready();

		// Create the World if it doesn't exist
		if (World == null)
		{
			World = new World();
			World.Name = "World"; // Set descriptive name instead of @Node@XXX
			AddChild(World);
			AquaLogger.Log($"WorldInstance created: {WorldId}");
		}

		EnsureUserSpawnInfrastructure();
	}

	/// <summary>
	/// Apply a template to this world instance.
	/// </summary>
	public void ApplyTemplate(string templateName)
	{
		if (World == null)
		{
			AquaLogger.Error("Cannot apply template: World is null");
			return;
		}

		var template = TemplateManager.GetTemplate(templateName);
		if (template == null)
		{
			AquaLogger.Error($"Template '{templateName}' not found");
			return;
		}

		template.ApplyWithSetup(World);
		TemplateName = templateName;
		AquaLogger.Log($"Applied template '{templateName}' to world '{WorldName}' with automatic UserRoot and SpawnPoints");
		PreviewTexture = template.GetPreviewTexture();
		EnsureUserSpawnInfrastructure();
	}

	/// <summary>
	/// Activate this world (make it visible and process).
	/// </summary>
	public void Activate()
	{
		ProcessMode = ProcessModeEnum.Inherit;
		if (World != null)
		{
			World.ProcessMode = ProcessModeEnum.Inherit;
			// World.RootSlot is a Node3D which has Show()
			if (World.RootSlot != null)
			{
				World.RootSlot.Show();
			}
		}
		IsActive = true;
		AquaLogger.Log($"World '{WorldName}' activated");
	}

	/// <summary>
	/// Deactivate this world (hide and pause processing).
	/// </summary>
	public void Deactivate()
	{
		ProcessMode = ProcessModeEnum.Disabled;
		if (World != null)
		{
			World.ProcessMode = ProcessModeEnum.Disabled;
			// World.RootSlot is a Node3D which has Hide()
			if (World.RootSlot != null)
			{
				World.RootSlot.Hide();
			}
		}
		IsActive = false;
		AquaLogger.Log($"World '{WorldName}' deactivated");
	}

	/// <summary>
	/// Setup user spawning infrastructure in the world by ensuring a SimpleUserSpawn component is present.
	/// </summary>
	private void EnsureUserSpawnInfrastructure()
	{
		if (World == null || World.RootSlot == null)
		{
			AquaLogger.Warn("WorldInstance: Cannot setup user spawn - World or RootSlot is null");
			return;
		}

		// Look for existing SimpleUserSpawn component
		var spawnSlot = World.RootSlot.FindChild("UserSpawner", false);
		if (spawnSlot == null)
		{
			spawnSlot = World.RootSlot.AddSlot("UserSpawner");
			AquaLogger.Log("WorldInstance: Created UserSpawner slot");
		}

		var userSpawn = spawnSlot.GetComponent<Components.SimpleUserSpawn>();
		if (userSpawn == null)
		{
			userSpawn = spawnSlot.AttachComponent<Components.SimpleUserSpawn>();
			userSpawn.PlayerScene.Value = PlayerCharacterController.PackedScene;
			AquaLogger.Log("WorldInstance: Added SimpleUserSpawn component");
		}
	}
}
