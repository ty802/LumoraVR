// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Components.Audio;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

public enum WaveformStyle
{
    // The signal against time, the way an oscilloscope draws it.
    Waveform,

    // Left against right, which draws a line for a mono signal, a circle for a stereo one out of
    // phase, and a shape for everything between.
    Stereo,

    // Energy per frequency band, low on the left.
    Spectrum,
}

// A live picture of a sound.
//
// Sound is the one thing in a world with no shape of its own, and a player that is playing looks
// exactly like a player that is not. This is the part that moves: a ribbon in the local XY plane,
// rebuilt every frame from whatever an audio source is actually producing.
//
// Point it at any audio output - a clip player's, a voice's, a stream's - and it draws what that
// output is producing. Deliberately NOT the source: a stream's Read advances a cursor, so a meter
// reading one is not watching the audio, it is eating it, and the sound would arrive at the ear with
// holes in it wherever a picture was being drawn. The mixer keeps a copy of the block it last
// produced and this copies that. -xlinka
[ComponentCategory("Assets/Procedural Meshes")]
public class AudioWaveformMesh : ProceduralMesh
{
    // The OUTPUT to watch, not the source. Reading a source would advance its cursor and take the
    // audio out of the mixer; the output keeps a copy of the block it last produced for exactly this.
    public readonly SyncRef<Lumora.Core.Components.Audio.AudioOutput> Source;

    public readonly Sync<WaveformStyle> Style;

    // How many points across. More is smoother and costs a vertex each.
    [Range(8f, 512f, "0")]
    public readonly Sync<int> Resolution;

    public readonly Sync<float> Width;
    public readonly Sync<float> Height;

    // How thick the line is, in the same units.
    public readonly Sync<float> Thickness;

    // 0 holds the last picture, 1 follows instantly. Somewhere between is what reads as alive rather
    // than as noise.
    [Range(0f, 1f, "F2")]
    public readonly Sync<float> Smoothing;

    public readonly Sync<float> Gain;

    private const int MaxResolution = 512;
    private const int SampleWindow = 2048;

    private PhosTriangleSubmesh? _submesh;
    private int _builtResolution;
    private int _firstIndex;

    private readonly float[] _values = new float[MaxResolution];
    private readonly float[] _display = new float[MaxResolution];
    private readonly StereoSample[] _samples = new StereoSample[SampleWindow];
    private FourierTransform? _fft;
    private float[]? _real;
    private float[]? _imag;

    private int _resolution;
    private float _width, _height, _thickness;

    public AudioWaveformMesh()
    {
        Source = new SyncRef<Lumora.Core.Components.Audio.AudioOutput>(this);
        Style = new Sync<WaveformStyle>(this, WaveformStyle.Waveform);
        Resolution = new Sync<int>(this, 96);
        Width = new Sync<float>(this, 1f);
        Height = new Sync<float>(this, 0.25f);
        Thickness = new Sync<float>(this, 0.006f);
        Smoothing = new Sync<float>(this, 0.45f);
        Gain = new Sync<float>(this, 1f);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        SubscribeToChanges(Resolution);
        SubscribeToChanges(Width);
        SubscribeToChanges(Height);
        SubscribeToChanges(Thickness);
        SubscribeToChanges(Style);
    }

    // Sampled every frame rather than on a change, because the thing it is drawing changes every
    // frame by definition. Marking dirty here is what keeps the mesh following the sound.
    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (Sample())
            MarkChangeDirty();
    }

    private bool Sample()
    {
        int resolution = System.Math.Clamp(Resolution.Value, 8, MaxResolution);
        var output = Source.Target;

        int read = 0;
        if (output != null && !output.IsDestroyed)
            read = output.CopyMonitor(_samples);

        if (read <= 0)
        {
            // Fall quietly to a flat line rather than freezing on the last thing it heard.
            bool moved = false;
            for (int i = 0; i < resolution; i++)
            {
                if (MathF.Abs(_display[i]) > 1e-4f)
                    moved = true;
                _display[i] *= 0.85f;
            }
            return moved;
        }

        var window = _samples.AsSpan(0, read);

        switch (Style.Value)
        {
            case WaveformStyle.Spectrum: FillSpectrum(window, resolution); break;
            case WaveformStyle.Stereo: FillStereo(window, resolution); break;
            default: FillWaveform(window, resolution); break;
        }

        float gain = Gain.Value;
        float blend = 1f - System.Math.Clamp(Smoothing.Value, 0f, 0.99f);
        for (int i = 0; i < resolution; i++)
            _display[i] += (_values[i] * gain - _display[i]) * blend;

        return true;
    }

    // Peak per bucket rather than one sample in N: a peak cannot miss a transient, and a transient is
    // the part of a waveform anybody is looking at.
    private void FillWaveform(ReadOnlySpan<StereoSample> window, int resolution)
    {
        int per = System.Math.Max(1, window.Length / resolution);
        for (int i = 0; i < resolution; i++)
        {
            int start = i * per;
            float peak = 0f;
            for (int j = 0; j < per && start + j < window.Length; j++)
            {
                var s = window[start + j];
                float v = (s.left + s.right) * 0.5f;
                if (MathF.Abs(v) > MathF.Abs(peak))
                    peak = v;
            }
            _values[i] = peak;
        }
    }

    private void FillStereo(ReadOnlySpan<StereoSample> window, int resolution)
    {
        int per = System.Math.Max(1, window.Length / resolution);
        for (int i = 0; i < resolution; i++)
        {
            int start = i * per;
            float sum = 0f;
            int count = 0;
            for (int j = 0; j < per && start + j < window.Length; j++)
            {
                var s = window[start + j];
                sum += s.left - s.right;
                count++;
            }
            _values[i] = count > 0 ? sum / count : 0f;
        }
    }

    private void FillSpectrum(ReadOnlySpan<StereoSample> window, int resolution)
    {
        _fft ??= new FourierTransform(1024);
        _real ??= new float[1024];
        _imag ??= new float[1024];

        for (int i = 0; i < 1024; i++)
        {
            float w = _fft.WindowAt(i);
            var s = i < window.Length ? window[i] : default;
            _real[i] = (s.left + s.right) * 0.5f * w * w;
            _imag[i] = 0f;
        }
        _fft.Forward(_real, _imag);

        // Logarithmic across the buckets, because hearing is: a linear split spends most of the
        // picture on frequencies nobody can pick out.
        int bins = 512;
        for (int i = 0; i < resolution; i++)
        {
            float t0 = i / (float)resolution;
            float t1 = (i + 1) / (float)resolution;
            int b0 = (int)(MathF.Pow(bins, t0));
            int b1 = System.Math.Max(b0 + 1, (int)(MathF.Pow(bins, t1)));
            if (b1 > bins) b1 = bins;

            float sum = 0f;
            for (int b = b0; b < b1; b++)
                sum += MathF.Sqrt(_real[b] * _real[b] + _imag[b] * _imag[b]);
            float magnitude = sum / MathF.Max(1, b1 - b0);
            // Compress hard: a spectrum in linear amplitude is a spike and a flat line.
            _values[i] = MathF.Min(1f, MathF.Sqrt(magnitude * 8f));
        }
    }

    protected override void PrepareAssetUpdateData()
    {
        _resolution = System.Math.Clamp(Resolution.Value, 8, MaxResolution);
        _width = Width.Value;
        _height = Height.Value;
        _thickness = MathF.Max(Thickness.Value, 0.0005f);
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool rebuild = _submesh == null || _builtResolution != _resolution;
        uploadHint[MeshUploadHint.Flag.Geometry] = rebuild;

        if (rebuild)
        {
            mesh.Clear();
            mesh.HasNormals = true;
            mesh.HasTangents = true;
            mesh.HasUV0s = true;

            _submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(_submesh);

            // A ribbon: two vertices per point, two triangles per span.
            int vertices = _resolution * 2;
            mesh.IncreaseVertexCount(vertices);
            _firstIndex = mesh.VertexCount - vertices;

            for (int i = 0; i < _resolution - 1; i++)
            {
                int a = _firstIndex + i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                // Wound the way this engine wants its front faces, same as everything else in Phos.
                _submesh.AddTriangle(a, c, b);
                _submesh.AddTriangle(b, c, d);
            }

            _builtResolution = _resolution;
        }

        var positions = mesh.RawPositions;
        var normals = mesh.RawNormals;
        var tangents = mesh.RawTangents;
        var uvs = mesh.RawUV0s;

        var normal = float3.Backward;
        var tangent = new float4(1f, 0f, 0f, -1f);
        bool spectrum = Style.Value == WaveformStyle.Spectrum;
        float half = _thickness * 0.5f;

        for (int i = 0; i < _resolution; i++)
        {
            float t = _resolution > 1 ? i / (float)(_resolution - 1) : 0f;
            float x = (t - 0.5f) * _width;
            float v = System.Math.Clamp(_display[i], -1f, 1f);

            // A spectrum grows up from the floor; a waveform swings either side of the centre.
            float centre = spectrum
                ? (-_height * 0.5f) + MathF.Abs(v) * _height * 0.5f
                : v * _height * 0.5f;

            int a = _firstIndex + i * 2;
            int b = a + 1;
            positions[a] = new float3(x, centre - half, 0f);
            positions[b] = new float3(x, centre + half, 0f);
            normals[a] = normals[b] = normal;
            tangents[a] = tangents[b] = tangent;
            uvs[a] = new float2(t, 0f);
            uvs[b] = new float2(t, 1f);
        }
    }

    protected override void ClearMeshData()
    {
        _submesh = null;
        _builtResolution = 0;
    }
}
