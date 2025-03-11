using Aquamarine.Source.Logging;
using Godot;
using System;

namespace Aquamarine.Source.Management
{
    public partial class ServerManager
    {
        private void OnPeerConnected(long id)
        {
            Logger.Log($"Peer connected with ID: {id}. Attempting to spawn player...");
            try
            {
                // Spawn the player using our updated spawning system
                if (_multiplayerScene == null)
                {
                    Logger.Error($"MultiplayerScene is null during OnPeerConnected for ID {id}");
                    return;
                }

                // Spawn the player
                _multiplayerScene.SpawnPlayer((int)id);
                Logger.Log($"Player spawned successfully for ID: {id}.");

                // Add to player list
                _multiplayerScene.PlayerList.Add((int)id, new PlayerInfo());
                Logger.Log("Player added to the player list.");

                // Notify others of the new player
                _multiplayerScene.SendUpdatedPlayerList();
                Logger.Log("Updated player list sent.");

                // Send prefabs to the new player
                _multiplayerScene.SendAllPrefabs((int)id);
                Logger.Log("Prefabs sent to the new player.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during OnPeerConnected for ID {id}: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        private void OnPeerDisconnected(long id)
        {
            try
            {
                var idInt = (int)id;

                if (_multiplayerScene == null)
                {
                    Logger.Error($"MultiplayerScene is null during OnPeerDisconnected for ID {id}");
                    return;
                }

                // Remove the player
                _multiplayerScene.RemovePlayer(idInt);
                Logger.Log($"Player with ID {id} removed from scene.");

                // Remove from player list
                if (_multiplayerScene.PlayerList.Remove(idInt))
                {
                    Logger.Log($"Player with ID {id} removed from player list.");
                }
                else
                {
                    Logger.Warn($"Player with ID {id} not found in player list.");
                }

                // Update player list for remaining players
                _multiplayerScene.SendUpdatedPlayerList();
                Logger.Log("Updated player list sent after disconnection.");

                Logger.Log($"Peer disconnected with ID: {id}. Cleanup complete.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling peer disconnection (ID: {id}): {ex.Message}\nStack trace: {ex.StackTrace}");
            }

            if (CurrentServerType is ServerType.Local)
            {
                Logger.Log("Quitting local server");
                MultiplayerPeer?.Close();
                GetTree().Quit();
            }
        }
    }
}