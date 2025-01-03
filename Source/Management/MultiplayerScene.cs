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
		public void AddPrefab(string name, string prefab, int? blacklist = null)
		{
			if (!IsMultiplayerAuthority()) return;
			
			var parsed = Prefab.Deserialize(prefab);
			if (!parsed.Valid()) return;
			
			Prefabs[name] = parsed;

			if (blacklist.HasValue)
			{
				foreach (var player in PlayerList)
				{
					if (player == blacklist.Value) continue;
					RpcId(player, MethodName.InternalAddPrefab, name, prefab);
				}
			}
			else Rpc(MethodName.InternalAddPrefab, name, prefab);
		}

		public void SendAllPrefabs(int? player = null)
		{
			if (!IsMultiplayerAuthority()) return;
			
			var dict = new Dictionary();
			foreach (var (name, prefab) in Prefabs) dict[name] = prefab.Serialize();
			var json = Json.Stringify(dict);

			if (player.HasValue) RpcId(player.Value, MethodName.InternalReceiveAllPrefabs, json);
			else Rpc(MethodName.InternalReceiveAllPrefabs, json);
		}

		[Rpc]
		private void InternalReceiveAllPrefabs(string json)
		{
			var parsed = Json.ParseString(json);
			if (parsed.VariantType is not Variant.Type.Dictionary) return;
			foreach (var (name, prefab) in parsed.AsGodotDictionary<string,string>()) Prefabs[name] = Prefab.Deserialize(prefab);
		}

		[Rpc]
		private void InternalAddPrefab(string name, string prefab)
		{
			var parsed = Prefab.Deserialize(prefab);
			Prefabs[name] = parsed;
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
