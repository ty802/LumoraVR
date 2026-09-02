// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

[ComponentCategory("Hidden")]
public sealed class FpsWidgetPreset : TextWidgetPreset
{
    // Height of the sparkline strip at the bottom of the pill, and the gap the number keeps above it.
    private const float GraphHeight = 12f;
    private const float GraphInsetX = 8f;
    private const float GraphInsetY = 4f;

    private PerformanceMetrics? _metrics;

    protected override void BuildBackground(Slot root)
    {
        _metrics = root.AttachComponent<PerformanceMetrics>();

        // The sparkline used to fill the whole pill and auto-range through the middle, which is
        // exactly where the number is: at any steady frame rate the line drew a strikethrough over
        // its own text. It gets its own strip along the bottom now and the number sits above it.
        // The strip is bare pill surface: the line and its soft fill are the whole graphic, no
        // darker well behind them (that read as a black bar under the number). Inset from the sides
        // so the strip stays inside the pill's rounded corners (the Mask clips to this rect, it
        // knows nothing about the corner radius). -xlinka
        var graphSlot = root.AddSlot("Graph");
        var rect = graphSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0f, 0f);
        rect.AnchorMax.Value = new float2(1f, 0f);
        rect.OffsetMin.Value = new float2(GraphInsetX, GraphInsetY);
        rect.OffsetMax.Value = new float2(-GraphInsetX, GraphInsetY + GraphHeight);
        graphSlot.AttachComponent<Mask>();

        var lineSlot = graphSlot.AddSlot("Line");
        var lineRect = lineSlot.AttachComponent<RectTransform>();
        lineRect.AnchorMin.Value = float2.Zero;
        lineRect.AnchorMax.Value = float2.One;
        lineRect.OffsetMin.Value = float2.Zero;
        lineRect.OffsetMax.Value = float2.Zero;

        var recorder = lineSlot.AttachComponent<ValueGraphRecorder>();
        recorder.Source.Target = _metrics.FPS;
        // Auto-fit to the recent window so the line stays centered at any frame
        // rate instead of pinning to the top edge above ~120 FPS.
        recorder.AutoRange.Value = true;

        var graph = lineSlot.AttachComponent<LineGraphMesh>();
        graph.Recorder.Target = recorder;
        graph.Color.Value = DashTheme.Accent;
        // The area under the line is a hint, not a bar: AccentSoft (0.18) read as a solid purple
        // block at this height, so the fill sits well below it and the line stays the graphic.
        graph.FillColor.Value = DashTheme.Accent.WithAlpha(0.08f);
        graph.Width.Value = 1.5f;
        graph.FillBelow.Value = true;
    }

    protected override void SetupText(Text text)
    {
        // Lift the number clear of the strip so the two never share pixels.
        var rect = text.RectTransform;
        if (rect != null)
            rect.OffsetMin.Value = new float2(0f, GraphInsetY + GraphHeight);

        var metrics = _metrics ?? text.Slot.AttachComponent<PerformanceMetrics>();
        var driver = text.Slot.AttachComponent<MultiValueTextFormatDriver>();
        driver.Format.Value = "{0:F0} FPS";
        driver.Sources.Add(metrics.FPS);
        driver.Target.Target = text.Content;
        _metrics = null;
    }
}
