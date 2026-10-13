// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

// Handlers for the widget sampler on the test bench. Its button, checkbox and slider bind to these methods
// rather than to inline closures, so the actions are stored as element and method references and survive
// duplication: a cloned panel's controls drive the clone's own status line, not the original's. -xlinka
[ComponentCategory("Hidden")]
public sealed class HelioTestActions : Component
{
    public readonly SyncRef<Text> Status;

    public HelioTestActions()
    {
        Status = new SyncRef<Text>(this);
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
