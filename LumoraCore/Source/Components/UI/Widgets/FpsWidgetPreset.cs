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

        // Sparkline behind the text, filling the pill. The auto-ranged line
        // sits in the middle band (never the top/bottom corners), so it stays
        // clear of the rounded border; the Mask clips the fill to the box, and
        // the widget's GraphicChunkRoot keeps this from rebuilding the whole
        // canvas each sample.
        var graphSlot = root.AddSlot("Graph");
        var rect = graphSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
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
        graph.Color.Value = new color(0.40f, 0.85f, 1f, 0.85f);
        graph.FillColor.Value = new color(0.40f, 0.85f, 1f, 0.12f);
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
