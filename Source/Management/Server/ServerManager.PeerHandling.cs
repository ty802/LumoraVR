using System;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Scene.RootObjects;
using Godot;

namespace Aquamarine.Source.Management
{
    public partial class ServerManager
    {
        private void OnPeerConnected(long id)
        {
            try
            {
                Logger.Log($"Server: Peer connected: {id}");

                // Spawn player for the connected peer
                if (_multiplayerScene != null)
                {
                    Logger.Log($"Server: Spawning player for peer {id}");
                    _multiplayerScene.SpawnPlayer((int)id);
                }
                else
                {
                    Logger.Error($"Server: Cannot spawn player for peer {id}: MultiplayerScene is null");
                    
                    // Try to find MultiplayerScene again
                    var multiplayerScene = FindMultiplayerSceneInChildren(GetTree().CurrentScene);
                    if (multiplayerScene != null)
                    {
                        _multiplayerScene = multiplayerScene;
                        Logger.Log($"Server: Found MultiplayerScene, spawning player for peer {id}");
                        _multiplayerScene.SpawnPlayer((int)id);
                    }
                    else
                    {
                        Logger.Error("Server: MultiplayerScene still not found, trying direct player spawning");
                        
                        // Try direct player spawning as a last resort
                        if (PlayerManager.Instance != null)
                        {
                            Logger.Log($"Server: Attempting to spawn player via PlayerManager for peer {id}");
                            PlayerManager.Instance.SpawnPlayer((int)id, Vector3.Zero);
                        }
                        else
                        {
                            // Try to find PlayerRoot directly
                            var playerRoot = GetTree().Root.GetNodeOrNull<Node3D>("//PlayerRoot");
                            if (playerRoot != null)
                            {
                                Logger.Log($"Server: Found PlayerRoot directly, spawning player for peer {id}");
                                
                                // Instantiate player directly
                                var playerScene = ResourceLoader.Load<PackedScene>("res://Scenes/Objects/RootObjects/PlayerCharacterController.tscn");
                                if (playerScene != null)
                                {
                                    var player = playerScene.Instantiate<PlayerCharacterController>();
                                    player.SetPlayerAuthority((int)id);
                                    player.Name = id.ToString();
                                    playerRoot.AddChild(player);
                                    Logger.Log($"Server: Player spawned directly for peer {id}");
                                }
                                else
                                {
                                    Logger.Error("Server: Could not load player scene");
                                }
                            }
                            else
                            {
                                Logger.Error("Server: Could not find PlayerRoot, cannot spawn player");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Server: Error in OnPeerConnected: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        private void OnPeerDisconnected(long id)
        {
            try
            {
                Logger.Log($"Server: Peer disconnected: {id}");

                // Remove player for the disconnected peer
                if (_multiplayerScene != null)
                {
                    Logger.Log($"Server: Removing player for peer {id}");
                    _multiplayerScene.RemovePlayer((int)id);
                }
                else
                {
                    Logger.Error($"Server: Cannot remove player for peer {id}: MultiplayerScene is null");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Server: Error in OnPeerDisconnected: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }
    }
}
