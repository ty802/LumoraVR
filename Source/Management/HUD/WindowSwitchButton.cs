using Godot;
using Aquamarine.Source.Logging;
using System.Linq;
using System.Collections.Generic;

namespace Aquamarine.Source.Management.HUD;

public partial class WindowSwitchButton : Button
{
    [Export] public Control Window;
    private IEnumerable<Control> _windows;

    public override void _Ready()
    {
        base._Ready();

        if (Window == null)
        {
            Logger.Error($"Window Switch Button on {Name} has a null window.");
            return;
        }

        _windows = Window.GetParent().GetChildren().Cast<Control>();
    }

    public override void _Pressed()
    {
        base._Pressed();

        foreach (Control child in _windows)
        {
            child.Visible = child == Window;
        }
    }
}
