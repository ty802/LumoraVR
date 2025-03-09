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
            _worldContainer = GetNode<Node>("/root/Root/WorldRoot");
            _loadingScreen = GetNode<Control>("/root/Root/HUDManager/LoadingMenu");
            _progressBar = GetNode<ProgressBar>("/root/Root/HUDManager/LoadingMenu/ProgressBar");

            _loadingScreen.Visible = false;

            LoadLocalHome();
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

            _loadingScreen.Visible = true;
            _progressBar.Value = 0;

            ResourceLoader.LoadThreadedRequest(worldPath, "", true, ResourceLoader.CacheMode.Reuse);
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
            var status = ResourceLoader.LoadThreadedGetStatus(_currentWorldPath);

            _progressBar.Value = _progress[0] * 100;

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

            _loadedWorld = ResourceLoader.LoadThreadedGet(_currentWorldPath) as PackedScene;

            foreach (Node child in _worldContainer.GetChildren())
            {
                child.QueueFree();
            }

            Node newWorld = _loadedWorld.Instantiate();
            _worldContainer.AddChild(newWorld);

            _loadingScreen.Visible = false;
        }

        private void HandleLoadError()
        {
            _isLoading = false;
            Logger.Error($"Filed to LoadWorld: {_currentWorldPath}");

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
