using System;
using Godot;
using System.Threading.Tasks;
using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Networking;
using Bones.Core;
using System.Net.NetworkInformation;
using System.Linq;
using System.Threading;
using Aquamarine.Source.Helpers;

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
        private int _localhomePort = 6000;
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
                LoadLocalScene();
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
        //this should only ever need to be called once
        private void SpawnLocalHome()
        {
            // Check if we're already connecting to a local home server
            if (_connectingToLocalHome)
            {
                Logger.Log("Already connecting to local home server, not starting another connection");
                return;
            }
            if (_localHomePid != 0)
            {
                JoinLocalHome();
                return;
            };
            _connectingToLocalHome = true;
            // find a free port
            _localhomePort = Helpers.SimpleIpHelpers.GetAvailablePortUdp(10) ?? _localhomePort;
            // Start a new local home server
            _localHomePid = OS.CreateProcess(OS.GetExecutablePath(), ["--run-home-server", "--xr-mode", "off", "--headless","--port",_localhomePort.ToString()]);
            Logger.Log($"Started local server process with PID: {_localHomePid}");
            Task.Run(async () => {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000);
                    if(IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endp => endp.Port == _localhomePort))
                    {
                        Logger.Log("Local server is running, attempting to connect");
                        this.RunOnNodeAsync(JoinLocalHome);
                        _connectingToLocalHome = false;
                        break;
                    }
                    Logger.Log("Waiting for local server");
                }
            },_cancellationTokenSource.Token);
        }

        public void LoadLocalScene()
        {
            WorldManager.Instance?.LoadWorld("res://Scenes/World/LocalHome.tscn");
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
        public override void _ExitTree()
        {
            base._ExitTree();
            if (_localHomePid != 0)
            {
                OS.Kill(_localHomePid);
                _localHomePid = 0;
            }
            _cancellationTokenSource.Cancel();
        }
        public void JoinLocalHome()
        {
            LoadLocalScene();
            JoinServer("localhost", _localhomePort);
        }
    }
}
