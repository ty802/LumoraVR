// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components;

// Draws the curve graph: the grid, the clip's length, the ruler, every shown channel's curve, the keys,
// the selected key's tangent handles and the playhead. One mesh in its own chunk, so a drag re-meshes
// this and nothing else on the panel.
//
// Everything is read off the panel on the main thread in PrepareCompute (the keys, the curves sampled
// across the visible span) and only turned into triangles on the worker, which never touches the world.
// Every machine draws its own from its own copy of the keys, so the graph needs nothing synced beyond
// the selection and the view. Geometry past the plot is left to the mask to clip. -xlinka
[ComponentCategory("Utility/Inspectors")]
public sealed class CurveGraphGraphic : Graphic
{
    public readonly SyncRef<CurveStudioPanel> Panel;
    public readonly SyncRef<CurveGraph> Graph;

    private static readonly color RulerColor = new(0.09f, 0.09f, 0.13f, 1f);
    private static readonly color OutsideColor = new(0f, 0f, 0f, 0.32f);
    private static readonly color GridColor = new(1f, 1f, 1f, 0.08f);
    private static readonly color MajorColor = new(1f, 1f, 1f, 0.16f);
    private static readonly color ZeroColor = new(1f, 1f, 1f, 0.30f);
    private static readonly color TickColor = new(1f, 1f, 1f, 0.45f);

    // Widths in canvas units. At the panel's half-millimetre unit a line under about 2.5 is less than a
    // pixel from a metre back, and the grid simply faded out. -xlinka
    private const float GridWidth = 2.4f;
    private const float MajorWidth = 3.2f;
    private const float ZeroWidth = 4f;
    private const float CurveWidth = 6f;
    private const float FadedCurveWidth = 4f;
    private static readonly color EndColor = new(0.72f, 0.56f, 1f, 0.55f);
    private static readonly color PlayheadColor = new(1f, 0.78f, 0.30f, 0.95f);
    private static readonly color SelectedColor = new(1f, 1f, 1f, 1f);
    private static readonly color HandleColor = new(1f, 1f, 1f, 0.75f);

    private sealed class CurveLine
    {
        public color Color;
        public float Width;
        public readonly List<float2> Points = new();
        public readonly List<float2> Keys = new();
        public int Selected = -1;
        public int Hovered = -1;
        public bool Active;
    }

    private readonly List<CurveLine> _lines = new();
    private readonly List<CurveLine> _pool = new();
    private readonly List<float> _timeTicks = new();
    private readonly List<float> _valueTicks = new();
    private bool _valid;
    private CurveView _view;
    private float _timeStep;
    private float _duration;
    private bool _fixedRange;
    private float _playhead;
    private bool _handles;
    private float2 _handleKey;
    private float2 _handleIn;
    private float2 _handleOut;
    private int _drawnVersion = -1;

    public CurveGraphGraphic()
    {
        Panel = new SyncRef<CurveStudioPanel>(this);
        Graph = new SyncRef<CurveGraph>(this);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect) => rect.MarkGraphicDirty();

    public override bool IsPointInside(in float2 point) => RectTransform?.LocalComputeRect.Contains(point) ?? false;

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var panel = Panel.Target;
        if (panel == null || panel.LocalVersion == _drawnVersion)
            return;
        _drawnVersion = panel.LocalVersion;
        RectTransform?.MarkGraphicDirty();
    }

    // MAIN THREAD: snapshot

    public override void PrepareCompute()
    {
        _valid = false;
        foreach (var line in _lines)
            _pool.Add(line);
        _lines.Clear();

        var panel = Panel.Target;
        var rect = RectTransform?.LocalComputeRect;
        if (panel == null || !rect.HasValue || rect.Value.IsEmpty)
            return;
        _view = new CurveView(rect.Value, panel.TimeView.Value, panel.ValueView.Value);
        if (!_view.IsValid)
            return;
        _valid = true;

        var source = panel.Source;
        _duration = source?.Duration ?? 1f;
        _fixedRange = source?.FixedTimeRange == true;
        _playhead = panel.Playhead.Value;

        _timeStep = CurveTicks.Step(_view.T1 - _view.T0, 12);
        Ticks(_timeTicks, _view.T0, _view.T1, _timeStep);
        Ticks(_valueTicks, _view.V0, _view.V1, CurveTicks.Step(_view.V1 - _view.V0, 7));

        var info = panel.Info;
        var rows = panel.Rows;
        _handles = false;
        if (info == null || source == null || !source.IsReady)
            return;

        var graph = Graph.Target;
        int active = System.Math.Clamp(panel.Channel.Value, 0, System.Math.Max(0, info.Channels.Length - 1));
        int selected = panel.Key.Value;
        int samples = System.Math.Clamp((int)(_view.Plot.width / 4f), 32, 360);

        // Inactive channels first so the active one draws on top.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int c = 0; c < info.Channels.Length; c++)
            {
                bool isActive = c == active;
                if ((pass == 1) != isActive || !panel.ChannelVisible(c))
                    continue;
                var line = _pool.Count > 0 ? _pool[^1] : new CurveLine();
                if (_pool.Count > 0)
                    _pool.RemoveAt(_pool.Count - 1);
                line.Points.Clear();
                line.Keys.Clear();
                var tint = CurveStudioPanel.ChannelColor(info, c);
                line.Active = isActive;
                line.Color = isActive ? tint : new color(tint.r, tint.g, tint.b, 0.55f);
                line.Width = isActive ? CurveWidth : FadedCurveWidth;
                line.Selected = isActive ? selected : -1;
                line.Hovered = graph != null && graph.HoverChannel == c ? graph.HoverKey : -1;

                var sampler = panel.Sampler;
                float carry = panel.SeedCarry(c, _view.T0);
                for (int i = 0; i <= samples; i++)
                {
                    float t = _view.T0 + (_view.T1 - _view.T0) * i / samples;
                    float v = sampler.Sample(c, t, ref carry);
                    if (!float.IsFinite(v))
                        continue;
                    line.Points.Add(new float2(_view.X(t), ClampY(_view.Y(v))));
                }
                for (int i = 0; i < rows.Count; i++)
                    line.Keys.Add(new float2(_view.X(rows[i].Time), ClampY(_view.Y(rows[i].Values[c]))));
                _lines.Add(line);

                if (isActive && info.AllowSmooth && selected >= 0 && selected < rows.Count
                    && rows[selected].Shape == KeyframeInterpolation.Cubic)
                {
                    _handles = true;
                    _handleKey = line.Keys[selected];
                    CurveGraph.HandlePoints(_view, rows[selected], c, out _handleIn, out _handleOut);
                }
            }
        }
    }

    private float ClampY(float y) => System.Math.Clamp(y, _view.Plot.yMin - 4000f, _view.Plot.yMax + 4000f);

    private static void Ticks(List<float> into, float min, float max, float step)
    {
        into.Clear();
        if (!(step > 0f))
            return;
        for (float t = CurveTicks.First(min, step); t <= max && into.Count < 200; t += step)
            into.Add(t);
    }

    // WORKER THREAD: triangles

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        if (!_valid)
            return;
        var submesh = renderData.GetSubmesh(null, null, MapMaterial);
        var mesh = submesh.Mesh;
        mesh.HasNormals = true;
        mesh.HasTangents = true;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);

        var full = _view.Full;
        var plot = _view.Plot;

        // Ruler strip, then the time outside the clip shaded.
        Quad(mesh, submesh, new float2(full.xMin, full.yMin), new float2(full.xMax, plot.yMin), RulerColor);
        float start = _view.X(0f);
        float end = _view.X(_fixedRange ? 1f : _duration);
        if (start > plot.xMin)
            Quad(mesh, submesh, new float2(plot.xMin, plot.yMin), new float2(System.Math.Min(start, plot.xMax), plot.yMax), OutsideColor);
        if (end < plot.xMax)
            Quad(mesh, submesh, new float2(System.Math.Max(end, plot.xMin), plot.yMin), new float2(plot.xMax, plot.yMax), OutsideColor);

        // Grid. Every fifth time line is heavier, the zero value line heavier still.
        foreach (float t in _timeTicks)
        {
            float x = _view.X(t);
            bool major = MathF.Abs(MathF.IEEERemainder(t / _timeStep, 5f)) < 1e-3f;
            float half = (major ? MajorWidth : GridWidth) * 0.5f;
            Quad(mesh, submesh, new float2(x - half, plot.yMin), new float2(x + half, plot.yMax), major ? MajorColor : GridColor);
            Quad(mesh, submesh, new float2(x - 1.4f, plot.yMin - 9f), new float2(x + 1.4f, plot.yMin), TickColor);
        }
        foreach (float v in _valueTicks)
        {
            float y = _view.Y(v);
            bool zero = MathF.Abs(v) < 1e-6f;
            float half = (zero ? ZeroWidth : GridWidth) * 0.5f;
            Quad(mesh, submesh, new float2(plot.xMin, y - half), new float2(plot.xMax, y + half), zero ? ZeroColor : GridColor);
        }
        if (end > plot.xMin && end < plot.xMax)
            Quad(mesh, submesh, new float2(end - 1.8f, plot.yMin), new float2(end + 1.8f, plot.yMax), EndColor);

        foreach (var line in _lines)
            Polyline(mesh, submesh, line.Points, line.Width, line.Color);

        foreach (var line in _lines)
        {
            for (int i = 0; i < line.Keys.Count; i++)
            {
                var p = line.Keys[i];
                if (p.x < plot.xMin - 12f || p.x > plot.xMax + 12f)
                    continue;
                bool selected = i == line.Selected;
                bool hovered = i == line.Hovered;
                float r = selected ? 12f : hovered ? 11f : line.Active ? 9f : 7f;
                if (selected)
                    Diamond(mesh, submesh, p, r + 3.5f, SelectedColor);
                Diamond(mesh, submesh, p, r, selected ? line.Color : hovered ? Lighten(line.Color) : line.Color);
            }
        }

        if (_handles)
        {
            Segment(mesh, submesh, _handleKey, _handleIn, 3f, HandleColor);
            Segment(mesh, submesh, _handleKey, _handleOut, 3f, HandleColor);
            Square(mesh, submesh, _handleIn, 7f, HandleColor);
            Square(mesh, submesh, _handleOut, 7f, HandleColor);
        }

        float playhead = _view.X(_playhead);
        if (playhead >= plot.xMin && playhead <= plot.xMax)
        {
            Quad(mesh, submesh, new float2(playhead - 1.8f, plot.yMin), new float2(playhead + 1.8f, plot.yMax), PlayheadColor);
            Quad(mesh, submesh, new float2(playhead - 7f, full.yMin + 2f), new float2(playhead + 7f, plot.yMin - 2f), PlayheadColor);
        }
    }

    private static color Lighten(color c) => new(MathF.Min(1f, c.r * 1.3f + 0.1f), MathF.Min(1f, c.g * 1.3f + 0.1f), MathF.Min(1f, c.b * 1.3f + 0.1f), 1f);

    // GEOMETRY. Canvas space is Y-up; quads go in top-left, top-right, bottom-right, bottom-left.

    private static void Quad(PhosMesh mesh, PhosTriangleSubmesh submesh, float2 min, float2 max, in color tint)
    {
        Corners(mesh, submesh, new float2(min.x, max.y), max, new float2(max.x, min.y), min, tint);
    }

    private static void Square(PhosMesh mesh, PhosTriangleSubmesh submesh, float2 center, float half, in color tint)
        => Quad(mesh, submesh, center - new float2(half, half), center + new float2(half, half), tint);

    private static void Diamond(PhosMesh mesh, PhosTriangleSubmesh submesh, float2 center, float radius, in color tint)
    {
        Corners(mesh, submesh,
            center + new float2(0f, radius),
            center + new float2(radius, 0f),
            center - new float2(0f, radius),
            center - new float2(radius, 0f), tint);
    }

    private static void Segment(PhosMesh mesh, PhosTriangleSubmesh submesh, float2 a, float2 b, float width, in color tint)
    {
        var d = b - a;
        float length = d.Length;
        if (length < 1e-4f)
            return;
        var perp = new float2(-d.y, d.x) / length * (width * 0.5f);
        Corners(mesh, submesh, a + perp, b + perp, b - perp, a - perp, tint);
    }

    private static void Corners(PhosMesh mesh, PhosTriangleSubmesh submesh, float2 tl, float2 tr, float2 br, float2 bl, in color tint)
    {
        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(4);
        Vertex(mesh, first, tl, new float2(0f, 1f), tint);
        Vertex(mesh, first + 1, tr, new float2(1f, 1f), tint);
        Vertex(mesh, first + 2, br, new float2(1f, 0f), tint);
        Vertex(mesh, first + 3, bl, new float2(0f, 0f), tint);
        submesh.AddQuadAsTriangles(first, first + 1, first + 2, first + 3);
    }

    // A connected ribbon: each point pushed out along the averaged normal, so segments share vertices and
    // the line keeps its width round bends.
    private static void Polyline(PhosMesh mesh, PhosTriangleSubmesh submesh, List<float2> points, float width, in color tint)
    {
        int n = points.Count;
        if (n < 2)
            return;
        float half = width * 0.5f;
        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(n * 2);
        float2 previous = float2.Zero;
        for (int i = 0; i < n; i++)
        {
            var p = points[i];
            var segment = i == n - 1 ? p - points[i - 1] : points[i + 1] - p;
            var dir = segment.LengthSquared > 1e-12f ? segment.Normalized : (previous.LengthSquared > 0f ? previous : new float2(1f, 0f));
            var join = dir;
            if (previous.LengthSquared > 0f)
            {
                var sum = dir + previous;
                if (sum.LengthSquared > 1e-12f)
                    join = sum.Normalized;
            }
            var perp = new float2(-join.y, join.x) * half;
            int vi = first + i * 2;
            Vertex(mesh, vi, p + perp, new float2(0f, 0f), tint);
            Vertex(mesh, vi + 1, p - perp, new float2(1f, 0f), tint);
            if (i < n - 1)
                submesh.AddQuadAsTriangles(vi, vi + 2, vi + 3, vi + 1);
            previous = dir;
        }
    }

    private static void Vertex(PhosMesh mesh, int index, float2 position, float2 uv, in color tint)
    {
        mesh.RawPositions[index] = new float3(position.x, position.y, 0f);
        mesh.RawNormals[index] = float3.Backward;
        mesh.RawTangents[index] = new float4(float3.Right, -1f);
        mesh.RawColors[index] = tint;
        mesh.SetUV(0, index, uv);
    }

    private static MaterialMap MapMaterial(GraphicsChunk.RenderData renderData,
        IAssetProvider<MaterialAsset>? baseMaterial, object? key, bool usingDefaultMaterial)
        => new(baseMaterial, key as IAssetProvider<MaterialPropertyBlockAsset>);
}
