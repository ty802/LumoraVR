using Aquamarine.Source.Logging;
using Aquamarine.Source.Scene.RootObjects;
using Godot;
using System;
using System.Linq;

namespace Aquamarine.Source.Management
{
    public partial class PlayerManager : Node
    {
        public static PlayerManager Instance;

        [Export] public Node3D PlayerRoot;

        public override void _Ready()
        {
            Instance = this;

            try
            {
                if (PlayerRoot == null)
                {
                    PlayerRoot = GetNodeOrNull<Node3D>("/root/Root/PlayerRoot");

                    if (PlayerRoot == null && GetParent() != null)
                    {
                        PlayerRoot = GetParent().GetNodeOrNull<Node3D>("PlayerRoot");
                    }

                    if (PlayerRoot == null)
                    {
                        PlayerRoot = new Node3D();
                        PlayerRoot.Name = "PlayerRoot";
                        GetParent().CallDeferred(Node.MethodName.AddChild, PlayerRoot);
                        Logger.Log("Created new PlayerRoot node (deferred)");
                    }
                }

                Logger.Log("PlayerManager initialized with PlayerRoot: " + (PlayerRoot != null));
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in PlayerManager._Ready(): {ex.Message}");
            }
        }

        public void SpawnPlayer(int authority, Vector3 position)
        {
            try
            {
                if (PlayerRoot == null)
                {
                    Logger.Error($"Cannot spawn player with authority {authority}: PlayerRoot is null");
                    return;
                }

                var charController = PlayerCharacterController.PackedScene.Instantiate<PlayerCharacterController>();
                charController.SetPlayerAuthority(authority);
                charController.Name = authority.ToString();

                PlayerRoot.CallDeferred(Node.MethodName.AddChild, charController);

                CallDeferred(nameof(SetPlayerPosition), charController, position);

                Logger.Log($"Player with authority {authority} spawn requested at position {position}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error spawning player with authority {authority}: {ex.Message}");
            }
        }

        private void SetPlayerPosition(PlayerCharacterController player, Vector3 position)
        {
            if (IsInstanceValid(player))
            {
                player.GlobalPosition = position;
                Logger.Log($"Player position set to {position}");
            }
        }

        public PlayerCharacterController GetPlayer(int id)
        {
            if (PlayerRoot == null)
            {
                Logger.Warn($"Cannot get player with id {id}: PlayerRoot is null");
                return null;
            }

            return PlayerRoot.GetChildren().OfType<PlayerCharacterController>().FirstOrDefault(i => i.Authority == id);
        }
    }
}
