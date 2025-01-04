using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Godot;

namespace Aquamarine.Source.Management.HUD;

public partial class HUDManager : Control
{
    public static HUDManager Instance;

    [Export] public Control DebugOverlay;

    [Export] public Control MainMenu;
    [Export] public Control WorldsTab;

    public override void _Ready()
    {
        base._Ready();
        Instance = this;

        DebugOverlay.Visible = false;
        DebugOverlay.ProcessMode = ProcessModeEnum.Disabled;

        Logger.Log("HUD initialized.");
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);

        if (@event.IsActionPressed("ui_debug"))
        {
            DebugOverlay.Visible = !DebugOverlay.Visible;
            DebugOverlay.ProcessMode = DebugOverlay.Visible ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
            Logger.Log($"Debug overlay toggled: {DebugOverlay.Visible}.");
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            InputManager.MovementLocked = !InputManager.MovementLocked;
            MainMenu.Visible = InputManager.MovementLocked;
        }

        if (@event.IsActionPressed("ui_home"))
        {
            Logger.Log($"Sample log message.");
            Logger.Warn($"Sample warn message.");
            Logger.Error($"Sample error message.");
            Logger.Debug($"Sample debug message.");
        }
    }
}
