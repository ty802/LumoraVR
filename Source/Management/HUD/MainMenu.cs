using Aquamarine.Source.Input;
using Godot;

namespace Aquamarine.Source.Management.HUD;

public partial class MainMenu : Control
{
    [Export] public Button CloseButton;

    public override void _Ready()
    {
        base._Ready();
        CloseButton.Pressed += ToggleMenu;
        Visible = false;
    }

    void ToggleMenu()
    {
        InputManager.MovementLocked = !InputManager.MovementLocked;
        Visible = InputManager.MovementLocked;
    }
}
