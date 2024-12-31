using System;
using Aquamarine.Source.Logging;
using Godot;

namespace Aquamarine.Source.Management
{
    public partial class ServerManager : Node
    {
        [Export] private MultiplayerScene _multiplayerScene;

        private const int Port = 7000;
        private const int MaxConnections = 20;

        public override void _Ready()
        {
            try
            {
                var peer = new ENetMultiplayerPeer();
                peer.CreateServer(Port, MaxConnections);
                Multiplayer.MultiplayerPeer = peer;

                peer.PeerConnected += OnPeerConnected;
                peer.PeerDisconnected += OnPeerDisconnected;

                Logger.Log($"Server started on port {Port} with a maximum of {MaxConnections} connections.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing ServerManager: {ex.Message}");
            }
        }

        private void OnPeerConnected(long id)
        {
            try
            {
                var idInt = (int)id;
                _multiplayerScene.SpawnPlayer(idInt);
                _multiplayerScene.PlayerList.Add(idInt);
                _multiplayerScene.SendUpdatedPlayerList();
                _multiplayerScene.SendAllPrefabs(idInt);
                Logger.Log($"Peer connected with ID: {id}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling peer connection (ID: {id}): {ex.Message}");
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
    }
}
