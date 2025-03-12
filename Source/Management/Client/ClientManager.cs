using System;
using Godot;
using System.Threading.Tasks;
using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Networking;
using Bones.Core;

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

        private void SpawnLocalHome()
        {
            _localHomePid = OS.CreateProcess(OS.GetExecutablePath(), ["--run-home-server", "--xr-mode", "off", "--headless"]);
            Logger.Log($"Started local server process with PID: {_localHomePid}");

            this.CreateTimer(2.0f, () =>
            {
                Logger.Log("Attempting to connect to server at localhost:6000");
                JoinServer("localhost", 6000);
            });
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
            }
        }
    }
}
