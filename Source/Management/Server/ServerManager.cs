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
        [Export] private MultiplayerScene _multiplayerScene;

        public LiteNetLibMultiplayerPeer MultiplayerPeer;

        public NetManager SessionListManager;
        public EventBasedNetListener SessionListListener;
        public NetPeer MainServer;

        private string _sessionSecret;
        private bool _running = true;

        private const int Port = 7000;
        private const int SessionListPort = Port + 7001;
        private const int MaxConnections = 20;
        private const string SessionApiUrl = "https://api.xlinka.com/sessions";

        private string _publicIp;
        private string _worldName = "My World";
        private string _worldIdentifier = "placeholder";

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (CurrentServerType == ServerType.Local && MultiplayerPeer != null)
            {
                MultiplayerPeer.Poll(); // Poll network events manually
            }
            SessionListManager?.PollEvents();
        }

        public override void _Ready()
        {
            try
            {
                var serverType = CurrentServerType;

                // Initialize MultiplayerPeer
                MultiplayerPeer = new LiteNetLibMultiplayerPeer();
                Multiplayer.MultiplayerPeer = MultiplayerPeer;
                MultiplayerPeer.PeerConnected += OnPeerConnected;
                MultiplayerPeer.PeerDisconnected += OnPeerDisconnected;

                if (serverType == ServerType.Local)
                {
                    // Switch scene
                    GetTree().ChangeSceneToFile("res://Scenes/World/LocalHome.tscn");
                    Logger.Log("LocalHome Loaded.");

                    // Defer MultiplayerScene initialization until scene is fully loaded
                    CallDeferred(nameof(InitializeMultiplayerScene));

                    // Start local server
                    MultiplayerPeer.CreateServer(6000, 1);
                    Logger.Log("Local server started on port 6000.");
                }
                else if (serverType == ServerType.Standard)
                {
                    // Start standard server
                    MultiplayerPeer.CreateServerNat(Port, MaxConnections);
                    Logger.Log($"Server started on port {Port} with max connections {MaxConnections}.");

                    // Initialize session manager
                    SessionListListener = new EventBasedNetListener();
                    SessionListManager = new NetManager(SessionListListener)
                    {
                        IPv6Enabled = true,
                        PingInterval = 10000,
                    };
                    SessionListListener.NetworkReceiveEvent += SessionListListenerOnNetworkReceiveEvent;
                    SessionListListener.PeerDisconnectedEvent += SessionListListenerOnPeerDisconnectedEvent;
                    SessionListManager.Start(SessionListPort);
                    ConnectToSessionServer();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing ServerManager: {ex}");
            }

            // Verify PlayerRoot
            if (PlayerManager.Instance.PlayerRoot == null)
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
            var multiplayerScene = GetTree().CurrentScene.GetNode<MultiplayerScene>("MultiplayerScene");
            if (multiplayerScene != null)
            {
                multiplayerScene.InitializeForServer();
                Logger.Log("MultiplayerScene initialized for server.");
            }
            else
            {
                Logger.Error("MultiplayerScene not found in the loaded world.");
            }
        }
        private void OnSceneChanged()
        {
            var multiplayerScene = GetTree().CurrentScene.GetNode<MultiplayerScene>("MultiplayerScene");
            if (multiplayerScene != null)
            {
                multiplayerScene.InitializeForServer();
                Logger.Log("MultiplayerScene initialized for server.");
            }
            else
            {
                Logger.Error("MultiplayerScene not found in the loaded world.");
            }
        }

        private void OnChildEnteredTree(Node node)
        {
            if (node is MultiplayerScene multiplayerScene)
            {
                multiplayerScene.InitializeForServer();
                Logger.Log("MultiplayerScene initialized.");
                GetTree().Root.ChildEnteredTree -= OnChildEnteredTree;
            }
        }
    }
}
