// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Audio;
using Lumora.Core.Assets;
using Lumora.Core.Components.Audio;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.UI;

// The last moments of a sound drawn as a line across its rect: the oldest sample at the left edge, the newest
// at the right, silence along the middle and full scale at the top and bottom. HistoryLength is how many
// seconds the line spans, Points how many places along it are measured (evenly, between the samples either
// side), Width the line's thickness in canvas units. A rect whose anchors run backwards draws it mirrored.
//
// Source is what to watch: an AudioOutput, or anything an output plays, whose output is found beside it (its
// own slot, then the slot above). The sound is copied block by block as the mixer makes it (OutputMonitorTap),
// so the line never takes audio from the speaker and never skips a block between frames, and every machine
// draws it from the sound it hears itself; nothing about it crosses the wire.
//
// It re-meshes every frame while sound comes in, so it belongs on a slot with a GraphicChunkRoot of its own,
// or everything sharing its chunk re-meshes with it. -xlinka
[ComponentCategory("UI/Helio/Graphics")]
public sealed class AudioWaveformGraphic : Graphic
{
    public readonly SyncRef<Component> Source;
    [Range(0.01f, 1f, "0.00")]
    public readonly Sync<float> HistoryLength;
    public readonly Sync<int> Points;
    public readonly Sync<float> Width;
    public readonly Sync<colorHDR> Color;
    public readonly Sync<float2> UVScale;
    // Empty draws with the canvas's own material.
    public readonly AssetRef<MaterialAsset> Material;

    private const int MaxPoints = 4096;
    private const int DrainLength = 2048;
    private const double StaleSeconds = 0.25;
    private const double SearchInterval = 1.0;

    private readonly StereoSample[] _drain = new StereoSample[DrainLength];
    private OutputMonitorTap? _tap;
    private AudioOutput? _output;
    private Component? _searchedFor;
    private double _nextSearch;
    private double _lastListen = double.NegativeInfinity;

    // The history: mono samples in a ring, _head the next to write. Zeros until it has filled once.
    private float[] _history = Array.Empty<float>();
    private int _head;
    // Samples written since the last that was not silence.
    private int _quietRun = int.MaxValue;

    // Snapshot for the emit pass, taken on the main thread.
    private float[] _snapshot = Array.Empty<float>();
    private float2[] _line = Array.Empty<float2>();
    private int _points;
    private float _width;
    private color _color;
    private float2 _uvScale;
    private IAssetProvider<MaterialAsset>? _material;

    public AudioWaveformGraphic()
    {
        Source = new SyncRef<Component>(this);
        HistoryLength = new Sync<float>(this, 0.025f);
        Points = new Sync<int>(this, 256);
        Width = new Sync<float>(this, 0.2f);
        Color = new Sync<colorHDR>(this, colorHDR.White);
        UVScale = new Sync<float2>(this, float2.One);
        Material = new AssetRef<MaterialAsset>(this);
    }

    public override bool RequiresPreGraphicsCompute => false;

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkGraphicDirty();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (!IsDestroyed && Listen())
            RectTransform?.MarkGraphicDirty();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        StopListening();
    }

    public override void OnDestroy()
    {
        StopListening();
        base.OnDestroy();
    }

    // Moves whatever the output played since the last frame into the history. True when the picture moved.
    private bool Listen()
    {
        var simulator = Engine.Current?.AudioSystem?.Simulator;
        var voice = simulator != null ? FindOutput()?.Voice : null;
        if (simulator == null || voice == null)
        {
            _tap?.Skip();
            return Quieten();
        }
        if (_tap == null || !ReferenceEquals(_tap.Simulator, simulator))
        {
            _tap?.Dispose();
            _tap = new OutputMonitorTap(simulator, DrainLength * 4);
        }
        Resize(simulator.SampleRate);

        double now = World?.Time.TotalTime ?? 0.0;
        if (now - _lastListen > StaleSeconds)
            _tap.Skip();
        _lastListen = now;
        _tap.Voice = voice;

        // A stopped source still sends blocks of silence; once the whole history is silence the line is
        // flat and stays drawn as it is.
        bool wasFlat = _quietRun >= _history.Length;
        bool moved = false;
        int read;
        while ((read = _tap.Read(_drain)) > 0)
        {
            var history = _history;
            for (int i = 0; i < read; i++)
            {
                float mono = (_drain[i].left + _drain[i].right) * 0.5f;
                history[_head] = mono;
                if (++_head == history.Length)
                    _head = 0;
                if (mono != 0f)
                {
                    _quietRun = 0;
                    moved = true;
                }
                else if (_quietRun < int.MaxValue)
                {
                    _quietRun++;
                }
            }
            moved |= !wasFlat;
        }
        return moved;
    }

    // With nothing to watch the line lies flat, once.
    private bool Quieten()
    {
        if (_quietRun >= _history.Length)
            return false;
        Array.Clear(_history);
        _quietRun = int.MaxValue;
        return true;
    }

    private void Resize(int sampleRate)
    {
        float seconds = System.Math.Clamp(float.IsFinite(HistoryLength.Value) ? HistoryLength.Value : 0.025f, 0.01f, 1f);
        int length = System.Math.Max(2, (int)MathF.Ceiling(seconds * sampleRate));
        if (_history.Length == length)
            return;
        _history = new float[length];
        _head = 0;
        _quietRun = int.MaxValue;
    }

    // The output playing Source: Source itself, or one beside it. Kept while it still plays it, looked for
    // again at most once a second while there is none.
    private AudioOutput? FindOutput()
    {
        var source = Source.Target;
        if (source == null || source.IsDestroyed)
            return _output = null;
        if (source is AudioOutput direct)
            return _output = direct.IsDestroyed ? null : direct;
        if (_output != null && !_output.IsDestroyed && ReferenceEquals(_output.Source.Target, source))
            return _output;
        _output = null;
        double now = World?.Time.TotalTime ?? 0.0;
        if (ReferenceEquals(source, _searchedFor) && now < _nextSearch)
            return null;
        _searchedFor = source;
        _nextSearch = now + SearchInterval;
        for (var slot = source.Slot; slot != null; slot = ReferenceEquals(slot, source.Slot) ? slot.Parent : null)
        {
            foreach (var component in slot.Components)
            {
                if (component is AudioOutput output && !output.IsDestroyed && ReferenceEquals(output.Source.Target, source))
                    return _output = output;
            }
        }
        return null;
    }

    private void StopListening()
    {
        _tap?.Dispose();
        _tap = null;
        _output = null;
        _searchedFor = null;
    }

    // Main thread: the history measured at each point, oldest first.
    public override void PrepareCompute()
    {
        _points = System.Math.Clamp(Points.Value, 2, MaxPoints);
        _width = float.IsFinite(Width.Value) ? Width.Value : 0f;
        var c = Color.Value;
        _color = new color(c.r, c.g, c.b, c.a);
        _uvScale = UVScale.Value;
        _material = Material.Target;
        if (_snapshot.Length != _points)
        {
            _snapshot = new float[_points];
            _line = new float2[_points];
        }

        var history = _history;
        int length = history.Length;
        if (length < 2)
        {
            Array.Clear(_snapshot);
            return;
        }
        double step = (double)length / _points;
        for (int i = 0; i < _points; i++)
        {
            double at = i * step;
            int index = (int)at;
            float between = (float)(at - index);
            float a = history[(_head + index) % length];
            float b = history[(_head + System.Math.Min(index + 1, length - 1)) % length];
            _snapshot[i] = a + (b - a) * between;
        }
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null || _points < 2)
            return;
        var rect = rectTransform.LocalComputeRect;
        if (rect.width == 0f || rect.height == 0f)
            return;

        var submesh = renderData.GetSubmesh(_material, null, MapMaterial);
        var mesh = submesh.Mesh;
        mesh.HasNormals = true;
        mesh.HasTangents = true;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);

        int n = _points;
        float last = n - 1;
        for (int i = 0; i < n; i++)
        {
            float amplitude = float.IsFinite(_snapshot[i]) ? _snapshot[i] : 0f;
            _line[i] = new float2(rect.xMin + rect.width * (i / last), rect.yMin + rect.height * (amplitude * 0.5f + 0.5f));
        }

        // A ribbon two vertices wide, each pair across the line's direction there, which is the next segment's
        // eased into the one before so the joins do not pinch.
        float half = _width * 0.5f;
        float travelled = 0f;
        float2 before = float2.Zero;
        int first = mesh.VertexCount;
        mesh.IncreaseVertexCount(n * 2);
        for (int i = 0; i < n; i++)
        {
            var point = _line[i];
            var segment = i == n - 1 ? point - _line[i - 1] : _line[i + 1] - point;
            float length = segment.Length;
            var direction = length > 1e-12f ? segment / length : (before.LengthSquared > 0f ? before : new float2(1f, 0f));
            if (before.LengthSquared > 0f)
            {
                var eased = direction + before;
                if (eased.LengthSquared > 1e-12f)
                    direction = eased.Normalized;
            }
            var across = new float2(-direction.y, direction.x) * half;
            int vertex = first + i * 2;
            Put(mesh, vertex, point + across, new float2(0f, travelled) * _uvScale);
            Put(mesh, vertex + 1, point - across, new float2(1f, travelled) * _uvScale);
            if (i < n - 1)
                submesh.AddQuadAsTriangles(vertex, vertex + 2, vertex + 3, vertex + 1);
            travelled += length;
            before = direction;
        }
    }

    private void Put(PhosMesh mesh, int index, in float2 position, in float2 uv)
    {
        mesh.RawPositions[index] = new float3(position.x, position.y, 0f);
        mesh.RawNormals[index] = float3.Backward;
        mesh.RawTangents[index] = new float4(float3.Right, -1f);
        mesh.RawColors[index] = _color;
        mesh.SetUV(0, index, uv);
    }

    public override bool IsPointInside(in float2 point)
    {
        if (RectTransform == null)
            return false;
        var rect = RectTransform.LocalComputeRect;
        return Rect.FromMinMax(float2.Min(rect.Min, rect.Max), float2.Max(rect.Min, rect.Max)).Contains(point);
    }

    private static MaterialMap MapMaterial(GraphicsChunk.RenderData renderData,
        IAssetProvider<MaterialAsset>? baseMaterial, object? key, bool usingDefaultMaterial)
        => new MaterialMap(baseMaterial);
}
