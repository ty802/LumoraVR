using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Godot;

namespace Aquamarine.Source.Management.HUD;

public partial class MainMenu : Control
{
    public static MainMenu Instance;

    public override void _Ready()
    {
        base._Ready();
        Instance = this;
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);
    }
}
