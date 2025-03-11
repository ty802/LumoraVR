using Aquamarine.Source.Logging;
using Aquamarine.Source.Scene.RootObjects;
using Godot;
using System;

namespace Aquamarine.Source.Management
{
    public partial class ServerManager
    {
        private void OnPeerConnected(long id)
        {
            Logger.Log($"Peer connected with ID: {id}. Attempting to spawn player...");
            if (PlayerManager.Instance.PlayerRoot == null)
            {
                Logger.Error("PlayerRoot is null during peer connection.");
                return;
            }
            if (_multiplayerScene == null)
            {
                Logger.Error("_multiplayerScene is null in OnPeerConnected.");
                return;
            }
            try
            {
                Logger.Log($"Calling SpawnPlayer for ID: {id}");
                _multiplayerScene.SpawnPlayer((int)id);
                Logger.Log($"Player spawned successfully for ID: {id}.");

                _multiplayerScene.PlayerList.Add((int)id, new PlayerInfo());
                Logger.Log("Player added to the player list.");

                _multiplayerScene.SendUpdatedPlayerList();
                Logger.Log("Updated player list sent.");

                _multiplayerScene.SendAllPrefabs((int)id);
                Logger.Log("Prefabs sent to the new player.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during OnPeerConnected for ID {id}: {ex.Message}");
            }
        }

        private void OnPeerDisconnected(long id)
        {
            if (_multiplayerScene == null)
            {
                Logger.Error("_multiplayerScene is null in OnPeerDisconnected.");
                return;
            }
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

            if (CurrentServerType is ServerType.Local)
            {
                Logger.Log("Quitting local server");
                MultiplayerPeer.Close();
                GetTree().Quit();
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
