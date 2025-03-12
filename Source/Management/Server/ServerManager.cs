using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Aquamarine.Source.Logging;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;
using Bones.Core;
using System.Threading.Tasks;
using static LiteNetLib.EventBasedNetListener;
using Aquamarine.Source.Networking;

namespace Aquamarine.Source.Management
{
    public partial class ServerManager : Node
    {
        [Export] public bool Advertise = true;
        // We'll use this field but find it dynamically at runtime
        private MultiplayerScene _multiplayerScene;

        public LiteNetLibMultiplayerPeer MultiplayerPeer;

        public NetManager SessionListManager;
        public EventBasedNetListener SessionListListener;
        public NetPeer MainServer;

        private string _sessionSecret;
        private bool _running = true;

        private int Port = 7000;
        private const int MaxConnections = 20;
        private const string SessionApiUrl = "https://api.xlinka.com/sessions";

        private string _publicIp;
        private string _worldName = "My World";
        private string _worldIdentifier = "placeholder";

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (ServerManager.CurrentServerType == ServerType.Local && MultiplayerPeer != null)
            {
                MultiplayerPeer.Poll(); // Poll network events manually
            }
            SessionListManager?.PollEvents();
        }

        public override void _Ready()
        {
            try
            {
                var serverType = ServerManager.CurrentServerType;

                // Initialize MultiplayerPeer
                MultiplayerPeer = new LiteNetLibMultiplayerPeer();
                // get port
                {
                    string[] args = OS.GetCmdlineArgs();
                    int index = Array.IndexOf(args, "--port");
                    if (index != -1 && args.Length > index + 1)
                    {
                        if (int.TryParse(args[index + 1], out int port))
                        {
                            Port = port;
                        }
                    }
                }
                Error error;
                switch (serverType) {
                    case ServerType.Local:
                    // Start local server
                    error = MultiplayerPeer.CreateServer(Port, 1);

                    // Check if server started successfully before setting it as the multiplayer peer
                    if (error == Error.Ok)
                    {
                        // Only set the multiplayer peer after it's properly initialized
                        Multiplayer.MultiplayerPeer = MultiplayerPeer;

                        // Now connect event handlers
                        MultiplayerPeer.PeerConnected += OnPeerConnected;
                        MultiplayerPeer.PeerDisconnected += OnPeerDisconnected;

                        Logger.Log($"Local server started on port {Port}.");

                        // Switch scene
                        GetNode("%WorldRoot")?.AddChild(ResourceLoader.Load<PackedScene>("res://Scenes/World/LocalHome.tscn").Instantiate());
                        Logger.Log("LocalHome Loaded.");

                        // Defer MultiplayerScene initialization until scene is fully loaded
                        CallDeferred(nameof(InitializeMultiplayerScene));
                    }
                    else
                    {
                        Logger.Error($"Failed to start local server: {error}");
                    }
                        break;
                    case ServerType.Standard:
                    // Start standard server
                    error = MultiplayerPeer.CreateServerNat(Port, MaxConnections);

                    // Check if server started successfully before setting it as the multiplayer peer
                    if (error == Error.Ok)
                    {
                        // Only set the multiplayer peer after it's properly initialized
                        Multiplayer.MultiplayerPeer = MultiplayerPeer;

                        // Now connect event handlers
                        MultiplayerPeer.PeerConnected += OnPeerConnected;
                        MultiplayerPeer.PeerDisconnected += OnPeerDisconnected;

                        Logger.Log($"Server started on port {Port} with max connections {MaxConnections}.");

                        // Defer MultiplayerScene initialization
                        CallDeferred(nameof(InitializeMultiplayerScene));

                        // Initialize session manager
                        SessionListListener = new EventBasedNetListener();
                        SessionListManager = new NetManager(SessionListListener)
                        {
                            IPv6Enabled = true,
                            PingInterval = 10000,
                        };
                        SessionListListener.NetworkReceiveEvent += SessionListListenerOnNetworkReceiveEvent;
                        SessionListListener.PeerDisconnectedEvent += SessionListListenerOnPeerDisconnectedEvent;
                        if(Helpers.SimpleIpHelpers.GetAvailablePortUdp(10) is not int port) throw new Exception("Failed to find available port (shit)");
                        SessionListManager.Start();
                        ConnectToSessionServer();
                    }
                    else
                    {
                        Logger.Error($"Failed to start standard server: {error}");
                    }
                    break;
                    default:
                        Logger.Error("Server type not recognized.");
                    break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing ServerManager: {ex}");
            }

            // Verify PlayerRoot
            if (PlayerManager.Instance?.PlayerRoot == null)
            {
                Logger.Error("PlayerRoot is not initialized in PlayerManager! Check Autoload settings.");
            }
            else
            {
                Logger.Log("PlayerRoot initialized successfully.");
            }
        }

        // Deferred method to initialize MultiplayerScene
        private void InitializeMultiplayerScene()
        {
            try
            {
                // First try direct access to MultiplayerScene
                var multiplayerScene = GetTree().CurrentScene.GetNodeOrNull<MultiplayerScene>("MultiplayerScene");

                if (multiplayerScene == null)
                {
                    // If not found directly, try deep search
                    multiplayerScene = FindMultiplayerSceneInChildren(GetTree().CurrentScene);
                }
                
                // If still not found, try to find it directly in the scene tree
                if (multiplayerScene == null)
                {
                    // Check if this is the LocalHome scene
                    if (GetTree().CurrentScene.Name == "Scene" && GetTree().CurrentScene.SceneFilePath.Contains("LocalHome.tscn"))
                    {
                        // For LocalHome.tscn, the scene itself is the MultiplayerScene
                        if (GetTree().CurrentScene is MultiplayerScene localHomeScene)
                        {
                            multiplayerScene = localHomeScene;
                            Logger.Log("Found MultiplayerScene: LocalHome scene is itself a MultiplayerScene");
                        }
                    }
                    
                    // Try to find it at a common path
                    if (multiplayerScene == null)
                    {
                        var scene = GetTree().Root.GetNodeOrNull("Root/WorldRoot/Scene");
                        if (scene is MultiplayerScene sceneAsMultiplayer)
                        {
                            multiplayerScene = sceneAsMultiplayer;
                            Logger.Log("Found MultiplayerScene at Root/WorldRoot/Scene");
                        }
                    }
                }

                if (multiplayerScene != null)
                {
                    _multiplayerScene = multiplayerScene;
                    multiplayerScene.InitializeForServer();
                    Logger.Log("MultiplayerScene found and initialized for server.");
                    
                    // Force spawn local player for LocalHome
                    if (ServerManager.CurrentServerType == ServerType.Local)
                    {
                        // Create a timer to spawn the local player after a short delay
                        var timer = new Timer
                        {
                            WaitTime = 0.5f,
                            OneShot = true,
                            Autostart = true
                        };
                        AddChild(timer);
                        timer.Timeout += () => {
                            // Check if player already exists
                            bool playerExists = false;
                            if (_multiplayerScene != null)
                            {
                                var existingPlayer = _multiplayerScene.GetPlayer(1);
                                playerExists = existingPlayer != null;
                                
                                if (playerExists)
                                {
                                    Logger.Log("LocalHome: Server player (ID 1) already exists, not spawning again");
                                }
                                else
                                {
                                    // Spawn a player for the server itself (ID 1)
                                    _multiplayerScene.SpawnPlayer(1);
                                    Logger.Log("LocalHome: Spawned player for server (ID 1)");
                                }
                            }
                            timer.QueueFree();
                        };
                    }
                }
                else
                {
                    Logger.Error("MultiplayerScene not found in the loaded world.");

                    // Setup listener for scene changes
                    GetTree().Root.ChildEnteredTree += OnChildEnteredTree;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing MultiplayerScene: {ex.Message}");
            }
        }

        // Helper method to recursively search for MultiplayerScene in children
        private MultiplayerScene FindMultiplayerSceneInChildren(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is MultiplayerScene scene)
                {
                    return scene;
                }

                var result = FindMultiplayerSceneInChildren(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void OnSceneChanged()
        {
            try
            {
                var multiplayerScene = GetTree().CurrentScene.GetNodeOrNull<MultiplayerScene>("MultiplayerScene");
                if (multiplayerScene == null)
                {
                    multiplayerScene = FindMultiplayerSceneInChildren(GetTree().CurrentScene);
                }

                if (multiplayerScene != null)
                {
                    _multiplayerScene = multiplayerScene;
                    multiplayerScene.InitializeForServer();
                    Logger.Log("MultiplayerScene initialized for server after scene change.");
                }
                else
                {
                    Logger.Error("MultiplayerScene not found in the loaded world after scene change.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OnSceneChanged: {ex.Message}");
            }
        }

        private void OnChildEnteredTree(Node node)
        {
            if (node is MultiplayerScene multiplayerScene)
            {
                _multiplayerScene = multiplayerScene;
                multiplayerScene.InitializeForServer();
                Logger.Log("MultiplayerScene initialized through ChildEnteredTree event.");
                GetTree().Root.ChildEnteredTree -= OnChildEnteredTree;
            }
        }

        // Call this method when switching worlds at runtime
        public void OnWorldLoaded(Node worldNode)
        {
            try
            {
                var multiplayerScene = worldNode.GetNodeOrNull<MultiplayerScene>("MultiplayerScene");
                if (multiplayerScene == null)
                {
                    multiplayerScene = FindMultiplayerSceneInChildren(worldNode);
                }

                if (multiplayerScene != null)
                {
                    _multiplayerScene = multiplayerScene;
                    multiplayerScene.InitializeForServer();
                    Logger.Log("MultiplayerScene initialized in newly loaded world.");
                }
                else
                {
                    Logger.Error("MultiplayerScene not found in newly loaded world.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OnWorldLoaded: {ex.Message}");
            }
        }
        
        // Helper method to find a node of a specific type in the scene tree
        private T FindNodeByType<T>(Node root) where T : class
        {
            // Check if the current node is of the desired type
            if (root is T result)
            {
                return result;
            }
            
            // Recursively search through all children
            foreach (var child in root.GetChildren())
            {
                var found = FindNodeByType<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }
    }
}
