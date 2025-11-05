using System;
using System.Linq;
using Aquamarine.Source.Logging;
using Godot;
using RuntimeEngine = Aquamarine.Source.Core.Engine;
using Logger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Management
{
    public partial class StartManager : Node
    {
        [Export] public Label Text;

        public override void _Ready()
        {
            base._Ready();
            try
            {
                var isServer = RuntimeEngine.IsDedicatedServer;
                Text.Text = isServer.ToString();
                Logger.Log($"Application started in {(isServer ? "server" : "client")} mode.");

                // Defer scene change to avoid modifying the scene tree immediately
                CallDeferred(nameof(ChangeScene), isServer);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in StartManager: {ex.Message}");
            }
        }

        private void ChangeScene(bool isServer)
        {
            GetTree().ChangeSceneToFile("res://Scenes/Client.tscn");
            Logger.Log(isServer
                ? "Dedicated server flag detected - running unified client scene in host mode."
                : "Scene changed to Client.tscn.");
        }
    }
}
