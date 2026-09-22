// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Stop the dash snapping in front of the view every frame, so it can be placed and left there.
[ComponentCategory("Hidden")]
public sealed class FreeformDashWidgetPreset : ToggleWidgetPreset
{
    protected override string Title => "Freeform Dash";
    protected override string? Icon => "freeform";
    protected override color IconTint => DashTheme.Warning;

    protected override bool Value
    {
        get => UserspaceDashboard.LocalInstance?.Freeform.Value ?? false;
        set => UserspaceDashboard.LocalInstance?.SetFreeform(value);
    }
}
