// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.UI;

public sealed class ClockWidgetPreset : TextWidgetPreset
{
    protected override void SetupText(Text text)
    {
        text.Slot.AttachComponent<CurrentDateTimeTextDriver>().Target.Target = text;
    }
}
