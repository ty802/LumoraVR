// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Handlers for the Helio validation panel. The panel's buttons, checkbox and
/// slider bind to these methods rather than to inline closures, so the actions
/// are stored as element + method references and survive duplication: a cloned
/// panel's controls drive the clone's own labels, not the original's.
/// </summary>
public sealed class HelioTestActions : Component
{
    public readonly SyncRef<PanelShell> Panel;
    public readonly SyncRef<Text> Status;
    public readonly Sync<int> ShellClickCount;

    public HelioTestActions()
    {
        Panel = new SyncRef<PanelShell>(this);
        Status = new SyncRef<Text>(this);
        ShellClickCount = new Sync<int>(this, 0);
    }

    [SyncMethod]
    public void OnShellPressed(Button button, UIInteractionContext context)
    {
        ShellClickCount.Value++;
        bool alternate = ShellClickCount.Value % 2 == 1;

        var panel = Panel.Target;
        if (panel != null)
        {
            panel.Title.Value = alternate ? $"Helio Validation {ShellClickCount.Value}" : "Helio Validation";
            panel.HeaderColor.Value = alternate
                ? new color(0.17f, 0.11f, 0.22f, 0.98f)
                : new color(0.105f, 0.130f, 0.165f, 0.98f);
        }

        SetStatus("PanelShell title/header updated live");
    }

    [SyncMethod]
    public void OnLaserPressed(Button button, UIInteractionContext context)
    {
        SetStatus($"Clicked {DateTime.Now:HH:mm:ss}");
    }

    [SyncMethod]
    public void OnCheckboxChanged(Checkbox checkbox, bool value)
    {
        SetStatus(value ? "Checkbox on" : "Checkbox off");
    }

    [SyncMethod]
    public void OnSliderChanged(Slider slider, float value)
    {
        SetStatus($"Slider {value:0.00}");
    }

    private void SetStatus(string text)
    {
        var status = Status.Target;
        if (status != null)
            status.Content.Value = text;
    }
}
