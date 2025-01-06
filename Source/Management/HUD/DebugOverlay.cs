using Aquamarine.Source.Helpers;
using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Godot;
using System;
using System.Text;

namespace Aquamarine.Source.Management.HUD;

public partial class DebugOverlay : Control
{
    [Export] public RichTextLabel StatsText;
    [Export] public RichTextLabel ConsoleText;
    [Export] public LineEdit ConsoleInput;
    readonly StringBuilder statsTextStringBuilder = new();
    readonly StringBuilder consoleTextStringBuilder = new();

    readonly string boolLabel = "  [[color=silver]bool[/color]]";
    readonly string intLabel = "  [[color=lime_green]int[/color]]";
    readonly string floatLabel = "  [[color=deep_sky_blue]float[/color]]";
    readonly string vector2Label = "  [[color=deep_sky_blue]vec2[/color]]";
    readonly string vector3Label = "  [[color=deep_sky_blue]vec3[/color]]";
    readonly string stringLabel = "  [[color=red]string[/color]]";

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
        Logger.OnPrettyLogMessageWritten += OnLogMessageWritten;
        ConsoleInput.TextSubmitted += OnConsoleInputSubmitted;
        Visible = false;
        Logger.Log("DebugOverlay initialized.");
    }

    private void OnConsoleInputSubmitted(string newText)
    {
        ConsoleInput.Clear();
        var strings = newText.ToLower().Split(' ');
        if (strings.Length == 0) return;
        var player = MultiplayerScene.Instance.GetLocalPlayer();
        try
        {
            switch (strings[0])
            {
                case "clear":
                    for (int i = 0; i < recentEntries.Length; i++)
                    {
                        recentEntries[i] = string.Empty;
                    }
                    break;
                case "connect":
                    if (strings.Length < 2)
                    {
                        Logger.Warn("Usage: connect <ip>");
                        return;
                    }
                    //MultiplayerScene.Instance.ConnectToServer(strings[1]);
                    break;
                case "respawn":
                    player?.Respawn();
                    break;
                case "exit":
                    GetTree().Quit();
                    break;
                default:
                    Logger.Warn($"Unknown command: {strings[0]}");
                    return;
            }
            Logger.Debug($"Ran Command: {newText}");
        }
        catch
        {
            Logger.Error($"Failed to run command: [{newText}] is your syntax malformed?");
        }
    }

    string[] recentEntries = new string[24];
    private void OnLogMessageWritten(string message)
    {
        for (int i = recentEntries.Length - 1; i > 0; i--)
        {
            recentEntries[i] = recentEntries[i - 1];
        }
        recentEntries[0] = message;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        statsTextStringBuilder.Clear();
        statsTextStringBuilder.AppendLine("[code]");

        statsTextStringBuilder.AppendLine("Game");
        statsTextStringBuilder.AppendLine($"{intLabel} FPS: {string.Format(intValue, Engine.GetFramesPerSecond())}");
        statsTextStringBuilder.AppendLine($"{intLabel} Physics TPS: {string.Format(intValue, Engine.PhysicsTicksPerSecond)}");
        statsTextStringBuilder.AppendLine();

        statsTextStringBuilder.AppendLine("Networking");
        statsTextStringBuilder.AppendLine($"{intLabel} Player Count: {string.Format(intValue, MultiplayerScene.Instance.PlayerList.Count)}");
        statsTextStringBuilder.AppendLine();

        var player = MultiplayerScene.Instance.GetLocalPlayer();

        if (player is not null)
        {
            statsTextStringBuilder.AppendLine("Player");
            statsTextStringBuilder.AppendLine($"{vector2Label} Movement: {string.Format(vector2Value, [InputManager.Movement.X.ToString("F2"), InputManager.Movement.Y.ToString("F2")])}");
            statsTextStringBuilder.AppendLine($"{vector3Label} Velocity: {string.Format(vector3Value, [player.Velocity.X.ToString("F2"), player.Velocity.Y.ToString("F2"), player.Velocity.Z.ToString("F2")])}");
            statsTextStringBuilder.AppendLine($"{boolLabel} Crouching: {(InputButton.Crouch.Held() ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");
            statsTextStringBuilder.AppendLine($"{boolLabel} Sprinting: {(InputButton.Sprint.Held() ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");
            statsTextStringBuilder.AppendLine($"{boolLabel} Jumping: {(InputButton.Jump.Held() ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");
            statsTextStringBuilder.AppendLine($"{boolLabel} Grounded: {(player.IsOnFloor() ? string.Format(boolTrueValue, true) : string.Format(boolFalseValue, false))}");
            statsTextStringBuilder.AppendLine();
        }
        else
        {
            statsTextStringBuilder.AppendLine("Player");
            statsTextStringBuilder.AppendLine("  No player found.");
            statsTextStringBuilder.AppendLine();
        }

        statsTextStringBuilder.AppendLine("[/code]");
        StatsText.Text = statsTextStringBuilder.ToString();

        consoleTextStringBuilder.Clear();
        consoleTextStringBuilder.AppendLine("[code]");
        foreach (var t in recentEntries) consoleTextStringBuilder.AppendLine(t);
        consoleTextStringBuilder.AppendLine("[/code]");
        ConsoleText.Text = consoleTextStringBuilder.ToString();
    }
}
