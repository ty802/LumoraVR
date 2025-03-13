using System;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Logging;
using Godot;

namespace Aquamarine.Source.Management.Client
{
    [GlobalClass]
    public partial class ClientConnectionHandler : Node
    {
        private MultiplayerScene _multiplayerScene;
        private CustomPlayerSync _playerSync;
        private bool _isConnected = false;

        public override void _Ready()
        {
            // Connect to network peer connected signal
            Multiplayer.PeerConnected += OnPeerConnected;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;
            
            // Find the MultiplayerScene
            _multiplayerScene = FindMultiplayerScene();
            
            // Find the CustomPlayerSync
            _playerSync = GetNodeOrNull<CustomPlayerSync>("%CustomPlayerSync");
            if (_playerSync == null)
            {
                _playerSync = GetNodeOrNull<CustomPlayerSync>("../CustomPlayerSync");
            }
            
            Logger.Log("ClientConnectionHandler initialized");
        }

        private bool _playerListRequested = false;
        
        private void OnPeerConnected(long id)
        {
            Logger.Log($"Client: Peer connected: {id}");
            
            // If this is the server connecting to us, we're now connected to the server
            if (id == 1 && !_isConnected)
            {
                _isConnected = true;
                
                // Reset the player list requested flag when connecting to a new server
                _playerListRequested = false;
                
                // Create a timer to request player list after a short delay
                // to ensure we're fully connected
                var timer = new Timer
                {
                    WaitTime = 1.0f,
                    OneShot = true,
                    Autostart = true
                };
                AddChild(timer);
                timer.Timeout += () => {
                    RequestPlayerList();
                    timer.QueueFree();
                };
            }
        }

        private void OnPeerDisconnected(long id)
        {
            Logger.Log($"Client: Peer disconnected: {id}");
            
            // If the server disconnected from us
            if (id == 1)
            {
                _isConnected = false;
                Logger.Log("Client: Disconnected from server");
            }
        }
        
        private void RequestPlayerList()
        {
            if (!_isConnected)
            {
                Logger.Log("Client: Cannot request player list - not connected to server");
                return;
            }
            
            // Check if we've already requested the player list
            if (_playerListRequested)
            {
                Logger.Log("Client: Player list already requested, not requesting again");
                return;
            }
            
            Logger.Log("Client: Requesting player list from server");
            
            // Try to find the MultiplayerScene again in case it was loaded after this component
            if (_multiplayerScene == null)
            {
                _multiplayerScene = FindMultiplayerScene();
            }
            
            // Request player list from the server
            if (_multiplayerScene != null)
            {
                try
                {
                    _multiplayerScene.Rpc(MultiplayerScene.MethodName.RequestPlayerList);
                    Logger.Log("Client: Player list request sent via MultiplayerScene");
                    _playerListRequested = true;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Client: Error requesting player list: {ex.Message}");
                    
                    // Try to find the MultiplayerScene directly in the scene tree
                    var scene = GetTree().Root.GetNodeOrNull("Root/WorldRoot/Scene");
                    if (scene is MultiplayerScene multiplayerScene)
                    {
                        _multiplayerScene = multiplayerScene;
                        Logger.Log($"Client: Found MultiplayerScene at Root/WorldRoot/Scene");
                        
                        try
                        {
                            _multiplayerScene.Rpc(MultiplayerScene.MethodName.RequestPlayerList);
                            Logger.Log("Client: Player list request sent via newly found MultiplayerScene");
                            _playerListRequested = true;
                        }
                        catch (Exception ex2)
                        {
                            Logger.Error($"Client: Error requesting player list from newly found MultiplayerScene: {ex2.Message}");
                        }
                    }
                }
            }
            else
            {
                Logger.Error("Client: Cannot request player list - MultiplayerScene not found");
                
                // Try one more time with direct path
                var scene = GetTree().Root.GetNodeOrNull("Root/WorldRoot/Scene");
                if (scene is MultiplayerScene multiplayerScene)
                {
                    _multiplayerScene = multiplayerScene;
                    Logger.Log($"Client: Found MultiplayerScene at Root/WorldRoot/Scene");
                    
                    try
                    {
                        _multiplayerScene.Rpc(MultiplayerScene.MethodName.RequestPlayerList);
                        Logger.Log("Client: Player list request sent via direct path MultiplayerScene");
                        _playerListRequested = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Client: Error requesting player list from direct path MultiplayerScene: {ex.Message}");
                    }
                }
            }
        }
        
        private MultiplayerScene FindMultiplayerScene()
        {
            // Try to find using MultiplayerScene.Instance first (most reliable)
            if (MultiplayerScene.Instance != null)
            {
                Logger.Log("ClientConnectionHandler: Found MultiplayerScene using MultiplayerScene.Instance");
                return MultiplayerScene.Instance;
            }
            
            // Try to find in the scene tree
            var multiplayerScene = GetNodeOrNull<MultiplayerScene>("%MultiplayerScene");
            if (multiplayerScene != null)
            {
                Logger.Log("ClientConnectionHandler: Found MultiplayerScene using %MultiplayerScene");
                return multiplayerScene;
            }
            
            // Search in the entire scene tree
            var root = GetTree().Root;
            multiplayerScene = FindNodeByType<MultiplayerScene>(root);
            if (multiplayerScene != null)
            {
                Logger.Log($"ClientConnectionHandler: Found MultiplayerScene by searching scene tree at {multiplayerScene.GetPath()}");
                return multiplayerScene;
            }
            
            // Try to find in common locations
            string[] possiblePaths = new[] {
                "/root/Root/WorldRoot/Scene",
                "/root/Scene",
                "/root/Root/Scene",
                "../Scene",
                "../../Scene"
            };
            
            foreach (var path in possiblePaths)
            {
                try
                {
                    var node = GetNodeOrNull(path);
                    if (node is MultiplayerScene scene)
                    {
                        Logger.Log($"ClientConnectionHandler: Found MultiplayerScene at path: {path}");
                        return scene;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error checking path {path}: {ex.Message}");
                }
            }
            
            Logger.Error("ClientConnectionHandler: Could not find MultiplayerScene");
            return null;
        }
        
        private T FindNodeByType<T>(Node root) where T : class
        {
            // Check if the current node is of the desired type
            if (root is T result)
            {
                return result;
            }
            
            // Recursively search through all children
            foreach (var child in root.GetChildren())
            {
                var found = FindNodeByType<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }
    }
}
