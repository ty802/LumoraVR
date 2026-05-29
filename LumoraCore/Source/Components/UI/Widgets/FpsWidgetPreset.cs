// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.UI;

public sealed class FpsWidgetPreset : TextWidgetPreset
{
    protected override void SetupText(Text text)
    {
        var metrics = text.Slot.AttachComponent<PerformanceMetrics>();
        var driver = text.Slot.AttachComponent<FpsTextDriver>();
        driver.Metrics.Target = metrics;
        driver.Target.Target = text;
    }
}
