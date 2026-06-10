// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.UI;

public sealed class LineGraphMesh : Graphic
{
    public readonly SyncRef<ValueGraphRecorder> Recorder;
    public readonly Sync<color> Color;
    public readonly Sync<float> Width;
    public readonly Sync<color> FillColor;
    public readonly Sync<bool> FillBelow;

    private float[]? _snapshot;
    private float2[]? _pts;
    private int _snapshotCount;
    private float _min;
    private float _max;
    private color _color;
    private color _fillColor;
    private float _width;
    private bool _fillBelow;
    private int _lastVersion = -1;

    public LineGraphMesh()
    {
        Recorder = new SyncRef<ValueGraphRecorder>(this);
        Color = new Sync<color>(this, color.White);
        Width = new Sync<float>(this, 1.5f);
        FillColor = new Sync<color>(this, new color(1f, 1f, 1f, 0.18f));
        FillBelow = new Sync<bool>(this, true);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    // The whole canvas re-uploads to the GPU on any change (one Godot surface rebuild per UI
    // element, ~tens of ms total), so a 20 Hz sparkline would hitch the entire dashboard every
    // frame. Cap how often a new sample redraws the graph; the recorder keeps sampling at full
    // rate, we just render fewer steps. Until the graph can live in its own mesh, this trades
    // smooth scrolling for a smooth dashboard. - xlinka
    private const double RedrawInterval = 1.0 / 3.0;
    private double _lastRedrawTime;

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var recorder = Recorder.Target;
        if (recorder == null)
            return;
        if (recorder.Version == _lastVersion)
            return;

        double now = Engine.Current?.Metrics?.TotalTime ?? 0.0;
        if (now - _lastRedrawTime < RedrawInterval)
            return;

        _lastRedrawTime = now;
        _lastVersion = recorder.Version;
        RectTransform?.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        _color = Color.Value;
        _fillColor = FillColor.Value;
        _width = Width.Value;
        _fillBelow = FillBelow.Value;

        var recorder = Recorder.Target;
        if (recorder == null)
        {
            _snapshotCount = 0;
            return;
        }

        int n = recorder.Count;
        if (_snapshot == null || _snapshot.Length < n)
            _snapshot = new float[n > 0 ? n : 1];
        for (int i = 0; i < n; i++)
            _snapshot[i] = recorder.GetSample(i);
        _snapshotCount = n;
        _min = recorder.RangeMin.Value;
        _max = recorder.RangeMax.Value;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null)
            return;

        var rect = rectTransform.LocalComputeRect;
        if (rect.IsEmpty)
            return;

        var clipRect = renderData.ClipRect;
        if (clipRect.HasValue && !rect.Overlaps(clipRect.Value))
            return;

        int n = _snapshotCount;
        if (n < 2 || _snapshot == null)
            return;

        var submesh = renderData.GetSubmesh(null, null, MapMaterial);
        var mesh = submesh.Mesh;
        PrepareMesh(mesh);

        float span = _max - _min;
        if (span < 0.001f)
            span = 0.001f;
        float dx = rect.width / (n - 1);
        float baseline = rect.yMin;

        if (_pts == null || _pts.Length < n)
            _pts = new float2[n];
        for (int i = 0; i < n; i++)
        {
            float x = rect.xMin + i * dx;
            float y = baseline + Clamp01((_snapshot[i] - _min) / span) * rect.height;
            _pts[i] = new float2(x, y);
        }

        if (_fillBelow)
        {
            for (int i = 0; i < n - 1; i++)
            {
                var a = _pts[i];
                var b = _pts[i + 1];
                AddQuad(mesh, submesh,
                    a, b,
                    new float2(b.x, baseline), new float2(a.x, baseline), in _fillColor);
            }
        }

        // connected polyline: offset each point along the mitre normal so segments share
        // vertices and the line keeps a constant perpendicular width on slopes. - xlinka
        float half = _width * 0.5f;
        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(n * 2);
        float2 prevDir = float2.Zero;
        for (int i = 0; i < n; i++)
        {
            var p = _pts[i];
            float2 seg = i == n - 1 ? new float2(p.x - _pts[i - 1].x, p.y - _pts[i - 1].y)
                                    : new float2(_pts[i + 1].x - p.x, _pts[i + 1].y - p.y);
            float2 dir = seg.LengthSquared > 1e-12f ? seg.Normalized
                       : (prevDir.LengthSquared > 0f ? prevDir : new float2(1f, 0f));
            if (prevDir.LengthSquared > 0f)
            {
                var sum = new float2(dir.x + prevDir.x, dir.y + prevDir.y);
                if (sum.LengthSquared > 1e-12f)
                    dir = sum.Normalized;
            }
            var perp = new float2(-dir.y, dir.x);
            float t = (float)i / (n - 1);
            int vi = first + i * 2;
            SetLineVertex(mesh, vi, new float2(p.x + perp.x * half, p.y + perp.y * half), new float2(0f, t), in _color);
            SetLineVertex(mesh, vi + 1, new float2(p.x - perp.x * half, p.y - perp.y * half), new float2(1f, t), in _color);
            if (i < n - 1)
                submesh.AddQuadAsTriangles(vi, vi + 2, vi + 3, vi + 1);
            prevDir = dir;
        }
    }

    private static void SetLineVertex(PhosMesh mesh, int index, in float2 pos, in float2 uv, in color tint)
    {
        mesh.RawPositions[index] = new float3(pos.x, pos.y, 0f);
        mesh.RawNormals[index] = float3.Backward;
        mesh.RawTangents[index] = new float4(float3.Right, -1f);
        mesh.RawColors[index] = tint;
        mesh.SetUV(0, index, uv);
    }

    public override bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }

    private static void AddQuad(PhosMesh mesh, PhosTriangleSubmesh submesh,
        in float2 tl, in float2 tr, in float2 br, in float2 bl, in color tint)
    {
        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(4);

        mesh.RawPositions[first] = new float3(tl.x, tl.y, 0f);
        mesh.RawPositions[first + 1] = new float3(tr.x, tr.y, 0f);
        mesh.RawPositions[first + 2] = new float3(br.x, br.y, 0f);
        mesh.RawPositions[first + 3] = new float3(bl.x, bl.y, 0f);

        for (int i = 0; i < 4; i++)
        {
            mesh.RawNormals[first + i] = float3.Backward;
            mesh.RawTangents[first + i] = new float4(float3.Right, -1f);
            mesh.RawColors[first + i] = tint;
        }

        mesh.SetUV(0, first, new float2(0f, 0f));
        mesh.SetUV(0, first + 1, new float2(1f, 0f));
        mesh.SetUV(0, first + 2, new float2(1f, 1f));
        mesh.SetUV(0, first + 3, new float2(0f, 1f));

        submesh.AddQuadAsTriangles(first, first + 1, first + 2, first + 3);
    }

    private static void PrepareMesh(PhosMesh mesh)
    {
        mesh.HasNormals = true;
        mesh.HasTangents = true;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
    }

    private static MaterialMap MapMaterial(GraphicsChunk.RenderData renderData,
        IAssetProvider<MaterialAsset>? baseMaterial, object? key, bool usingDefaultMaterial)
    {
        return new MaterialMap(baseMaterial, key as IAssetProvider<MaterialPropertyBlockAsset>);
    }

    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }
}
