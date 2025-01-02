using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Aquamarine.Source.Networking;

public partial class LiteNetLibMultiplayerPeer : MultiplayerPeerExtension
{
	//LIMITATIONS
	// 1. There are only 64 channels total, but only 63 are usable (0-62), the last channel is reserved as a control channel, godot normally supports the full Int32 range iirc.
	//    We shouldn't need to worry about it for this project, but it's something to keep in mind.
	private class Packet
	{
		public NetPeer Peer;
		public byte[] Data;
		public byte Channel;
		public DeliveryMethod Method;

		public Packet(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
		{
			Peer = peer;
			Data = reader.GetRemainingBytes();
			Channel = channel;
			Method = method;
		}
	}
	
	public const string RoomKey = "Aquamarine"; //TODO
	public const byte ControlChannel = 0b00111111;

	public const byte ControlSetLocalID = 0x01;
	
	public readonly NetManager NetManager;
	public readonly EventBasedNetListener Listener;

	private readonly ConcurrentQueue<Packet> _packetQueue = new();
	
	public int MaxClients { get; private set; } = 32;
	private readonly Dictionary<NetPeer, int> _peerToId = new();
	private Dictionary<int, NetPeer> _idToPeer = new();

	private int _localNetworkId = -1;

	private Packet _currentPacket;
	
	private NetPeer _currentPeer;
	private int _currentPeerId;
	private byte _currentChannel;
	private DeliveryMethod _currentMethod;

	private bool IsServer => _localNetworkId == 1;

	private ConnectionStatus _clientConnectionStatus;

	private bool _refuseNewConnections;

	public LiteNetLibMultiplayerPeer()
	{
		Listener = new EventBasedNetListener();
		NetManager = new NetManager(Listener)
		{
			ChannelsCount = 64,
			IPv6Enabled = true,
			AutoRecycle = true,
		};

		Listener.NetworkReceiveEvent += ListenerOnNetworkReceiveEvent;
		Listener.PeerConnectedEvent += ListenerOnPeerConnectedEvent;
		Listener.PeerDisconnectedEvent += ListenerOnPeerDisconnectedEvent;
		Listener.ConnectionRequestEvent += ListenerOnConnectionRequestEvent;
	}
	private void UpdateIdToPeer()
	{
		_idToPeer = _peerToId.ToDictionary(i => i.Value, i => i.Key);
	}
	private void ListenerOnConnectionRequestEvent(ConnectionRequest request)
	{
		if (IsServer)
		{
			if (NetManager.ConnectedPeersCount >= MaxClients)
			{
				//GD.Print("Rejecting, full lobby");
				request.Reject("Lobby Full"u8.ToArray());
				return;
			}
			if (_refuseNewConnections)
			{
				request.Reject("Refusing new connections"u8.ToArray());
				return;
			}
			
			var usedIds = _idToPeer.Select(i => i.Key).ToArray();
			var unusedId = Random.Shared.Next(2, int.MaxValue);
			while (usedIds.Contains(unusedId)) unusedId = Random.Shared.Next(2, int.MaxValue);
			
			//GD.Print($"Client {unusedId} connected to server");
			
			var writer = new NetDataWriter();
			writer.Put(ControlSetLocalID);
			writer.Put(unusedId);
			
			var peer = request.Accept();
			peer.Send(writer, ControlChannel, DeliveryMethod.ReliableOrdered);
			
			_peerToId.Add(peer, unusedId);
			UpdateIdToPeer();
			EmitSignal(MultiplayerPeer.SignalName.PeerConnected, unusedId);
		}
		else
		{
			request.Reject("Not a server"u8.ToArray());
		}
	}
	private void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectinfo)
	{
		if (IsServer)
		{
			if (_peerToId.TryGetValue(peer, out var peerId))
			{
				if (disconnectinfo.Reason is not DisconnectReason.DisconnectPeerCalled)
				{
					EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, peerId);
				}

				_peerToId.Remove(peer);
				UpdateIdToPeer();
			}
		}
		else
		{
			_peerToId.Clear();
			_idToPeer.Clear();
			_clientConnectionStatus = ConnectionStatus.Disconnected;
			NetManager.Stop();
			_localNetworkId = -1;
			_packetQueue.Clear();

			if (_peerToId.TryGetValue(peer, out var peerId))
			{
				EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, peerId);
			}
		}
	}

	private void ListenerOnPeerConnectedEvent(NetPeer peer)
	{
		if (IsServer)
		{
			
		}
		else
		{
			//GD.Print("Connected to server");
			//our only peer is the server
			_peerToId.Add(peer, 1);
			UpdateIdToPeer();
		}
	}
	private void ListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
	{
		//GD.Print($"Received packet of length {reader.AvailableBytes} on channel {channel}");
		if (channel == ControlChannel)
		{
			if (IsServer)
			{
				//GD.PrintErr("Received control packet, but I'm a server");
				return;
			}
			try
			{
				//GD.Print($"Got special control packet");
				var controlCode = reader.GetByte();
				switch (controlCode)
				{
					case ControlSetLocalID:
					{
						if (_localNetworkId == -1)
						{
							GD.Print("Finished connecting");
							_localNetworkId = reader.GetInt();
							_clientConnectionStatus = ConnectionStatus.Connected;
							EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1);
						}
						return;
					}
				}
				return;
			}
			catch
			{
				return;
			}
		}
		_packetQueue.Enqueue(new Packet(peer, reader, channel, deliveryMethod));
	}

	public Error CreateServer(int port, int maxClients = 32, bool natPunch = true)
	{
		if (NetManager.IsRunning) return Error.AlreadyInUse;
		
		//GD.Print($"Creating server on port {port}");

		MaxClients = maxClients;
		NetManager.NatPunchEnabled = natPunch;
		//NetManager.AllowPeerAddressChange = false;
		_localNetworkId = 1;
		
		NetManager.Start(port);
		
		return Error.Ok;
	}

	public Error CreateClient(string address, int port, bool natPunch = true)
	{
		if (NetManager.IsRunning) return Error.AlreadyInUse;
		
		//GD.Print($"Connecting to server {address} on port {port}");

		NetManager.NatPunchEnabled = natPunch;
		
		NetManager.Start();
		NetManager.Connect(address, port, RoomKey);

		_clientConnectionStatus = ConnectionStatus.Connecting;

		return Error.Ok;
	}
	
	public override void _Close()
	{
		if (!NetManager.IsRunning) return;
		NetManager.Stop();
		_localNetworkId = -1;
	}
	public override void _DisconnectPeer(int pPeer, bool pForce)
	{
		_idToPeer[pPeer].Disconnect();
	}
	public override int _GetAvailablePacketCount() => IsServer || _clientConnectionStatus == ConnectionStatus.Connected ? _packetQueue.Count : 0;
	public override ConnectionStatus _GetConnectionStatus() => IsServer ? ConnectionStatus.Connected : _clientConnectionStatus;
	public override int _GetMaxPacketSize() => NetConstants.MaxPacketSize;

	public override int _GetPacketChannel() => _packetQueue.TryPeek(out var packet) ? packet.Channel : 0;
	public override TransferModeEnum _GetPacketMode() => (_packetQueue.TryPeek(out var packet) ? packet.Method : DeliveryMethod.Unreliable) switch
	{
		DeliveryMethod.Unreliable => TransferModeEnum.Unreliable,
		DeliveryMethod.Sequenced => TransferModeEnum.UnreliableOrdered,
		DeliveryMethod.ReliableOrdered => TransferModeEnum.Reliable,
		_ => TransferModeEnum.Unreliable,
	};
	public override int _GetPacketPeer()
	{
		if (_packetQueue.TryPeek(out var packet)) return _peerToId[packet.Peer];
		return -1;
	}
	public override byte[] _GetPacketScript()
	{
		_packetQueue.TryDequeue(out _currentPacket);
		if (_currentPacket is not null) return _currentPacket.Data;
		
		//GD.PrintErr("CurrentPacket is null");
		return [];
	}
	public override int _GetTransferChannel() => _currentChannel;
	public override TransferModeEnum _GetTransferMode() => _currentMethod switch
	{
		DeliveryMethod.Unreliable => TransferModeEnum.Unreliable,
		DeliveryMethod.Sequenced => TransferModeEnum.UnreliableOrdered,
		DeliveryMethod.ReliableOrdered => TransferModeEnum.Reliable,
		_ => TransferModeEnum.Unreliable,
	};

	public override Error _PutPacketScript(byte[] pBuffer)
	{
		//GD.Print("Sending packet");
		try
		{
			if (_currentPeerId == 0)
			{
				NetManager.SendToAll(pBuffer, _currentChannel, _currentMethod);
			}
			else
			{
				_currentPeer.Send(pBuffer, _currentChannel, _currentMethod);
			}
		}
		catch
		{
			return Error.Failed;
		}
		return Error.Ok;
	}
	public override bool _IsServer() => IsServer;
	public override bool _IsServerRelaySupported() => true; //TODO: does this matter if we disable it?
	public override void _SetTargetPeer(int pPeer)
	{
		_currentPeer = _idToPeer.GetValueOrDefault(pPeer);
		_currentPeerId = pPeer;
	}
	public override void _SetTransferChannel(int pChannel)
	{
		_currentChannel = (byte)Math.Clamp(pChannel, 0, ControlChannel - 1);
		
	}
	public override void _SetTransferMode(TransferModeEnum pMode) =>
		_currentMethod = pMode switch
		{
			TransferModeEnum.Unreliable => DeliveryMethod.Unreliable,
			TransferModeEnum.UnreliableOrdered => DeliveryMethod.Sequenced,
			TransferModeEnum.Reliable => DeliveryMethod.ReliableOrdered,
			_ => DeliveryMethod.Unreliable,
		};

	public override int _GetUniqueId()
	{
		if (!NetManager.IsRunning || (!IsServer && _clientConnectionStatus != ConnectionStatus.Connected)) GD.PrintErr("Not Active");
		return _localNetworkId;
	}
	public override bool _IsRefusingNewConnections() => _refuseNewConnections;
	public override void _SetRefuseNewConnections(bool pEnable) => _refuseNewConnections = pEnable;
	public override void _Poll() => NetManager.PollEvents();
}
