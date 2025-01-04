using System;
using System.Linq;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Scene;
using Aquamarine.Source.Scene.RootObjects;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Management
{
	public partial class MultiplayerScene : Node
	{
		public static MultiplayerScene Instance;

		[Export] public MultiplayerSpawner Spawner;
		[Export] public MultiplayerSpawner PlayerSpawner;
		[Export] public Node3D PlayerRoot;
		[Export] public Array<int> PlayerList = [];

		public System.Collections.Generic.Dictionary<string, Prefab> Prefabs = new();
        
		public override void _Ready()
		{
			base._Ready();
			Instance = this;
			Logger.Log("MultiplayerScene initialized.");
		}
		public void SendUpdatedPlayerList()
		{
			if (IsMultiplayerAuthority()) Rpc(MethodName.UpdatePlayerList, PlayerList.ToArray());
		}

		[Rpc(CallLocal = false, TransferChannel = SerializationHelpers.WorldUpdateChannel, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
		private void UpdatePlayerList(int[] playerList) => PlayerList = new Array<int>(playerList);
        
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
        
		
		//[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = SerializationHelpers.WorldUpdateChannel)]
		private void InternalSpawnPlayer(int authority, Vector3 position)
		{
			try
			{
				var charController = PlayerCharacterController.PackedScene.Instantiate<PlayerCharacterController>();
				charController.SetPlayerAuthority(authority);
				charController.Name = authority.ToString();
				PlayerRoot.AddChild(charController);
				PlayerRoot.GlobalPosition = position;
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
			if (IsMultiplayerAuthority())
			{
				InternalSpawnPlayer(authority, position ?? Vector3.Zero);
				//Rpc(MethodName.InternalSpawnPlayer, authority, position ?? Vector3.Zero);
				Logger.Log($"SpawnPlayer called for authority {authority}.");
			}
			else
			{
				Logger.Warn($"SpawnPlayer call ignored due to lack of multiplayer authority for authority {authority}.");
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
	}
}
