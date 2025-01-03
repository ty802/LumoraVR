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
		private const int MaxNatRetries = 3;

		private bool _isDirectConnection = false;
		private bool _natPunchthroughCompleted = false;
		private int _natRetryCount = 0;
		private string _lastUsedToken;
		private bool _natPunchthroughInProgress = false; // To prevent duplicate attempts

		public override void _Ready()
		{
			try
			{
				InitializeInput();

				// Delay and attempt to join a server
				this.CreateTimer(1, async () =>
				{
					var info = await FetchServerInfo();

					this.CreateTimer(1, () =>
					{
						if (info != null && !string.IsNullOrEmpty(info.SessionIdentifier))
						{
							GD.Print($"Fetched session identifier: {info.SessionIdentifier}");
							JoinNatServer(info.SessionIdentifier);
						}
						else
						{
							GD.Print("Failed to fetch a valid session identifier in time.");
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
			if (string.IsNullOrEmpty(identifier))
			{
				Logger.Error("Session identifier is null or empty. Cannot join NAT server.");
				return;
			}

			if (_natPunchthroughCompleted)
			{
				GD.Print("NAT punchthrough already completed. Skipping.");
				return;
			}

			if (_natPunchthroughInProgress)
			{
				GD.Print("NAT punchthrough already in progress. Skipping.");
				return;
			}

			_natPunchthroughInProgress = true; // Mark as in progress
			var token = $"client:{identifier}";
			GD.Print($"Attempting NAT punchthrough with token: {token}");

			_peer = new LiteNetLibMultiplayerPeer();
			_peer.ClientConnectionSuccess += OnNatPunchthroughSuccess;
			_peer.ClientConnectionFail += OnNatPunchthroughFail;
			_peer.CreateClientNat(SessionInfo.SessionServer.Address.ToString(), SessionInfo.SessionServer.Port, token);
			Multiplayer.MultiplayerPeer = _peer;
			_isDirectConnection = true;
		}

	


		private void OnNatPunchthroughSuccess()
		{
			_natPunchthroughCompleted = true;
			_peer.ClientConnectionSuccess -= OnNatPunchthroughSuccess;
			_peer.ClientConnectionFail -= OnNatPunchthroughFail;
			GD.Print("NAT punchthrough successful.");
			Logger.Log("Successfully connected to the NAT server.");
		}

		private void OnNatPunchthroughFail()
		{
			GD.Print("NAT punchthrough failed. Retrying...");
			Logger.Error($"NAT punchthrough attempt {_natRetryCount} failed.");
			JoinNatServer(_lastUsedToken.Replace("client:", "")); // Reuse the session identifier
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

				GD.Print("Got the session list");
				GD.Print(response);

				var sessions = JsonSerializer.Deserialize<List<SessionInfo>>(response, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
				});

				if (sessions != null && sessions.Any())
				{
					foreach (var session in sessions)
					{
						if (_natPunchthroughCompleted)
						{
							GD.Print("NAT punchthrough already completed. Ignoring session list updates.");
							return null; // Stop further processing
						}

						GD.Print($"Session: {session.Name}, IP: {session.IP}, Port: {session.Port}, Identifier: {session.SessionIdentifier}");

						if (string.IsNullOrEmpty(session.SessionIdentifier))
						{
							Logger.Error($"Session {session.Name} is missing an identifier. Skipping.");
							continue;
						}

						return session; // Return the first valid session
					}
				}

				Logger.Error("No valid sessions available in the API response.");
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
