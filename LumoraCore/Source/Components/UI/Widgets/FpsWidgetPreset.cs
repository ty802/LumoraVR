// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public sealed class FpsWidgetPreset : TextWidgetPreset
{
    private PerformanceMetrics? _metrics;

    protected override void BuildBackground(Slot root)
    {
        _metrics = root.AttachComponent<PerformanceMetrics>();

        var graphSlot = root.AddSlot("Graph");
        var rect = graphSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;

        var recorder = graphSlot.AttachComponent<ValueGraphRecorder>();
        recorder.Source.Target = _metrics.FPS;
        recorder.RangeMin.Value = 0f;
        recorder.RangeMax.Value = 120f;

        var graph = graphSlot.AttachComponent<LineGraphMesh>();
        graph.Recorder.Target = recorder;
        graph.Color.Value = new color(0.40f, 0.85f, 1f, 0.9f);
        graph.FillColor.Value = new color(0.40f, 0.85f, 1f, 0.15f);
        graph.Width.Value = 2f;
        graph.FillBelow.Value = true;
    }

    protected override void SetupText(Text text)
    {
        var metrics = _metrics ?? text.Slot.AttachComponent<PerformanceMetrics>();
        var driver = text.Slot.AttachComponent<MultiValueTextFormatDriver>();
        driver.Format.Value = "{0:F0} FPS";
        driver.Sources.Add(metrics.FPS);
        driver.Target.Target = text.Content;
        _metrics = null;
    }
}
