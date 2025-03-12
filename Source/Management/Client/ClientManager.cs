using System;
using Godot;
using System.Threading.Tasks;
using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Networking;
using Bones.Core;
using System.Threading;

namespace Aquamarine.Source.Management
{
    public partial class ClientManager : Node
    {
        public static ClientManager Instance;
        public static bool ShowDebug = true;

        private XRInterface _xrInterface;
        private IInputProvider _input;
        private LiteNetLibMultiplayerPeer  _peer;
        [Export] private Node3D _inputRoot;
        [Export] private MultiplayerScene _multiplayerScene;
        private string _targetWorldPath = null;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private int _localHomePid = 0;

        private bool _isDirectConnection = false;
        [Signal]
        public delegate bool OnConnectSucsessEventHandler();

        public override void _Ready()
        {
            Instance = this;

            try
            {
                InitializeLocalDatabase();
                InitializeLoginManager();
                InitializeInput();
                InitializeDiscordManager();
                FetchServerInfo();
                SpawnLocalHome();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing ClientManager: {ex.Message}");
            }
        }

        public override void _Input(InputEvent @event)
        {
            base._Input(@event);

            if (@event.IsActionPressed("ToggleDebug"))
            {
                ShowDebug = !ShowDebug;
            }
        }

        // Flag to track if we're already connecting to a local home server
        private bool _connectingToLocalHome = false;
        
        private void SpawnLocalHome()
        {
            // Check if we're already connecting to a local home server
            if (_connectingToLocalHome)
            {
                Logger.Log("Already connecting to local home server, not starting another connection");
                return;
            }

            _connectingToLocalHome = true;

            // Check if we already have a local home server running
            if (_localHomePid != 0)
            {
                Logger.Log($"Local home server already running with PID: {_localHomePid}, not starting another one");
                throw new Exception("wtf");
            }
            // find a free port
            int port = Helpers.SimpleIpHelpers.GetAvailablePortUdp(10) ?? 6000;
            // Start a new local home server
            _localHomePid = OS.CreateProcess(OS.GetExecutablePath(), ["--run-home-server", "--xr-mode", "off", "--headless","--port",port.ToString()]);
            Logger.Log($"Started local server process with PID: {_localHomePid}");
            Task.Run(async () => {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000);
                    if(IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endp => endp.Port == port))
                    {
                        Logger.Log("Local server is running, attempting to connect");
                        JoinServer("localhost", port);
                        break;
                    }
                    Logger.Log("Waiting for local server");
                }
            },_cancellationTokenSource.Token);
        }

        public void LoadLocalScene()
        {
            DisconnectFromCurrentServer();

            GetTree().ChangeSceneToFile("res://Scenes/World/LocalHome.tscn");
            Logger.Log("Switched to local scene.");
        }

        ~ClientManager()
        {
            if (_localHomePid != 0)
            {
                OS.Kill(_localHomePid);
                _localHomePid = 0;
            }
            _cancellationTokenSource.Cancel();
        }
        public override void _EnterTree()
        {
            base._EnterTree();
            if (_localHomePid != 0)
            {
                OS.Kill(_localHomePid);
                _localHomePid = 0;
            }
            _cancellationTokenSource.Cancel();
        }
    }
}
