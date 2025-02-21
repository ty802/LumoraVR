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
            SessionListManager?.PollEvents();
        }

        public override void _Ready()
        {
            try
            {
                var serverType = CurrentServerType;

                MultiplayerPeer = new LiteNetLibMultiplayerPeer();

                if (serverType is ServerType.Local)
                {
                    /*
                    var args = OS.GetCmdlineArgs().ToList();
                    var runHomeIndex = args.IndexOf("--run-home-server");

                    var port = int.Parse(args[runHomeIndex][2..]);
                    
                    Logger.Log(port.ToString());
                    */
                    MultiplayerPeer.CreateServer(6000, 1);
                    Logger.Log($"Local server started on port {6000}.");
                }
                else
                {
                    MultiplayerPeer.CreateServerNat(Port, MaxConnections);
                    Logger.Log($"Server started on port {Port} with a maximum of {MaxConnections} connections.");
                }

                Multiplayer.MultiplayerPeer = MultiplayerPeer;

                MultiplayerPeer.PeerConnected += OnPeerConnected;
                MultiplayerPeer.PeerDisconnected += OnPeerDisconnected;


                if (serverType is ServerType.Standard)
                {
                    // Initialize session list manager
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
        }
    }
}
