// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Import whatever is on the OS clipboard into the focused world. Same gate the world-side Ctrl+V
// importer uses, so the button is honest about when a paste would actually land.
[ComponentCategory("Hidden")]
public sealed class PasteWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "Paste";
    protected override string? Icon => "paste";
    protected override color IconTint => DashTheme.IconTeal;

    protected override string? Unavailable
    {
        get
        {
            if (ImportHandlers.Clipboard == null)
                return "No clipboard";
            var world = FocusedWorld;
            if (world == null)
                return "No world";
            if (!world.IsAuthority)
                return "Host only";
            return world.State == World.WorldState.Running ? null : "World loading";
        }
    }

    protected override void Invoke() => ImportHandlers.Clipboard?.Paste();
}
