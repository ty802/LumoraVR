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
    public World World { get; private set; }
    public string WorldId { get; private set; }
    public string WorldName { get; set; }
    public string TemplateName { get; set; }
    public bool IsActive { get; private set; }
    public Texture2D PreviewTexture { get; private set; }

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
            AddChild(World);
            AquaLogger.Log($"WorldInstance created: {WorldId}");
        }

        EnsureMultiplayerInfrastructure();
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
        EnsureMultiplayerInfrastructure();
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

    private void EnsureMultiplayerInfrastructure()
    {
        var multiplayerScene = GetNodeOrNull<MultiplayerScene>("MultiplayerScene");
        if (multiplayerScene == null)
        {
            multiplayerScene = new MultiplayerScene
            {
                Name = "MultiplayerScene"
            };
            AddChild(multiplayerScene);
            AquaLogger.Log("Created MultiplayerScene node for world instance.");
        }

        var playerRoot = multiplayerScene.GetNodeOrNull<Node3D>("PlayerRoot");
        if (playerRoot == null)
        {
            playerRoot = new Node3D { Name = "PlayerRoot" };
            multiplayerScene.AddChild(playerRoot);
            AquaLogger.Log("Created PlayerRoot for MultiplayerScene.");
        }
        multiplayerScene.PlayerRoot = playerRoot;

        var spawner = multiplayerScene.GetNodeOrNull<CustomPlayerSpawner>("CustomPlayerSpawner");
        if (spawner == null)
        {
            spawner = new CustomPlayerSpawner
            {
                Name = "CustomPlayerSpawner"
            };
            multiplayerScene.AddChild(spawner);
            AquaLogger.Log("Created CustomPlayerSpawner for MultiplayerScene.");
        }
        spawner.PlayerScene = PlayerCharacterController.PackedScene;
        spawner.SetSpawnRoot(playerRoot.GetPath());
        multiplayerScene.PlayerSpawner = spawner;

        var playerSync = multiplayerScene.GetNodeOrNull<Node>("CustomPlayerSync");
        if (playerSync == null)
        {
            playerSync = new Node { Name = "CustomPlayerSync" };
            multiplayerScene.AddChild(playerSync);
            AquaLogger.Log("Created placeholder CustomPlayerSync node.");
        }
    }
}
