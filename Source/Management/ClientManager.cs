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
				this.CreateTimer(2, JoinServer);
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

		public async void JoinServer()
		{
			try
			{
				Logger.Log("Fetching server details from the session API...");
				// Fetch the server details from the session API
				var serverInfo = await FetchServerInfo();

				if (serverInfo == null)
				{
					Logger.Error("No available servers to connect to.");
					return;
				}

				Logger.Log($"Server details fetched: WorldName={serverInfo.WorldName}, IP={serverInfo.IP}, Port={serverInfo.Port}");

				var peer = new LiteNetLibMultiplayerPeer();
                _peer = peer;
				try
				{
					Logger.Log("Attempting direct connection...");
					// Attempt direct connection
                    
					peer.CreateClient(serverInfo.IP, serverInfo.Port);
                    Multiplayer.MultiplayerPeer = peer;
					_isDirectConnection = true;

                    peer.ClientConnectionFail += RetryOnRelay;
                    peer.ClientConnectionSuccess += SuccessConnection;

                    //Logger.Log($"Direct connection established: Server={serverInfo.IP}:{serverInfo.Port}, World={serverInfo.WorldName}");
                }
				catch (Exception ex)
				{
					Logger.Warn($"Direct connection failed: {ex.Message}.");
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"Unexpected error while joining server: {ex.Message}");
			}
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
				using var client = new System.Net.Http.HttpClient();
				var response = await client.GetStringAsync(RelayApiUrl);

				// Deserialize the JSON response
				var sessions = JsonSerializer.Deserialize<List<SessionInfo>>(response, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true // Allow case-insensitive deserialization
				});

				if (sessions != null && sessions.Any())
				{
					Logger.Log($"Fetched {sessions.Count} sessions. First session: {sessions[0].WorldName}, IP: {sessions[0].IP}, Port: {sessions[0].Port}");
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


		private class SessionInfo
		{
			public string WorldName { get; set; }
			public string IP { get; set; }
			public int Port { get; set; }
		}
	}
}
