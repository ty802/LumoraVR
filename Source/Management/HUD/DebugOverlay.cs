using Aquamarine.Source.Logging;
using Godot;

namespace Aquamarine.Source.Management.HUD;

public partial class DebugOverlay : Control
{
    [Export] public RichTextLabel Text;

    public override void _Ready()
    {
        base._Ready();
        Logger.Log("DebugOverlay initialized.");
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        Text.Text =
        $"Game\n" +
        $"├─ FPS: {Engine.GetFramesPerSecond()}\n" +
        $"└─ PTPS: {Engine.PhysicsTicksPerSecond}\n" +
        $"\nNetworking\n" +
        $"└─ Player Count: {MultiplayerScene.Instance.PlayerList.Count}\n" +
        $"\nPlayer\n" +
        $"├─ Velocity: DUMMY\n" +
        $"└─ IsOnFloor: DUMMY";
    }
}
