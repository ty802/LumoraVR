// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The debug screen as a home card: a tool you open on purpose from the home page, not a tab standing
// beside Worlds and Settings on every screen.
[ComponentCategory("Hidden")]
public sealed class OpenDebugWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "Widget.Debug.Title".AsLocale("Debug").Resolve();

    protected override string? Icon => "debug";

    protected override color IconTint => DashTheme.Warning;

    protected override string? Unavailable => Dash == null ? "Widget.Debug.DashOnly".AsLocale("Open from the dash").Resolve() : null;

    protected override void Invoke()
    {
        var dash = Dash;
        if (dash == null)
            return;
        foreach (var screen in dash.Screens)
        {
            if (screen is DebugScreen)
            {
                dash.SwitchTo(screen);
                return;
            }
        }
    }
}
