using System.Linq;
using Aquamarine.Source.Scene.ObjectTypes;
using Godot;

namespace Aquamarine.Source.Management;

public partial class ServerManager : Node
{
    [Export] private MultiplayerScene _multiplayerScene;

    private const int Port = 7000;
    private const string DefaultServerIP = "127.0.0.1"; // IPv4 localhost
    private const int MaxConnections = 20;
    
    public override void _Ready()
    {
        base._Ready();
        
        var peer = new ENetMultiplayerPeer();
        
        peer.CreateServer(Port, MaxConnections);

        /*_multiplayerScene.*/Multiplayer.MultiplayerPeer = peer;
        
        peer.PeerConnected += OnPeerConnected;
        peer.PeerDisconnected += OnPeerDisconnected;
    }
    private void OnPeerConnected(long id)
    {
        _multiplayerScene.SpawnPlayer((int)id);
    }
    private void OnPeerDisconnected(long id)
    {
        _multiplayerScene.RemovePlayer((int)id);
    }
}
