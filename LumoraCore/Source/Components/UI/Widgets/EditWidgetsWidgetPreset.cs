// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Show the grid lines and make every widget draggable, here and on the popped-out panels.
[ComponentCategory("Hidden")]
public sealed class EditWidgetsWidgetPreset : ToggleWidgetPreset
{
    protected override string Title => "Edit Widgets";
    protected override string? Icon => "edit-widgets";
    protected override color IconTint => DashTheme.Accent;

    protected override bool LiveInEditMode => true;

    protected override bool Value
    {
        get => WidgetPanel.EditMode;
        set => WidgetPanel.EditMode = value;
    }
}
