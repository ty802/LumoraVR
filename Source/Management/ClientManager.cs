using System;
using System.Net.Http;
using System.Text.Json;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Input;
using Aquamarine.Source.Networking;
using Bones.Core;
using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace Aquamarine.Source.Management
{
	public partial class ClientManager : Node
	{
		private XRInterface _xrInterface;
		private IInputProvider _input;
        private LiteNetLibMultiplayerPeer _peer;
		[Export] private Node3D _inputRoot;
		[Export] private MultiplayerScene _multiplayerScene;

		private const int Port = 7000; // Default port for LiteNetLib
		private const string RelayApiUrl = "https://api.xlinka.com/sessions";

		private bool _isDirectConnection = false;

		public override void _Ready()
		{
			try
			{
				InitializeInput();

				// Delay and attempt to join a server
				this.CreateTimer(1, () =>
                {
                    var info = FetchServerInfo();
                    
                    this.CreateTimer(1, () =>
                    {
                        if (info.IsCompleted)
                        {
                            JoinNatServer(info.Result.SessionIdentifier);
                        }
                        else
                        {
                            GD.Print("went too slowly");
                        }
                    });
                });
			}
			catch (Exception ex)
			{
				Logger.Error($"Error initializing ClientManager: {ex.Message}");
			}
		}

		private void InitializeInput()
		{
			_xrInterface = XRServer.FindInterface("OpenXR");

			if (IsInstanceValid(_xrInterface) && _xrInterface.IsInitialized())
			{
				DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
				GetViewport().UseXR = true;

				var vrInput = VRInput.PackedScene.Instantiate<VRInput>();
				_input = vrInput;
				_inputRoot.AddChild(vrInput);
				Logger.Log("XR interface initialized successfully.");
			}
			else
			{
				var desktopInput = DesktopInput.PackedScene.Instantiate<DesktopInput>();
				_input = desktopInput;
				_inputRoot.AddChild(desktopInput);
				Logger.Log("Desktop interface initialized successfully.");
			}
		}

        public void JoinNatServer(string identifier)
        {
            _peer = new LiteNetLibMultiplayerPeer();

            _peer.CreateClientNat(SessionInfo.SessionServer.Address.ToString(), SessionInfo.SessionServer.Port, $"client:{identifier}");
            Multiplayer.MultiplayerPeer = _peer;
            _isDirectConnection = true;
        }

		public void JoinServer(string address, int port)
		{
            _peer = new LiteNetLibMultiplayerPeer();
            
            _peer.CreateClient(address, port);
            Multiplayer.MultiplayerPeer = _peer;
            _isDirectConnection = true;
		}
        

        private void SuccessConnection()
        {
            _peer.ClientConnectionFail -= RetryOnRelay;
            _peer.ClientConnectionSuccess -= SuccessConnection;
        }

        private void RetryOnRelay()
        {
            SuccessConnection();
            
            Logger.Log("Attempting relay connection...");
            
            _peer.CreateClient("relay.xlinka.com", 8000);
            Multiplayer.MultiplayerPeer = _peer;
            _isDirectConnection = false;
        }

		private async Task<SessionInfo> FetchServerInfo()
		{
			try
            {
                GD.Print("Trying to get session list");
                
				using var client = new System.Net.Http.HttpClient();
				var response = await client.GetStringAsync(SessionInfo.SessionList);
                
                GD.Print($"Got the session list");
                GD.Print(response);

				// Deserialize the JSON response
				var sessions = JsonSerializer.Deserialize<List<SessionInfo>>(response, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true, // Allow case-insensitive deserialization
				});

				if (sessions != null && sessions.Any())
				{
					//Logger.Log($"Fetched {sessions.Count} sessions. First session: {sessions[0].WorldName}, IP: {sessions[0].IP}, Port: {sessions[0].Port}");
					return sessions.FirstOrDefault(); // Get the first available session
				}

				Logger.Error("No sessions available in the API response.");
				return null;
			}
			catch (Exception ex)
			{
				Logger.Error($"Error fetching server info: {ex.Message}");
				return null;
			}
		}
	}
}
