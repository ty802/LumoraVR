using Aquamarine.Source.Logging;
using Bones.Core;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading.Tasks;

namespace Aquamarine.Source.Management
{
    public partial class WorldManager : Node
    {
        public static WorldManager Instance;

        [Export] private Node _worldContainer;
        [Export] private Control _loadingScreen;
        [Export] private ProgressBar _progressBar;

        private string _currentWorldPath = "";
        private PackedScene _loadedWorld;
        private bool _isLoading = false;
        private float[] _progress = new float[1];

        public override void _Ready()
        {
            Instance = this;

            // Defer node finding to prevent errors if nodes don't exist yet
            CallDeferred(nameof(InitializeComponents));

            // Start loading local home immediately
            LoadLocalHome();
        }

        private void InitializeComponents()
        {
            try
            {
                // Try to find components, but don't crash if they don't exist
                _worldContainer = GetNodeOrNull<Node>("/root/Root/WorldRoot");
                if (_worldContainer == null)
                {
                    Logger.Error("WorldRoot node not found! World loading may not work properly.");
                }

                _loadingScreen = GetNodeOrNull<Control>("/root/Root/HUDManager/LoadingMenu");
                if (_loadingScreen == null)
                {
                    Logger.Error("LoadingMenu not found! Loading screen will not be displayed.");
                }
                else
                {
                    _loadingScreen.Visible = false;

                    _progressBar = _loadingScreen.GetNodeOrNull<ProgressBar>("ProgressBar");
                    if (_progressBar == null)
                    {
                        Logger.Error("ProgressBar not found in LoadingMenu! Progress will not be displayed.");
                    }
                }

                Logger.Log("WorldManager components initialized.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing WorldManager components: {ex.Message}");
            }
        }

        public void LoadWorld(string worldPath, bool disconnectFromCurrent = true)
        {
            if (_isLoading) return;

            _isLoading = true;
            _currentWorldPath = worldPath;

            if (disconnectFromCurrent && ClientManager.Instance != null)
            {
                ClientManager.Instance.DisconnectFromCurrentServer();
            }

            // Only show loading screen if it exists
            if (_loadingScreen != null)
            {
                _loadingScreen.Visible = true;
            }

            // Only update progress bar if it exists
            if (_progressBar != null)
            {
                _progressBar.Value = 0;
            }

            ResourceLoader.LoadThreadedRequest(worldPath, "", true, ResourceLoader.CacheMode.Reuse);
            Logger.Log($"Started loading world: {worldPath}");
        }

        public override void _Process(double delta)
        {
            if (_isLoading)
            {
                UpdateLoadingProgress();
            }
        }

        private void UpdateLoadingProgress()
        {
            // Create a Godot.Collections.Array for the progress parameter
            var progressArray = new Godot.Collections.Array();
            progressArray.Add(0.0f); // Initialize with 0 progress

            var status = ResourceLoader.LoadThreadedGetStatus(_currentWorldPath, progressArray);

            // Extract the progress value from the Godot array
            float progress = 0;
            if (progressArray.Count > 0)
            {
                progress = (float)progressArray[0];
            }

            // Only update progress bar if it exists
            if (_progressBar != null)
            {
                _progressBar.Value = progress * 100;
            }

            if (status == ResourceLoader.ThreadLoadStatus.Loaded)
            {
                FinishWorldLoading();
            }
            else if (status == ResourceLoader.ThreadLoadStatus.Failed)
            {
                HandleLoadError();
            }
        }

        private void FinishWorldLoading()
        {
            _isLoading = false;

            try
            {
                _loadedWorld = ResourceLoader.LoadThreadedGet(_currentWorldPath) as PackedScene;

                if (_worldContainer == null)
                {
                    // Try one more time to find world container
                    _worldContainer = GetNodeOrNull<Node>("/root/Root/WorldRoot");

                    if (_worldContainer == null)
                    {
                        Logger.Error("WorldRoot node still not found! Cannot load world.");
                        return;
                    }
                }

                foreach (Node child in _worldContainer.GetChildren())
                {
                    child.QueueFree();
                }

                Node newWorld = _loadedWorld.Instantiate();
                _worldContainer.AddChild(newWorld);

                // Hide loading screen if it exists
                if (_loadingScreen != null)
                {
                    _loadingScreen.Visible = false;
                }

                if (IsServerMode())
                {
                    var multiplayerScene = newWorld.GetNodeOrNull("MultiplayerScene");
                    if (multiplayerScene != null)
                    {
                        multiplayerScene.Call("InitializeForServer");
                        Logger.Log("서버용 멀티플레이어 컴포넌트 초기화 완료.");
                    }
                    else
                    {
                        Logger.Error("MultiplayerScene을 찾을 수 없습니다.");
                    }
                }

                Logger.Log($"World loaded successfully: {_currentWorldPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading world: {ex.Message}");

                // Hide loading screen if it exists
                if (_loadingScreen != null)
                {
                    _loadingScreen.Visible = false;
                }
            }
        }

        private bool IsServerMode()
        {
            return OS.HasFeature("server"); // 서버 모드 체크
        }

        private void HandleLoadError()
        {
            _isLoading = false;
            Logger.Error($"Filed to LoadWorld: {_currentWorldPath}");

            // Hide loading screen if it exists
            if (_loadingScreen != null)
            {
                _loadingScreen.Visible = false;
            }

            LoadLocalHome();
        }

        public void LoadLocalHome()
        {
            LoadWorld("res://Scenes/World/LocalHome.tscn", false);

            this.CreateTimer(0.5f, () => {
                if (ClientManager.Instance != null)
                {
                    ClientManager.Instance.JoinServer("localhost", 6000);
                }
            });
        }
    }
}
