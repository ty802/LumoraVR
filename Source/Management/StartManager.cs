using System.Linq;
using Aquamarine.Source.Logging;
using Bones.Core;
using Godot;

namespace Aquamarine.Source.Management
{
    public partial class StartManager : Node
    {
        [Export] public Label Text;

        public override void _Process(double delta)
        {
            try
            {
                base._Process(delta);

                var args = OS.GetCmdlineArgs();
                var isServer = args.Any(i => i.Equals("--run-server", System.StringComparison.CurrentCultureIgnoreCase));

                Text.Text = isServer.ToString();
                Logger.Log($"Application started in {(isServer ? "server" : "client")} mode.");

                GetTree().ChangeSceneToFile(isServer ? "res://Scenes/Server.tscn" : "res://Scenes/Client.tscn");
                Logger.Log($"Scene changed to {(isServer ? "Server.tscn" : "Client.tscn")}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in StartManager: {ex.Message}");
            }
        }
    }
}
