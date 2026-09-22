// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Opens the create-world dialog that the Home screen owns. Works from a popped-out panel too: it brings
// the dash to Home first, because the dialog is part of that screen.
[ComponentCategory("Hidden")]
public sealed class NewWorldWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "New World";
    protected override string? Icon => "new-world";
    protected override color IconTint => DashTheme.Positive;

    protected override bool Primary => true;

    protected override void Invoke()
    {
        var dash = Dash;
        if (dash == null)
            return;
        foreach (var screen in dash.Screens)
        {
            if (screen is not HomeScreen home)
                continue;
            dash.SwitchTo(home);
            home.OpenCreateMenu();
            return;
        }
    }
}
