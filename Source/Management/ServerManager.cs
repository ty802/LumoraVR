using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Networking;
using Bones.Core;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

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

        public override void _Process(double delta)
        {
            base._Process(delta);
            SessionListManager.PollEvents();
        }

        public override void _Ready()
		{
			try
			{
                MultiplayerPeer = new LiteNetLibMultiplayerPeer();
                MultiplayerPeer.CreateServerNat(Port, MaxConnections);
				Multiplayer.MultiplayerPeer = MultiplayerPeer;

                MultiplayerPeer.PeerConnected += OnPeerConnected;
                MultiplayerPeer.PeerDisconnected += OnPeerDisconnected;

				Logger.Log($"Server started on port {Port} with a maximum of {MaxConnections} connections.");

                //if we are a local server for the user home, don't advertise
                if (Advertise)
                {
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
                //if we somehow disconnect, try to reconnect to the session server
                ConnectToSessionServer();
            }
        }
        private void ConnectToSessionServer()
        {
            GD.Print("Attempting to connect to session server...");
            MainServer = SessionListManager.Connect(SessionInfo.SessionServer, "Private");
            
            this.CreateTimer(10, () =>
            {
                GD.Print($"{MainServer.ConnectionState}");
            });
        }
        private void SessionListListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliverymethod)
        {
            var opcode = reader.GetByte();
            
            GD.Print($"Recieved session list message, opcode: {opcode}");
            
            switch (opcode)
            {
                case 0x01:
                {
                    _sessionSecret = reader.GetString();

                    GD.Print($"Set session secret to {_sessionSecret}");
                    
                    var writer = new NetDataWriter();
                    writer.Put((byte)0x01);
                    writer.Put("My World");
                    MainServer.Send(writer, DeliveryMethod.ReliableOrdered);
                    break;
                }
                case 0x02:
                {
                    GD.Print("Sending NAT punchthrough message");
                    MultiplayerPeer.ServerSendNatPunchthrough(SessionInfo.SessionServer.Address.ToString(), SessionInfo.SessionServer.Port, $"server:{_sessionSecret}");
                    //SessionListManager.NatPunchModule.SendNatIntroduceRequest(SessionInfo.SessionServer, $"server:{_sessionSecret}");
                    break;
                }
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
	}
}
