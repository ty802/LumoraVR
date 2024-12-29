using System.Linq;
using Bones.Core;
using Godot;

namespace Aquamarine.Source.Management;

public partial class StartManager : Node
{
    [Export] public Label Text;
    public override void _Process(double delta)
    {
        base._Process(delta);
        var args = OS.GetCmdlineArgs();

        var isServer = args.Any(i => i.Equals("--run-server", System.StringComparison.CurrentCultureIgnoreCase));

        Text.Text = isServer.ToString();
        
        //GD.Print("CLAPCLAPCLAPFUCK");
        //GD.Print(isServer);
        
        GetTree().ChangeSceneToFile(isServer ? "res://Scenes/Server.tscn" : "res://Scenes/Client.tscn");
    }
}
