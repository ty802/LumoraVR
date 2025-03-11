using System;
using System.Linq;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Scene;
using Aquamarine.Source.Scene.RootObjects;
using Aquamarine.Source.Scene.UI;
using Bones.Core;
using Godot;
using Godot.Collections;
using Array = Godot.Collections.Array;

namespace Aquamarine.Source.Management
{
	public partial class MultiplayerScene : Node
	{
		public static MultiplayerScene Instance;

		[Export] public MultiplayerSpawner Spawner;
		[Export] public MultiplayerSpawner PlayerSpawner;
		[Export] public Node3D PlayerRoot;

		public System.Collections.Generic.Dictionary<int, PlayerInfo> PlayerList = new();
		public System.Collections.Generic.Dictionary<string, Prefab> Prefabs = new();

        private PlayerCharacterController _local;

        public PlayerCharacterController GetPlayer(int id)
		{
			var tryFind = PlayerRoot.GetChildren().OfType<PlayerCharacterController>().FirstOrDefault(i => i.Authority == id);
			return tryFind;
		}

        public PlayerCharacterController GetLocalPlayer()
        {
            if (!IsInstanceValid(_local))
            {
                if (PlayerRoot == null)
                {
                    Logger.Warn("PlayerRoot is null"); // Log only once or less frequently
                    return null;
                }

                if (PlayerRoot.GetChildCount() == 0)
                {
                    Logger.Log("PlayerRoot has no children"); // Consider throttling this log
                    return null;
                }

                var localId = Multiplayer.GetUniqueId();
                _local = PlayerRoot.GetChildren()
                    .OfType<PlayerCharacterController>()
                    .FirstOrDefault(i => i.Authority == localId);
            }
            return _local;
        }

        public override void _Ready()
        {
            base._Ready();
            Instance = this;

            try
            {
                SetupPlayerRoot();

                SetupPlayerSpawner();

                if (Spawner != null)
                {
                    Logger.Log("Spawner is assigned.");
                }
                else
                {
                    Logger.Warn("Spawner is not assigned!");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing MultiplayerScene: {ex.Message}\nStack trace: {ex.StackTrace}");
            }

            Logger.Log("MultiplayerScene initialized.");
        }
        public void InitializeForServer()
        {
            Logger.Log("MultiplayerScene initialized for server.");
        }
        private void SetupPlayerRoot()
        {
            if (PlayerManager.Instance != null)
            {
                PlayerRoot = PlayerManager.Instance.PlayerRoot;
                Logger.Log("PlayerRoot set from PlayerManager: " + (PlayerRoot != null));
            }
            else
            {
                PlayerRoot = GetNodeOrNull<Node3D>("%PlayerRoot") ?? GetNodeOrNull<Node3D>("/root/Root/PlayerRoot");
                if (PlayerRoot == null)
                {
                    Logger.Error("PlayerRoot not found at %PlayerRoot or /root/Root/PlayerRoot");
                    LogSceneTree(GetTree().Root);
                    return;
                }
                Logger.Log("PlayerRoot found: " + PlayerRoot.Name);
            }
        }
        private void SetupPlayerSpawner()
        {
            if (PlayerSpawner != null)
            {
                if (PlayerManager.Instance != null && PlayerManager.Instance.PlayerRoot != null)
                {
                    PlayerSpawner.SpawnPath = PlayerManager.Instance.PlayerRoot.GetPath();
                    PlayerSpawner.SpawnFunction = new Callable(this, nameof(SpawnPlayerCustom));
                    Logger.Log("PlayerSpawner configured with SpawnPath: " + PlayerSpawner.SpawnPath);
                }
                else
                {
                    Logger.Error("Cannot set SpawnPath: PlayerManager.Instance or PlayerRoot is null");
                }
            }
            else
            {
                Logger.Warn("PlayerSpawner is not assigned!");
            }
        }

        private void SetUpSpawner()
        {
            if (Spawner != null)
            {
                if (PlayerManager.Instance != null && PlayerManager.Instance.PlayerRoot != null)
                {
                    Spawner.SpawnPath = PlayerManager.Instance.PlayerRoot.GetPath();
                    Logger.Log("Spawner configured with SpawnPath: " + Spawner.SpawnPath);
                }
                else
                {
                    Logger.Error("Cannot set SpawnPath for Spawner: PlayerManager.Instance or PlayerRoot is null");
                }
            }
            else
            {
                Logger.Warn("Spawner is not assigned!");
            }
        }
        private Node SpawnPlayerCustom(Variant data)
		{
			int playerId = (int)data;
			var charController = PlayerCharacterController.PackedScene.Instantiate<PlayerCharacterController>();
			charController.SetPlayerAuthority(playerId);
			charController.Name = playerId.ToString();
			Logger.Log($"Custom spawning player with authority {playerId}");
			return charController;
		}
		public void SpawnPlayerUsingSpawner(int authority, Vector3? position = null)
		{
			if (IsMultiplayerAuthority())
			{
				PlayerSpawner.Spawn(Variant.From(authority));

				this.CreateTimer(0.1f, () => {
					var player = GetPlayer(authority);
					if (player != null)
					{
						player.GlobalPosition = position ?? Vector3.Zero;
					}
				});

				Logger.Log($"Spawning player with authority {authority} via spawner.");
			}
		}
		public void SendUpdatedPlayerList()
		{
			if (!IsMultiplayerAuthority()) return;
			
			var dict = new Dictionary();

			foreach (var playerPair in PlayerList)
			{
				dict[playerPair.Key] = new Dictionary
				{
					{"name", playerPair.Value.Name },
				};
			}
				
			Rpc(MethodName.UpdatePlayerList, dict);
			
			UpdatePlayerNametags();
		}

		private void UpdatePlayerNametags()
		{
			foreach (var player in PlayerList)
			{
				var controller = GetPlayer(player.Key);
				if (controller?.Nametag is Nameplate nameplate)
				{
					nameplate.SetText(player.Value.Name);
				}
			}
		}

		[Rpc(CallLocal = false, TransferChannel = SerializationHelpers.WorldUpdateChannel, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
		private void UpdatePlayerList(Dictionary playerList)
		{
			var newList = new System.Collections.Generic.Dictionary<int, PlayerInfo>();

			foreach (var pair in playerList)
			{
				var valueDict = pair.Value.AsGodotDictionary();
				
				var player = new PlayerInfo
				{
					Name = valueDict["name"].AsString(),
				};
				newList[pair.Key.AsInt32()] = player;
			}

			PlayerList = newList;
			
			UpdatePlayerNametags();
		}
		[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferChannel = SerializationHelpers.SessionControlChannel, CallLocal = false)]
		public void DisconnectPlayer()
		{
			if (!IsMultiplayerAuthority()) return;

			var id = Multiplayer.GetRemoteSenderId();
			Logger.Log($"Player {id} has disconnected");
			
			Multiplayer.MultiplayerPeer.DisconnectPeer(id);
		}
		[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferChannel = SerializationHelpers.SessionControlChannel, CallLocal = false)]
		public void SetPlayerName(string name)
		{
			if (!IsMultiplayerAuthority()) return;
			
			if (!PlayerList.TryGetValue(Multiplayer.GetRemoteSenderId(), out var player)) return;
			
			player.Name = name;
			SendUpdatedPlayerList();
		}
		
		public void SendAllPrefabs(int? user = null)
		{
			if (!IsMultiplayerAuthority()) return;

			foreach (var prefab in Prefabs) SendPrefab(prefab.Key, user);
		}
		public void SendPrefab(string prefabName, int? user = null)
		{
			if (user.HasValue) RpcId(user.Value, MethodName.RecievePrefab, prefabName);
			else Rpc(MethodName.RecievePrefab, prefabName);
		}
		[Rpc(TransferChannel = SerializationHelpers.PrefabChannel)]
		private void RecievePrefab(string prefabName)
		{
			
		}
		
		public void RequestPrefab(string prefabName)
		{
			
		}

		[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = SerializationHelpers.WorldUpdateChannel)]
		private void InternalSpawnPlayer(int authority, Vector3 position)
		{
			try
			{
				var charController = PlayerCharacterController.PackedScene.Instantiate<PlayerCharacterController>();
				charController.SetPlayerAuthority(authority);
				charController.Name = authority.ToString();
				PlayerRoot.AddChild(charController);
				charController.GlobalPosition = position; // PlayerRoot  charController
				Logger.Log($"Player with authority {authority} spawned at position {position}.");
			}
			catch (Exception ex)
			{
				Logger.Error($"Error spawning player with authority {authority}: {ex.Message}");
			}
		}

		//[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = SerializationHelpers.WorldUpdateChannel)]
		private void InternalRemovePlayer(int authority)
		{
			try
			{
				var charController = PlayerRoot.GetChildren().FirstOrDefault(i => i is PlayerCharacterController cont && cont.Authority == authority);
				if (charController is null)
				{
					Logger.Warn($"Attempted to remove player with authority {authority}, but they were not found.");
					return;
				}
				PlayerRoot.RemoveChild(charController);
				charController.QueueFree();
				Logger.Log($"Player with authority {authority} removed.");
			}
			catch (Exception ex)
			{
				Logger.Error($"Error removing player with authority {authority}: {ex.Message}");
			}
		}
        public void SpawnPlayer(int authority, Vector3? position = null)
        {
            try
            {
                if (IsMultiplayerAuthority())
                {
                    // PlayerRoot
                    if (PlayerRoot == null)
                    {
                        Logger.Error("Cannot spawn player: PlayerRoot is null");
                        return;
                    }

                    // PlayerManager
                    if (PlayerManager.Instance != null)
                    {
                        PlayerManager.Instance.SpawnPlayer(authority, position ?? Vector3.Zero);
                        Logger.Log($"Player with authority {authority} spawned via PlayerManager");
                    }
                    // MultiplayerSpawner 
                    else if (PlayerSpawner != null)
                    {
                        SpawnPlayerUsingSpawner(authority, position);
                        Logger.Log($"Player with authority {authority} spawned via spawner");
                    }
                    //
                    else
                    {
                        InternalSpawnPlayer(authority, position ?? Vector3.Zero);
                        Logger.Log($"Player with authority {authority} spawned directly");
                    }

                    Rpc(MethodName.InternalSpawnPlayer, authority, position ?? Vector3.Zero);
                    Logger.Log($"SpawnPlayer RPC sent for authority {authority}");
                }
                else
                {
                    Logger.Warn($"SpawnPlayer call ignored due to lack of multiplayer authority for authority {authority}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in SpawnPlayer: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        public void RemovePlayer(int authority)
		{
			if (IsMultiplayerAuthority())
			{
				InternalRemovePlayer(authority);
				//Rpc(MethodName.InternalRemovePlayer, authority);
				Logger.Log($"RemovePlayer called for authority {authority}.");
			}
			else
			{
				Logger.Warn($"RemovePlayer call ignored due to lack of multiplayer authority for authority {authority}.");
			}
		}
        public bool IsPlayerRootValid()
        {
            if (PlayerRoot == null)
            {
                Logger.Warn("PlayerRoot is null");
                return false;
            }
            return true;
        }

        public bool IsPlayerSpawned(int authority)
        {
            if (!IsPlayerRootValid()) return false;

            var player = GetPlayer(authority);
            return player != null;
        }

        private void LogSceneTree(Node node, string indent = "")
        {
            Logger.Log(indent + node.Name + " (" + node.GetType().Name + ")");
            foreach (var child in node.GetChildren())
            {
                LogSceneTree(child, indent + "  ");
            }
        }

    }
}
