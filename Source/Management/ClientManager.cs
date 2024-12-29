using System;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Input;
using Bones.Core;
using Godot;

namespace Aquamarine.Source.Management
{
    public partial class ClientManager : Node
    {
        private XRInterface _xrInterface;
        private VRInput _vrInput;
        [Export] private Node3D _inputRoot;
        [Export] private MultiplayerScene _multiplayerScene;

        private const int Port = 7000;
        private const string DefaultServerIP = "127.0.0.1"; // IPv4 localhost
        private const int MaxConnections = 20;

        public override void _Ready()
        {
            try
            {
                _xrInterface = XRServer.FindInterface("OpenXR");
                if (IsInstanceValid(_xrInterface) && _xrInterface.IsInitialized())
                {
                    DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
                    GetViewport().UseXR = true;

                    _vrInput = VRInput.PackedScene.Instantiate<VRInput>();
                    _inputRoot.AddChild(_vrInput);
                    Logger.Log("XR interface initialized successfully.");
                }
                else
                {
                    Logger.Warn("XR interface is not valid or not initialized.");
                }

                this.CreateTimer(3, JoinServer);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing ClientManager: {ex.Message}");
            }
        }

        public void JoinServer()
        {
            try
            {
                var peer = new ENetMultiplayerPeer();
                peer.CreateClient(DefaultServerIP, Port);
                Multiplayer.MultiplayerPeer = peer;

                Logger.Log($"Client joined server at {DefaultServerIP}:{Port}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error joining server: {ex.Message}");
            }
        }
    }
}
