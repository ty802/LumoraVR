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
        [Export] public Node PlayerSpawner; // Changed type to Node to accept both MultiplayerSpawner and CustomPlayerSpawner
        [Export] public Node3D PlayerRoot;

        public System.Collections.Generic.Dictionary<int, PlayerInfo> PlayerList = new();
        public System.Collections.Generic.Dictionary<string, Prefab> Prefabs = new();

        private PlayerCharacterController _local;
        private CustomPlayerSpawner _customPlayerSpawner; // Cache reference to custom spawner if exists

        public PlayerCharacterController GetPlayer(int id)
        {
            // First try to get player via CustomPlayerSpawner if available
            if (_customPlayerSpawner != null)
            {
                var player = _customPlayerSpawner.GetPlayer(id);
                if (player != null)
                {
                    return player;
                }
            }

            // Fallback to old method
            var tryFind = PlayerRoot?.GetChildren().OfType<PlayerCharacterController>().FirstOrDefault(i => i.Authority == id);
            return tryFind;
        }

        public PlayerCharacterController GetLocalPlayer()
        {
            if (!IsInstanceValid(_local))
            {
                if (PlayerRoot == null)
                {
                    Logger.Warn("PlayerRoot is null");
                    return null;
                }

                if (PlayerRoot.GetChildCount() == 0)
                {
                    Logger.Log("PlayerRoot has no children");
                    return null;
                }

                var localId = Multiplayer.GetUniqueId();

                // First try to get local player via CustomPlayerSpawner if available
                if (_customPlayerSpawner != null)
                {
                    _local = _customPlayerSpawner.GetPlayer(localId);
                    if (_local != null)
                    {
                        return _local;
                    }
                }

                // Fallback to old method
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
                SetupCustomPlayerSpawner();
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

        private void SetupCustomPlayerSpawner()
        {
            // Check if PlayerSpawner is already a CustomPlayerSpawner
            _customPlayerSpawner = PlayerSpawner as CustomPlayerSpawner;

            if (_customPlayerSpawner != null)
            {
                Logger.Log("Found CustomPlayerSpawner in PlayerSpawner export");

                // Update its configuration
                if (PlayerRoot != null)
                {
                    _customPlayerSpawner.SpawnRootPath = PlayerRoot.GetPath();
                    _customPlayerSpawner.PlayerScene = PlayerCharacterController.PackedScene;
                }
                return;
            }

            // Check if PlayerManager has a CustomPlayerSpawner
            if (PlayerManager.Instance?.PlayerSpawner != null)
            {
                _customPlayerSpawner = PlayerManager.Instance.PlayerSpawner;
                Logger.Log("Using CustomPlayerSpawner from PlayerManager");
                return;
            }

            // Try to find in the scene tree
            _customPlayerSpawner = GetNodeOrNull<CustomPlayerSpawner>("%CustomPlayerSpawner");
            if (_customPlayerSpawner != null)
            {
                Logger.Log("Found CustomPlayerSpawner in scene tree");

                // Update its configuration
                if (PlayerRoot != null)
                {
                    _customPlayerSpawner.SpawnRootPath = PlayerRoot.GetPath();
                    _customPlayerSpawner.PlayerScene = PlayerCharacterController.PackedScene;
                }
                return;
            }

            // Create a new CustomPlayerSpawner if needed
            if (PlayerRoot != null)
            {
                _customPlayerSpawner = new CustomPlayerSpawner
                {
                    Name = "CustomPlayerSpawner",
                    SpawnRootPath = PlayerRoot.GetPath(),
                    PlayerScene = PlayerCharacterController.PackedScene
                };
                AddChild(_customPlayerSpawner);

                // Connect to signals
                _customPlayerSpawner.PlayerSpawned += OnCustomPlayerSpawned;
                _customPlayerSpawner.PlayerRemoved += OnCustomPlayerRemoved;

                Logger.Log("Created new CustomPlayerSpawner");
            }
        }

        private void OnCustomPlayerSpawned(PlayerCharacterController player)
        {
            // Handle player spawned event
            if (player.Authority == Multiplayer.GetUniqueId())
            {
                _local = player;
                Logger.Log($"Local player spawned with ID {player.Authority}");
            }
            else
            {
                Logger.Log($"Remote player spawned with ID {player.Authority}");
            }

            // Ensure player is in the player list
            if (!PlayerList.ContainsKey(player.Authority))
            {
                PlayerList[player.Authority] = new PlayerInfo { Name = $"Player {player.Authority}" };
                SendUpdatedPlayerList();
            }
        }

        private void OnCustomPlayerRemoved(int playerId)
        {
            // Handle player removed event
            if (playerId == Multiplayer.GetUniqueId())
            {
                _local = null;
                Logger.Log($"Local player with ID {playerId} removed");
            }
            else
            {
                Logger.Log($"Remote player with ID {playerId} removed");
            }

            // Remove from player list
            PlayerList.Remove(playerId);
            SendUpdatedPlayerList();
        }

        private void SetupPlayerSpawner()
        {
            if (PlayerSpawner != null)
            {
                if (PlayerManager.Instance != null && PlayerManager.Instance.PlayerRoot != null)
                {
                    var multiplayerSpawner = PlayerSpawner as MultiplayerSpawner;
                    if (multiplayerSpawner != null)
                    {
                        multiplayerSpawner.SpawnPath = PlayerManager.Instance.PlayerRoot.GetPath();
                        multiplayerSpawner.SpawnFunction = new Callable(this, nameof(SpawnPlayerCustom));
                        Logger.Log("PlayerSpawner (legacy) configured with SpawnPath: " + multiplayerSpawner.SpawnPath);
                    }
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
                var multiplayerSpawner = PlayerSpawner as MultiplayerSpawner;
                if (multiplayerSpawner != null)
                {
                    multiplayerSpawner.Spawn(Variant.From(authority));
                }
                else if (_customPlayerSpawner != null)
                {
                    _customPlayerSpawner.SpawnPlayer(authority, position ?? Vector3.Zero);
                }

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
            // Handle receiving prefab
        }

        public void RequestPrefab(string prefabName)
        {
            // Handle requesting prefab
        }

        [Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = SerializationHelpers.WorldUpdateChannel)]
        private void InternalSpawnPlayer(int authority, Vector3 position)
        {
            try
            {
                // Use the CustomPlayerSpawner if available
                if (_customPlayerSpawner != null)
                {
                    _customPlayerSpawner.SpawnPlayer(authority, position);
                    Logger.Log($"Player with authority {authority} spawned via CustomPlayerSpawner from RPC");
                    return;
                }

                // Fallback to manual instantiation
                var charController = PlayerCharacterController.PackedScene.Instantiate<PlayerCharacterController>();
                charController.SetPlayerAuthority(authority);
                charController.Name = authority.ToString();
                PlayerRoot.AddChild(charController);
                charController.GlobalPosition = position;
                Logger.Log($"Player with authority {authority} spawned at position {position} via fallback method.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error spawning player with authority {authority}: {ex.Message}");
            }
        }

        private void InternalRemovePlayer(int authority)
        {
            try
            {
                // Use the CustomPlayerSpawner if available
                if (_customPlayerSpawner != null)
                {
                    _customPlayerSpawner.RemovePlayer(authority);
                    Logger.Log($"Player with authority {authority} removed via CustomPlayerSpawner");
                    return;
                }

                // Fallback to manual removal
                var charController = PlayerRoot.GetChildren().FirstOrDefault(i => i is PlayerCharacterController cont && cont.Authority == authority);
                if (charController is null)
                {
                    Logger.Warn($"Attempted to remove player with authority {authority}, but they were not found.");
                    return;
                }
                PlayerRoot.RemoveChild(charController);
                charController.QueueFree();
                Logger.Log($"Player with authority {authority} removed via fallback method.");
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

                    // Use the CustomPlayerSpawner
                    if (_customPlayerSpawner != null)
                    {
                        _customPlayerSpawner.SpawnPlayer(authority, position ?? Vector3.Zero);
                        Logger.Log($"Player with authority {authority} spawned via CustomPlayerSpawner");
                    }
                    // PlayerManager
                    else if (PlayerManager.Instance != null)
                    {
                        PlayerManager.Instance.SpawnPlayer(authority, position ?? Vector3.Zero);
                        Logger.Log($"Player with authority {authority} spawned via PlayerManager");
                    }
                    // Legacy MultiplayerSpawner 
                    else
                    {
                        var multiplayerSpawner = PlayerSpawner as MultiplayerSpawner;
                        if (multiplayerSpawner != null)
                        {
                            SpawnPlayerUsingSpawner(authority, position);
                            Logger.Log($"Player with authority {authority} spawned via legacy spawner");
                        }
                        else
                        {
                            InternalSpawnPlayer(authority, position ?? Vector3.Zero);
                            Logger.Log($"Player with authority {authority} spawned directly via fallback");
                        }
                    }

                    // Add to player list if not there already
                    if (!PlayerList.ContainsKey(authority))
                    {
                        PlayerList[authority] = new PlayerInfo { Name = $"Player {authority}" };
                        SendUpdatedPlayerList();
                    }

                    // Send RPC to other clients
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
                try
                {
                    // Use the CustomPlayerSpawner
                    if (_customPlayerSpawner != null)
                    {
                        _customPlayerSpawner.RemovePlayer(authority);
                        Logger.Log($"Player with authority {authority} removed via CustomPlayerSpawner");
                    }
                    // Fallback
                    else
                    {
                        InternalRemovePlayer(authority);
                        Logger.Log($"Player with authority {authority} removed via fallback method");
                    }

                    // Remove from player list
                    PlayerList.Remove(authority);
                    SendUpdatedPlayerList();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in RemovePlayer: {ex.Message}\nStack trace: {ex.StackTrace}");
                }
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
            if (_customPlayerSpawner != null)
            {
                return _customPlayerSpawner.GetPlayer(authority) != null;
            }

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