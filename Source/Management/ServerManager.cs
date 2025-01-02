using System;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Networking;
using Godot;

namespace Aquamarine.Source.Management
{
	public partial class ServerManager : Node
	{
		[Export] private MultiplayerScene _multiplayerScene;

		private const int Port = 7000;
		private const int MaxConnections = 20;
		private const string SessionApiUrl = "https://api.xlinka.com/sessions";

		public override void _Ready()
		{
			try
			{
				var peer = new LiteNetLibMultiplayerPeer();
				peer.CreateServer(Port, MaxConnections);
				Multiplayer.MultiplayerPeer = peer;

				peer.PeerConnected += OnPeerConnected;
				peer.PeerDisconnected += OnPeerDisconnected;

				Logger.Log($"Server started on port {Port} with a maximum of {MaxConnections} connections.");

				// Advertise session to the API
				AdvertiseSession();
			}
			catch (Exception ex)
			{
				Logger.Error($"Error initializing ServerManager: {ex.Message}");
			}
		}

		private async void AdvertiseSession()
		{
			try
			{
				using var client = new System.Net.Http.HttpClient();
				var session = new SessionInfo
				{
					WorldName = "My World",
					IP = GetPublicIPAddress(),
					Port = Port
				};

				var content = new StringContent(JsonSerializer.Serialize(session), Encoding.UTF8, "application/json");
				var response = await client.PostAsync(SessionApiUrl, content);

				if (response.IsSuccessStatusCode)
				{
					Logger.Log("Session advertised successfully.");
				}
				else
				{
					Logger.Error($"Failed to advertise session: {response.ReasonPhrase}");
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"Error advertising session: {ex.Message}");
			}
		}

		private string GetPublicIPAddress()
		{
			try
			{
				using var client = new System.Net.Http.HttpClient();
				return client.GetStringAsync("https://api64.ipify.org").Result.Trim();
			}
			catch (Exception ex)
			{
				Logger.Error($"Error retrieving public IP: {ex.Message}");
				return "127.0.0.1"; // Fallback to localhost
			}
		}

		private void OnPeerConnected(long id)
		{
			Logger.Log($"Peer connected with ID: {id}. Spawning player...");
			_multiplayerScene.SpawnPlayer((int)id);
			_multiplayerScene.PlayerList.Add((int)id);
			_multiplayerScene.SendUpdatedPlayerList();
			_multiplayerScene.SendAllPrefabs((int)id);
		}


		private void OnPeerDisconnected(long id)
		{
			try
			{
				var idInt = (int)id;
				_multiplayerScene.RemovePlayer(idInt);
				_multiplayerScene.PlayerList.Remove(idInt);
				_multiplayerScene.SendUpdatedPlayerList();
				Logger.Log($"Peer disconnected with ID: {id}.");
			}
			catch (Exception ex)
			{
				Logger.Error($"Error handling peer disconnection (ID: {id}): {ex.Message}");
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
