// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

[ComponentCategory("Utility")]
public class FpsTextDriver : Component
{
    public readonly SyncRef<PerformanceMetrics> Metrics;
    public readonly SyncRef<Text> Target;
    public readonly Sync<string> Format;

    public FpsTextDriver()
    {
        Metrics = new SyncRef<PerformanceMetrics>(this);
        Target = new SyncRef<Text>(this);
        Format = new Sync<string>(this, "{0:0} FPS");
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var text = Target.Target;
        var metrics = Metrics.Target;
        if (text != null && metrics != null)
            text.Content.Value = string.Format(Format.Value, metrics.FPS.Value);
    }
}
