using Aquamarine.Source.Input;
using Godot;

namespace Aquamarine.Source.Management.HUD;

public partial class MainMenu : Control
{
    public static MainMenu Instance;
    [Export] public Button CloseButton;

    public override void _Ready()
    {
        base._Ready();
        Instance = this;

        CloseButton.Pressed += ToggleMenu;
    }

    void ToggleMenu()
    {
        InputManager.MovementLocked = !InputManager.MovementLocked;
        Visible = InputManager.MovementLocked;
    }
}
