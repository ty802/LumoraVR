using System;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Networking;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;
using Bones.Core;
using System.Threading.Tasks;

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

		public override void _Process(double delta)
		{
			base._Process(delta);
			SessionListManager.PollEvents();
		}

		public override async void _Ready()
		{
			try
			{
				MultiplayerPeer = new LiteNetLibMultiplayerPeer();
				MultiplayerPeer.CreateServerNat(Port, MaxConnections);
				Multiplayer.MultiplayerPeer = MultiplayerPeer;

				MultiplayerPeer.PeerConnected += OnPeerConnected;
				MultiplayerPeer.PeerDisconnected += OnPeerDisconnected;

				Logger.Log($"Server started on port {Port} with a maximum of {MaxConnections} connections.");

				if (Advertise)
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
				Logger.Error($"Error initializing ServerManager: {ex.Message}");
			}
		}

		private void SessionListListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectinfo)
		{
			GD.Print($"Disconnected from the session server: {disconnectinfo.Reason}");
			if (_running)
			{
				ConnectToSessionServer();
			}
		}

		private void ConnectToSessionServer()
		{
			GD.Print("Attempting to connect to session server...");
			MainServer = SessionListManager.Connect(SessionInfo.SessionServer, "Private");

            /*
			this.CreateTimer(10, () =>
			{
				GD.Print($"{MainServer.ConnectionState}");
			});
			*/
		}

		private bool _natPunchthroughCompleted = false;

		private void SessionListListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliverymethod)
		{
			var opcode = reader.GetByte();

			GD.Print($"Received session list message, opcode: {opcode}");

			switch (opcode)
			{
				case 0x01:
					_sessionSecret = reader.GetString();
					GD.Print($"Set session secret to {_sessionSecret}");
					var writer = new NetDataWriter();
					writer.Put((byte)0x01);
					writer.Put(_worldName);
					MainServer.Send(writer, DeliveryMethod.ReliableOrdered);
					break;

				case 0x02:
					if (_natPunchthroughCompleted)
					{
						GD.Print("NAT punchthrough already completed. Skipping.");
						return;
					}

					GD.Print("Sending NAT punchthrough message");
					MultiplayerPeer.ServerSendNatPunchthrough(
						SessionInfo.SessionServer.Address.ToString(),
						SessionInfo.SessionServer.Port,
						$"server:{_sessionSecret}"
					);
					_natPunchthroughCompleted = true;
					break;
			}
		}


		private void OnPeerConnected(long id)
		{
			Logger.Log($"Peer connected with ID: {id}. Attempting to spawn player...");

			try
			{
				_multiplayerScene.SpawnPlayer((int)id); // Spawn the player
				//Logger.Log($"Player spawned successfully for ID: {id}.");

				_multiplayerScene.PlayerList.Add((int)id); // Add to player list
				//Logger.Log("Player added to the player list.");

				_multiplayerScene.SendUpdatedPlayerList(); // Notify others of the new player
				//Logger.Log("Updated player list sent.");

				_multiplayerScene.SendAllPrefabs((int)id); // Send prefabs to the new player
				//Logger.Log("Prefabs sent to the new player.");
			}
			catch (Exception ex)
			{
				Logger.Error($"Error during OnPeerConnected for ID {id}: {ex.Message}");
			}
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
/*
		private async Task<string> GetPublicIP()
		{
			try
			{
				using var client = new System.Net.Http.HttpClient();
				var publicIp = await client.GetStringAsync("https://api.ipify.org");
				Logger.Log($"Retrieved public IP: {publicIp}");
				return publicIp.Trim();
			}
			catch (Exception ex)
			{
				Logger.Error($"Error fetching public IP: {ex.Message}");
				return null;
			}
		}
*/
	}
}
