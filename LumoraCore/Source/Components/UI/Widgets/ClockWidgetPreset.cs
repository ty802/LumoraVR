// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components.UI;

[ComponentCategory("Hidden")]
public sealed class ClockWidgetPreset : TextWidgetPreset
{
    protected override void SetupText(Text text)
    {
        var driver = text.Slot.AttachComponent<CurrentDateTimeTextDriver>();
        driver.Format.Value = "HH:mm";
        driver.Target.DriveTarget(text.Content);
    }
}
