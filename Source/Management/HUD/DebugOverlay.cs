using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Godot;
using System;
using System.Text;

namespace Aquamarine.Source.Management.HUD;

public partial class DebugOverlay : Control
{
    [Export] public RichTextLabel Text;
    readonly StringBuilder stringBuilder = new();

    readonly string boolLabel = " [[color=silver]bool[/color]]";
    readonly string intLabel = " [[color=lime_green]int[/color]]";
    readonly string floatLabel = " [[color=deep_sky_blue]float[/color]]";
    readonly string vector2Label = " [[color=deep_sky_blue]vec2[/color]]";
    readonly string vector3Label = " [[color=deep_sky_blue]vec3[/color]]";
    readonly string stringLabel = " [[color=red]string[/color]]";

    readonly string boolTrueValue = "[color=pale_green]true[/color]";
    readonly string boolFalseValue = "[color=indian_red]false[/color]";
    readonly string intValue = "[color=pale_green]{0}[/color]";
    readonly string floatValue = "[color=sky_blue]{0}[/color]";
    readonly string vector2Value = "([color=sky_blue]{0},{1}[/color])";
    readonly string vector3Value = "([color=sky_blue]{0},{1},{2}[/color])";
    readonly string stringValue = "[color=indian_red]\"{0}\"[/color]";

    public override void _Ready()
    {
        base._Ready();
        Logger.Log("DebugOverlay initialized.");
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        stringBuilder.Clear();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("[center][font_size=24][color=aqua]DEBUG[/color] OVERLAY[/font_size][/center]");
        stringBuilder.AppendLine();
        stringBuilder.AppendLine($"[font_size=20][u]General State Information[/u][/font_size]");
        stringBuilder.AppendLine("[code]");

        stringBuilder.AppendLine("Game");
        stringBuilder.AppendLine($"{intLabel} {"FPS:"} {string.Format(intValue, Engine.GetFramesPerSecond())}");
        stringBuilder.AppendLine($"{intLabel} {"Physics TPS:"} {string.Format(intValue, Engine.PhysicsTicksPerSecond)}");
        stringBuilder.AppendLine();

        stringBuilder.AppendLine("Networking");
        stringBuilder.AppendLine($"{intLabel} {"Player Count:"} {string.Format(intValue, MultiplayerScene.Instance.PlayerList.Count)}");
        stringBuilder.AppendLine();

        stringBuilder.AppendLine("Player");
        stringBuilder.AppendLine($"{vector2Label} {"Movement:"} {string.Format(vector2Value, [MathF.Round(InputManager.Movement.X, 2), MathF.Round(InputManager.Movement.Y, 2)])}");
        stringBuilder.AppendLine($"{boolLabel} {"Crouching:"} {(InputButton.Crouch.Held() ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");
        stringBuilder.AppendLine($"{boolLabel} {"Sprinting:"} {(InputButton.Sprint.Held() ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");
        stringBuilder.AppendLine($"{boolLabel} {"Jumping:"} {(InputButton.Jump.Held() ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");
        //stringBuilder.AppendLine($"{vector3Label + "Velocity:"} {string.Format(vector3Value, [1f, 2f, 3f])}");
        //stringBuilder.AppendLine($"{boolLabel + "Grounded:"} {(true ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");

        stringBuilder.AppendLine("[/code]");

        Text.Text = stringBuilder.ToString();
    }
}
