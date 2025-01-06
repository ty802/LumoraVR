using Godot;
using Aquamarine.Source.Logging;
using System.Linq;
using System.Collections.Generic;

namespace Aquamarine.Source.Management.HUD;

public partial class WindowStateSetButton : Button
{
    [Export] public Control Window;
    [Export] public WindowStateEnum State;

    public override void _Ready()
    {
        base._Ready();

        if (Window == null)
        {
            Logger.Error($"Window State Set Button on {Name} has a null window.");
            return;
        }
    }

    public override void _Pressed()
    {
        base._Pressed();

        switch (State)
        {
            case WindowStateEnum.Open:
                Window.Visible = true;
                Window.ProcessMode = ProcessModeEnum.Inherit;
                break;
            case WindowStateEnum.Close:
                Window.Visible = false;
                Window.ProcessMode = ProcessModeEnum.Disabled;
                break;
            case WindowStateEnum.Toggle:
                Window.Visible = !Window.Visible;
                Window.ProcessMode = Window.Visible ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
                break;
        }
    }
}

public enum WindowStateEnum
{
    Close,
    Open,
    Toggle
}
