// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Lumora.Core.Math;

namespace Lumora.Audio;

// PUBLIC SURFACE OF THE MIXER. The world side (LumoraCore) codes against exactly this; the bodies
// are the mixer pass's job. Rules that hold across every implementation:
//   - One simulator per engine. It owns the audio thread. Nothing here is called by the audio thread
//     except IAudioDataSource.Read, IAudioDspFilter.Process, IAudioDeviceOutput.Write/FramesWritable
//     and the RenderStarted event + pre-render tasks.
//   - Spaces, listeners and outputs are created/removed and updated from the world thread. Updates
//     are staged and copied by the audio thread at the top of a render, so a torn read inside one
//     frame is impossible and no world-thread call ever waits on the mixer.
//   - The audio thread renders one FrameSize block whenever PrimaryOutput.Device has room for it,
//     otherwise it sleeps a millisecond. A render: run pre-render tasks, raise RenderStarted, for
//     every active space: pull every registered output (culled to MaxActiveOutputs by Priority then
//     loudness) once and run its own effect chain on it, then for every listener: sum those outputs
//     with their shape/spatialisation applied, run the listener's effect chain on the sum, add to the
//     device mix; scale by PrimaryOutput.Volume, soft-clip, Write to the device. Outputs whose source
//     IsActive is false cost nothing once their own chain has rung out.
//   - Time: AudioFrameIndex advances by one per rendered frame, DspTime by FrameSize / SampleRate.
// -xlinka
public sealed class AudioSimulator : IDisposable
{
    public const int DefaultSampleRate = 48000;
    public const int DefaultFrameSize = 512;

    private readonly object _structureLock = new object();
    private readonly List<AudioSpace> _spaces = new List<AudioSpace>();
    private readonly List<AudioSpace> _spaceAdds = new List<AudioSpace>();
    private readonly List<AudioSpace> _spaceRemoves = new List<AudioSpace>();
    private readonly ConcurrentQueue<Action> _preRenderTasks = new ConcurrentQueue<Action>();

    private Thread? _thread;
    private volatile bool _stopRequested;
    // False means no audio thread can be touching the live lists, so a world-thread call may apply
    // its own pending work instead of leaving it queued forever in a headless or pre-Start engine.
    private volatile bool _renderThreadLive;
    private volatile int _stagedFrameSize;
    private int _droppedFrames;
    private int _deviceUnderruns;
    private long _nonFiniteSamples;

    public AudioSimulator(int sampleRate = DefaultSampleRate, int frameSize = DefaultFrameSize)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (frameSize <= 0) throw new ArgumentOutOfRangeException(nameof(frameSize));
        SampleRate = sampleRate;
        FrameSize = frameSize;
        InvSampleRate = 1.0 / sampleRate;
        _stagedFrameSize = frameSize;
        PrimaryOutput = new AudioDeviceOutput(this);
        MaxActiveOutputs = 64;
    }

    public int SampleRate { get; }
    public double InvSampleRate { get; }
    public int FrameSize { get; private set; }

    // Voice limit: outputs beyond it are parked for the frame, lowest priority first.
    public int MaxActiveOutputs { get; set; }

    public long AudioFrameIndex { get; private set; }
    public double DspTime { get; private set; }
    public bool IsRunning { get; private set; }
    public AudioDeviceOutput PrimaryOutput { get; }
    public AudioSimulatorStats Stats { get; private set; }

    // Raised on the audio thread at the top of every render, after pre-render tasks.
    public event Action<AudioSimulator>? RenderStarted;

    public AudioSpace AddSpace(bool startActive)
    {
        AudioSpace space = new AudioSpace(this) { IsActive = startActive };
        lock (_structureLock)
        {
            _spaceAdds.Add(space);
            if (!_renderThreadLive) DrainSpacesLocked();
        }
        return space;
    }

    public void RemoveSpace(AudioSpace space)
    {
        if (space == null) return;
        lock (_structureLock)
        {
            _spaceRemoves.Add(space);
            if (!_renderThreadLive) DrainSpacesLocked();
        }
    }

    public void ActivateSpace(AudioSpace space)
    {
        if (space != null) space.IsActive = true;
    }

    public void DeactivateSpace(AudioSpace space)
    {
        if (space != null) space.IsActive = false;
    }

    // Takes effect at the next render; buffers are reallocated on the audio thread.
    public void SetFrameSize(int frameSize)
    {
        if (frameSize <= 0) throw new ArgumentOutOfRangeException(nameof(frameSize));
        _stagedFrameSize = frameSize;
        if (!_renderThreadLive && frameSize != FrameSize) ApplyFrameSize(frameSize);
    }

    // Runs once on the audio thread before the next render. For handing the mixer something that
    // must not be touched from two threads (a buffer swap, a stream reset).
    public void SchedulePreRenderTask(Action task)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        _preRenderTasks.Enqueue(task);
    }

    public void Start()
    {
        lock (_structureLock)
        {
            if (IsRunning) return;
            _stopRequested = false;
            IsRunning = true;
            _renderThreadLive = true;
            _thread = new Thread(PumpLoop)
            {
                Name = "Lumora Audio",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_structureLock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _stopRequested = true;
            thread = _thread;
            _thread = null;
        }

        if (thread != null) thread.Join(1000);
        _renderThreadLive = false;

        // Anything queued while the thread was on its way out still has to land, or a world that
        // shut down mid-frame would leave voices registered against a dead simulator.
        lock (_structureLock) { DrainSpacesLocked(); }
        for (int i = 0; i < _spaces.Count; i++) _spaces[i].ApplyPending();
    }

    public void Dispose()
    {
        Stop();
        PrimaryOutput.Device = null;
        lock (_structureLock)
        {
            _spaceAdds.Clear();
            _spaceRemoves.Clear();
            _spaces.Clear();
        }
        RenderStarted = null;
    }

    private void PumpLoop()
    {
        while (!_stopRequested)
        {
            IAudioDeviceOutput? device = PrimaryOutput.Device;
            if (device == null)
            {
                long started = Stopwatch.GetTimestamp();
                RenderFrame(null);
                double budget = FrameSize * InvSampleRate;
                double spent = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
                int sleep = (int)((budget - spent) * 1000.0);
                if (sleep > 0) Thread.Sleep(sleep);
                else Thread.Yield();
                continue;
            }

            int writable;
            try { writable = device.FramesWritable; }
            catch { writable = 0; }

            if (writable >= FrameSize)
            {
                // Room for two whole frames means the device drained past our fill point while we
                // were away. It is the only underrun signal the seam gives us. -xlinka
                if (writable >= FrameSize * 2 && AudioFrameIndex > 8) _deviceUnderruns++;
                RenderFrame(device);
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private void RenderFrame(IAudioDeviceOutput? device)
    {
        long started = Stopwatch.GetTimestamp();

        while (_preRenderTasks.TryDequeue(out Action? task))
        {
            try { task(); }
            catch { }
        }

        int staged = _stagedFrameSize;
        if (staged != FrameSize) ApplyFrameSize(staged);

        lock (_structureLock) { DrainSpacesLocked(); }

        try { RenderStarted?.Invoke(this); }
        catch { }

        int frame = FrameSize;
        StereoSample[] mix = PrimaryOutput.Mix;
        Array.Clear(mix, 0, frame);

        int active = 0;
        int culled = 0;
        int listeners = 0;
        for (int i = 0; i < _spaces.Count; i++)
        {
            AudioSpace space = _spaces[i];
            space.ApplyPending();
            if (!space.IsActive) continue;
            space.Render(frame, ref active, ref culled, ref listeners);
        }

        float devicePeak = 0f;
        if (device != null)
        {
            _nonFiniteSamples += SampleBuffers.Sanitize(mix.AsSpan(0, frame));
            Spatializer.ApplyMaster(mix.AsSpan(0, frame), PrimaryOutput.Volume);
            for (int i = 0; i < frame; i++)
            {
                float a = mix[i].AbsoluteAmplitude;
                if (a > devicePeak) devicePeak = a;
            }
            try { device.Write(new ReadOnlySpan<StereoSample>(mix, 0, frame)); }
            catch { }
        }

        AudioFrameIndex++;
        DspTime += frame * InvSampleRate;

        double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs > frame * 1000.0 * InvSampleRate) _droppedFrames++;

        Stats = new AudioSimulatorStats
        {
            RenderedFrames = AudioFrameIndex,
            DroppedFrames = _droppedFrames,
            ActiveOutputs = active,
            CulledOutputs = culled,
            ActiveListeners = listeners,
            LastRenderMilliseconds = elapsedMs,
            DeviceUnderruns = _deviceUnderruns,
            LastDevicePeak = devicePeak,
            NonFiniteSamples = _nonFiniteSamples,
        };
    }

    private void ApplyFrameSize(int frameSize)
    {
        FrameSize = frameSize;
        PrimaryOutput.EnsureBuffer(frameSize);
        for (int i = 0; i < _spaces.Count; i++) _spaces[i].EnsureBuffers(frameSize);
    }

    private void DrainSpacesLocked()
    {
        for (int i = 0; i < _spaceAdds.Count; i++)
        {
            AudioSpace space = _spaceAdds[i];
            if (!_spaces.Contains(space)) _spaces.Add(space);
        }
        _spaceAdds.Clear();

        for (int i = 0; i < _spaceRemoves.Count; i++) _spaces.Remove(_spaceRemoves[i]);
        _spaceRemoves.Clear();
    }

    internal bool RenderThreadLive => _renderThreadLive;
}

// Where the mixed result goes. Device null = render into nothing (headless), which still advances
// time so clip positions keep meaning something.
public sealed class AudioDeviceOutput
{
    internal AudioDeviceOutput(AudioSimulator simulator)
    {
        Simulator = simulator;
        Mix = new StereoSample[System.Math.Max(1, simulator.FrameSize)];
    }

    public AudioSimulator Simulator { get; }

    // Master gain applied last, before clipping.
    public float Volume { get; set; } = 1f;

    // Swapping devices Stops the old and Starts the new on the caller's thread; the audio thread
    // sees the swap at its next render.
    public IAudioDeviceOutput? Device
    {
        get => Volatile.Read(ref _device);
        set
        {
            lock (_deviceLock)
            {
                IAudioDeviceOutput? previous = _device;
                if (ReferenceEquals(previous, value)) return;
                if (previous != null)
                {
                    try { previous.Stop(); }
                    catch { }
                }
                if (value != null)
                {
                    try { value.Start(); }
                    catch { }
                }
                // Published last so the audio thread never writes into a device that is not started.
                Volatile.Write(ref _device, value);
            }
        }
    }

    private IAudioDeviceOutput? _device;
    private readonly object _deviceLock = new object();

    internal StereoSample[] Mix;

    internal void EnsureBuffer(int frameSize)
    {
        if (Mix.Length != frameSize) Mix = new StereoSample[System.Math.Max(1, frameSize)];
    }
}

// One world's worth of audio. Inactive spaces are skipped by the render entirely (a background
// world is silent but its outputs keep their state).
public sealed class AudioSpace
{
    internal AudioSpace(AudioSimulator simulator) { Simulator = simulator; }

    private readonly object _lock = new object();
    private readonly List<AudioListenerVoice> _listeners = new List<AudioListenerVoice>();
    private readonly List<AudioOutputVoice> _outputs = new List<AudioOutputVoice>();
    private readonly List<AudioListenerVoice> _listenerAdds = new List<AudioListenerVoice>();
    private readonly List<AudioListenerVoice> _listenerRemoves = new List<AudioListenerVoice>();
    private readonly List<AudioOutputVoice> _outputAdds = new List<AudioOutputVoice>();
    private readonly List<AudioOutputVoice> _outputRemoves = new List<AudioOutputVoice>();

    private long[] _cullKeys = Array.Empty<long>();
    private int[] _cullSlots = Array.Empty<int>();
    private AudioOutputVoice[] _rendered = Array.Empty<AudioOutputVoice>();
    private volatile bool _isActive;

    public AudioSimulator Simulator { get; }

    public bool IsActive
    {
        get => _isActive;
        internal set => _isActive = value;
    }

    public AudioListenerVoice AddListener()
    {
        AudioListenerVoice listener = new AudioListenerVoice(this);
        lock (_lock)
        {
            _listenerAdds.Add(listener);
            if (!Simulator.RenderThreadLive) ApplyPendingLocked();
        }
        return listener;
    }

    public void RemoveListener(AudioListenerVoice listener)
    {
        if (listener == null) return;
        lock (_lock)
        {
            _listenerRemoves.Add(listener);
            if (!Simulator.RenderThreadLive) ApplyPendingLocked();
        }
    }

    public AudioOutputVoice AddOutput()
    {
        AudioOutputVoice output = new AudioOutputVoice(this);
        lock (_lock)
        {
            _outputAdds.Add(output);
            if (!Simulator.RenderThreadLive) ApplyPendingLocked();
        }
        return output;
    }

    public void RemoveOutput(AudioOutputVoice output)
    {
        if (output == null) return;
        lock (_lock)
        {
            _outputRemoves.Add(output);
            if (!Simulator.RenderThreadLive) ApplyPendingLocked();
        }
    }

    internal void ApplyPending()
    {
        lock (_lock) { ApplyPendingLocked(); }
    }

    private void ApplyPendingLocked()
    {
        bool changed = false;

        for (int i = 0; i < _listenerAdds.Count; i++)
        {
            AudioListenerVoice listener = _listenerAdds[i];
            if (_listeners.Contains(listener)) continue;
            _listeners.Add(listener);
            changed = true;
        }
        _listenerAdds.Clear();

        for (int i = 0; i < _outputAdds.Count; i++)
        {
            AudioOutputVoice output = _outputAdds[i];
            if (_outputs.Contains(output)) continue;
            _outputs.Add(output);
            changed = true;
        }
        _outputAdds.Clear();

        for (int i = 0; i < _listenerRemoves.Count; i++)
        {
            AudioListenerVoice listener = _listenerRemoves[i];
            if (!_listeners.Remove(listener)) continue;
            changed = true;
            // The per-pair ramp state is keyed by listener, so a removed ear has to be dropped from
            // every output or the dictionary grows for the life of the session.
            for (int o = 0; o < _outputs.Count; o++) _outputs[o].ForgetListener(listener);
        }
        _listenerRemoves.Clear();

        for (int i = 0; i < _outputRemoves.Count; i++)
        {
            if (_outputs.Remove(_outputRemoves[i])) changed = true;
        }
        _outputRemoves.Clear();

        if (!changed) return;

        if (_cullKeys.Length < _outputs.Count)
        {
            int capacity = System.Math.Max(16, _outputs.Count * 2);
            _cullKeys = new long[capacity];
            _cullSlots = new int[capacity];
            _rendered = new AudioOutputVoice[capacity];
        }

        int frame = Simulator.FrameSize;
        for (int i = 0; i < _listeners.Count; i++) _listeners[i].EnsureBuffers(frame);
        for (int i = 0; i < _outputs.Count; i++) _outputs[i].EnsureBuffers(frame);
    }

    internal void EnsureBuffers(int frameSize)
    {
        for (int i = 0; i < _listeners.Count; i++) _listeners[i].EnsureBuffers(frameSize);
        for (int i = 0; i < _outputs.Count; i++) _outputs[i].EnsureBuffers(frameSize);
    }

    // Order matters: every source is pulled before any listener buffer is cleared, so a source that
    // reads a listener's CurrentBuffer gets last frame's finished mix instead of a half-built one.
    internal void Render(int frame, ref int activeOutputs, ref int culledOutputs, ref int activeListeners)
    {
        for (int i = 0; i < _listeners.Count; i++) _listeners[i].ApplyStaged();
        for (int i = 0; i < _outputs.Count; i++) _outputs[i].ApplyStaged();

        AudioListenerVoice? dopplerListener = null;
        for (int i = 0; i < _listeners.Count; i++)
        {
            if (_listeners[i].Device == null) continue;
            dopplerListener = _listeners[i];
            break;
        }

        int candidates = 0;
        for (int i = 0; i < _outputs.Count; i++)
        {
            AudioOutputVoice output = _outputs[i];
            output.BeginFrame();
            if (!output.HasActiveSource) continue;
            // Priority in the high half, audibility bits in the low half: one long sorts the pair
            // lexicographically with no comparer object and no boxing.
            _cullKeys[candidates] = ((long)output.Priority << 32) | (uint)BitConverter.SingleToInt32Bits(output.Audibility(_listeners));
            _cullSlots[candidates] = i;
            candidates++;
        }

        int limit = System.Math.Max(0, Simulator.MaxActiveOutputs);
        int take = System.Math.Min(candidates, limit);
        if (take < candidates) Array.Sort(_cullKeys, _cullSlots, 0, candidates);

        int rendered = 0;
        for (int i = candidates - take; i < candidates; i++) _rendered[rendered++] = _outputs[_cullSlots[i]];

        for (int i = 0; i < rendered; i++) _rendered[i].PullFrame(Simulator, frame, dopplerListener);

        for (int i = 0; i < _listeners.Count; i++)
        {
            AudioListenerVoice listener = _listeners[i];
            listener.BeginFrame(frame);
            for (int o = 0; o < rendered; o++)
            {
                AudioOutputVoice output = _rendered[o];
                if (output.Excludes(listener)) continue;
                output.MixInto(listener, frame);
            }
            listener.FinishFrame(Simulator.SampleRate, frame);
        }

        activeOutputs += rendered;
        culledOutputs += candidates - take;
        activeListeners += _listeners.Count;
    }
}

// An ear. Only listeners bound to a device produce sound; an unbound listener still mixes so its
// CurrentBuffer can be read back as a source. -xlinka
public sealed class AudioListenerVoice
{
    internal AudioListenerVoice(AudioSpace space)
    {
        Space = space;
        _frame = System.Math.Max(1, space.Simulator.FrameSize);
        _wet = new StereoSample[_frame];
        _dry = new StereoSample[_frame];
    }

    private readonly object _paramLock = new object();
    private readonly List<IAudioDspFilter> _stagedEffects = new List<IAudioDspFilter>();
    private readonly List<IAudioDspFilter> _effects = new List<IAudioDspFilter>();

    private AudioListenerParams _staged = AudioListenerParams.Default;
    private AudioListenerParams _current = AudioListenerParams.Default;
    private bool _paramsDirty;
    private bool _effectsDirty;
    private StereoSample[] _wet;
    private StereoSample[] _dry;
    private int _frame;

    public AudioSpace Space { get; }
    public bool IsBoundToDevice => Device != null;
    public AudioDeviceOutput? Device { get; private set; }

    // Read off the log when a listener that received gains still produced nothing at the device.

    public void Update(in AudioListenerParams parameters)
    {
        lock (_paramLock)
        {
            _staged = parameters;
            _paramsDirty = true;
        }
    }

    // A reference write is atomic and nothing downstream reads it twice in a frame, so the bind
    // lands immediately rather than a render later; the caller can read Device back straight away.
    public void BindToDevice(AudioDeviceOutput? device) => Device = device;

    // Chain applied in order to this listener's mix; null or empty = dry.
    public void SetEffects(IReadOnlyList<IAudioDspFilter>? effects)
    {
        lock (_paramLock)
        {
            _stagedEffects.Clear();
            if (effects != null)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    IAudioDspFilter filter = effects[i];
                    if (filter != null) _stagedEffects.Add(filter);
                }
            }
            _effectsDirty = true;
        }
    }

    // The last rendered frame for this listener, audio-thread owned. Valid to read from a source's
    // Read during the same render only.
    public ReadOnlySpan<StereoSample> CurrentBuffer
    {
        get
        {
            // Array and length are two reads; a frame-size change between them would hand out a
            // span past the end of the new buffer, so the length is clamped to what is really there.
            StereoSample[] buffer = _wet;
            int count = _frame;
            return new ReadOnlySpan<StereoSample>(buffer, 0, count < buffer.Length ? count : buffer.Length);
        }
    }

    internal ref readonly AudioListenerParams CurrentParams => ref _current;

    internal void ApplyStaged()
    {
        lock (_paramLock)
        {
            if (_paramsDirty)
            {
                _current = _staged;
                _paramsDirty = false;
            }
            if (_effectsDirty)
            {
                _effects.Clear();
                for (int i = 0; i < _stagedEffects.Count; i++) _effects.Add(_stagedEffects[i]);
                _effectsDirty = false;
            }
        }
    }

    internal void EnsureBuffers(int frameSize)
    {
        int size = System.Math.Max(1, frameSize);
        if (_wet.Length == size)
        {
            _frame = size;
            return;
        }
        _wet = new StereoSample[size];
        _dry = new StereoSample[size];
        _frame = size;
    }

    internal void BeginFrame(int frame)
    {
        _frame = frame;
        Array.Clear(_wet, 0, frame);
        Array.Clear(_dry, 0, frame);
    }

    internal Span<StereoSample> WetBuffer(int frame) => _wet.AsSpan(0, frame);

    internal Span<StereoSample> DryBuffer(int frame) => _dry.AsSpan(0, frame);

    internal void FinishFrame(int sampleRate, int frame)
    {
        Span<StereoSample> wet = _wet.AsSpan(0, frame);
        for (int i = 0; i < _effects.Count; i++)
        {
            IAudioDspFilter filter = _effects[i];
            bool enabled;
            try { enabled = filter.Enabled; }
            catch { enabled = false; }
            if (!enabled) continue;
            try
            {
                filter.Process(wet, sampleRate);
            }
            catch
            {
                // A filter that threw has left the buffer in an unknown state; silence beats noise.
                wet.Clear();
                break;
            }
        }

        Span<StereoSample> dry = _dry.AsSpan(0, frame);
        for (int i = 0; i < frame; i++) wet[i] = wet[i].Add(dry[i]);

        SampleBuffers.Sanitize(wet);

        AudioDeviceOutput? device = Device;
        if (device != null)
        {
            SampleBuffers.Mix(device.Mix.AsSpan(0, frame), wet, 1f);
        }
    }
}

// A sound in the world. Source is pulled once per render (not once per listener) and resampled for
// doppler and pitch, the output's own effect chain runs on it, and then the frame is positioned per
// listener: shape gain, spatial blend, panning.
//
// THE OUTPUT'S OWN CHAIN runs on the sound as it leaves the source, before it is placed anywhere, so a
// radio filter on one speaker is heard through that speaker by every ear in the room, each from where it
// stands, and costs one pass however many ears there are. The chain arrives as an array swapped in by
// reference and never touched again by the world thread, so the audio thread takes it without a lock. A
// chain that is still ringing when the source stops (an echo, a flanger's feedback) keeps the voice
// running on silence until its tail is out: a filter that reports one is asked, and anything else is given
// a quarter of a second after the chain's output has gone quiet. -xlinka
public sealed class AudioOutputVoice
{
    internal AudioOutputVoice(AudioSpace space)
    {
        Space = space;
        int frame = System.Math.Max(1, space.Simulator.FrameSize);
        _scratch = new StereoSample[frame];
        _block = new StereoSample[frame];
        _fifo = new CircularAudioBuffer<StereoSample>(frame * FifoFrames);
    }

    private const int FifoFrames = 4;
    private const int MaxFeedBlocks = 96;
    private const float SilenceFloor = 1e-6f;
    private const double QuietSeconds = 0.25;
    private const double MinRate = 1.0 / 64.0;
    private const double MaxRate = 64.0;
    // A rate step of more than a few percent per frame is audible as a zip on a moving source.
    private const double MaxRateStep = 0.03;

    private struct PairGain
    {
        public float Left;
        public float Right;
    }

    private readonly object _paramLock = new object();
    private readonly HashSet<AudioListenerVoice> _stagedExcluded = new HashSet<AudioListenerVoice>();
    private readonly HashSet<AudioListenerVoice> _excluded = new HashSet<AudioListenerVoice>();
    private readonly Dictionary<AudioListenerVoice, PairGain> _pairs = new Dictionary<AudioListenerVoice, PairGain>();

    private AudioOutputParams _staged = AudioOutputParams.Default;
    private AudioOutputParams _current = AudioOutputParams.Default;
    private IAudioDataSource? _stagedSource;
    private IAudioDataSource? _source;
    private bool _paramsDirty;
    private bool _sourceDirty;
    private bool _excludedDirty;

    private StereoSample[] _scratch;
    private StereoSample[] _block;
    private CircularAudioBuffer<StereoSample> _fifo;
    private long _fifoPosition;
    private double _fifoFraction;
    private StereoSample _fifoLast;
    private double _rate = 1.0;
    private bool _rateStarted;
    private bool _hasActiveSource;
    private bool _sourceActive;
    private float _peak;

    private IAudioDspFilter[] _stagedEffects = Array.Empty<IAudioDspFilter>();
    private IAudioDspFilter[] _effects = Array.Empty<IAudioDspFilter>();
    private bool _ringing;
    private int _quietSamples;

    // A MONITOR TAP, so a picture of a sound can be drawn without taking the sound.
    //
    // The obvious way to visualise a voice is to read its source, and it is wrong: a stream's Read
    // advances a cursor, so a meter reading it is not watching the audio, it is eating it. The mixer
    // is already holding the finished block, so it keeps a copy of the last one and anybody who wants
    // to draw it copies that instead. Costs one memcpy per voice per frame and cannot disturb
    // playback by construction. -xlinka
    private readonly StereoSample[] _monitor = new StereoSample[1024];
    private int _monitorCount;
    private readonly object _monitorLock = new();

    public AudioSpace Space { get; }

    public void Update(in AudioOutputParams parameters)
    {
        lock (_paramLock)
        {
            _staged = parameters;
            _paramsDirty = true;
        }
    }

    public void SetSource(IAudioDataSource? source)
    {
        lock (_paramLock)
        {
            _stagedSource = source;
            _sourceDirty = true;
        }
    }

    // Chain applied in order to this output's own sound before it is positioned; null or empty = none.
    // A filter must not be in two chains at once. Lock-free: the list is copied into a fresh array here,
    // on the caller's thread, and the audio thread picks the array up at the top of its next render.
    public void SetEffects(IReadOnlyList<IAudioDspFilter>? effects)
    {
        int count = 0;
        if (effects != null)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] != null) count++;
            }
        }
        IAudioDspFilter[] chain = count == 0 ? Array.Empty<IAudioDspFilter>() : new IAudioDspFilter[count];
        int at = 0;
        for (int i = 0; at < count; i++)
        {
            IAudioDspFilter filter = effects![i];
            if (filter != null) chain[at++] = filter;
        }
        Volatile.Write(ref _stagedEffects, chain);
    }

    // Listeners that must not hear this output (a user's own voice, a camera). Null = nobody
    // excluded.
    public void SetExcludedListeners(IReadOnlyCollection<AudioListenerVoice>? excluded)
    {
        lock (_paramLock)
        {
            _stagedExcluded.Clear();
            if (excluded != null)
            {
                // Copied here rather than on the audio thread: the caller's collection belongs to
                // the world thread and may be mutated the instant this returns.
                foreach (AudioListenerVoice listener in excluded)
                {
                    if (listener != null) _stagedExcluded.Add(listener);
                }
            }
            _excludedDirty = true;
        }
    }

    // Loudness the mixer measured on the last frame this output was rendered, for culling and meters.
    public float LastPeakAmplitude => _peak;

    // Copies the most recent block this voice produced. Returns how many samples were written; zero
    // means the voice has not produced anything yet.
    public int CopyMonitor(Span<StereoSample> destination)
    {
        lock (_monitorLock)
        {
            int n = System.Math.Min(destination.Length, _monitorCount);
            _monitor.AsSpan(0, n).CopyTo(destination);
            return n;
        }
    }

    // What the last render applied for the device-bound listener, for reading a silent output off the log.

    internal int Priority => _current.Priority;

    internal bool HasActiveSource => _hasActiveSource;

    internal void ApplyStaged()
    {
        _effects = Volatile.Read(ref _stagedEffects);
        lock (_paramLock)
        {
            if (_paramsDirty)
            {
                _current = _staged;
                _paramsDirty = false;
            }
            if (_sourceDirty)
            {
                if (!ReferenceEquals(_source, _stagedSource))
                {
                    _source = _stagedSource;
                    ResetStream();
                }
                _sourceDirty = false;
            }
            if (_excludedDirty)
            {
                _excluded.Clear();
                foreach (AudioListenerVoice listener in _stagedExcluded) _excluded.Add(listener);
                _excludedDirty = false;
            }
        }
    }

    internal void EnsureBuffers(int frameSize)
    {
        int size = System.Math.Max(1, frameSize);
        if (_scratch.Length == size) return;
        _scratch = new StereoSample[size];
        _block = new StereoSample[size];
        _fifo = new CircularAudioBuffer<StereoSample>(size * FifoFrames);
        ResetStream();
    }

    internal void ForgetListener(AudioListenerVoice listener) => _pairs.Remove(listener);

    internal bool Excludes(AudioListenerVoice listener) => _excluded.Count > 0 && _excluded.Contains(listener);

    internal void BeginFrame()
    {
        _peak = 0f;
        IAudioDataSource? source = _source;
        bool active = false;
        if (source != null)
        {
            try { active = source.IsActive; }
            catch { active = false; }
        }
        _sourceActive = active;
        _hasActiveSource = active || (_ringing && _effects.Length > 0);
    }

    internal float Audibility(List<AudioListenerVoice> listeners)
    {
        float best = 0f;
        bool anyBound = false;
        for (int i = 0; i < listeners.Count; i++)
        {
            AudioListenerVoice listener = listeners[i];
            if (listener.Device == null) continue;
            anyBound = true;
            float distance = (_current.Position - listener.CurrentParams.Position).Length;
            float gain = Spatializer.ShapeGain(_current.Shape, distance);
            if (gain > best) best = gain;
        }
        if (!anyBound) best = 1f;

        float audibility = _current.Volume * best;
        // The sort key packs this as raw float bits, so it has to be a normal non-negative number.
        if (!(audibility > 0f)) return 0f;
        return float.IsPositiveInfinity(audibility) ? float.MaxValue : audibility;
    }

    internal void PullFrame(AudioSimulator simulator, int frame, AudioListenerVoice? dopplerListener)
    {
        IAudioDataSource? source = _source;
        Span<StereoSample> scratch = _scratch.AsSpan(0, frame);
        if (source == null || !_sourceActive)
        {
            // Only reached while the output's own chain rings out: it is fed silence.
            scratch.Clear();
        }
        else
        {
            AdvanceRate(dopplerListener);

            int produced = 0;
            int blocks = 0;
            while (produced < frame && blocks < MaxFeedBlocks)
            {
                produced += _fifo.Read(scratch.Slice(produced), ref _fifoPosition, _rate, ref _fifoFraction, ref _fifoLast);
                if (produced >= frame) break;

                Span<StereoSample> block = _block.AsSpan(0, frame);
                try { source.Read(block, simulator); }
                catch { block.Clear(); }
                _fifo.Put(block);
                blocks++;
            }

            if (produced < frame) scratch.Slice(produced).Clear();
            SampleBuffers.Sanitize(scratch.Slice(0, frame));
        }

        bool tail = RunEffects(scratch, simulator.SampleRate);

        float peak = 0f;
        for (int i = 0; i < frame; i++)
        {
            float amplitude = scratch[i].AbsoluteAmplitude;
            if (amplitude > peak) peak = amplitude;
        }
        _peak = float.IsFinite(peak) ? peak : 0f;

        if (_effects.Length == 0)
        {
            _ringing = false;
        }
        else
        {
            _quietSamples = _peak > SilenceFloor ? 0 : System.Math.Min(int.MaxValue / 2, _quietSamples + frame);
            _ringing = tail || _quietSamples < QuietSeconds * simulator.SampleRate;
        }

        lock (_monitorLock)
        {
            int n = System.Math.Min(frame, _monitor.Length);
            scratch.Slice(0, n).CopyTo(_monitor);
            _monitorCount = n;
        }
    }

    // Once per render, in place on the pulled frame. True when a filter in it reported sound still to come.
    private bool RunEffects(Span<StereoSample> buffer, int sampleRate)
    {
        IAudioDspFilter[] effects = _effects;
        if (effects.Length == 0) return false;

        bool tail = false;
        for (int i = 0; i < effects.Length; i++)
        {
            IAudioDspFilter filter = effects[i];
            bool enabled;
            try { enabled = filter.Enabled; }
            catch { enabled = false; }
            if (!enabled) continue;
            try
            {
                filter.Process(buffer, sampleRate);
                if (filter is IAudioTailFilter ringing && ringing.HasTail) tail = true;
            }
            catch
            {
                // A filter that threw has left the buffer in an unknown state; silence beats noise.
                buffer.Clear();
                tail = false;
                break;
            }
        }
        SampleBuffers.Sanitize(buffer);
        return tail;
    }

    internal void MixInto(AudioListenerVoice listener, int frame)
    {
        ref readonly AudioListenerParams listenerParams = ref listener.CurrentParams;
        float3 delta = _current.Position - listenerParams.Position;
        float distance = delta.Length;
        if (!float.IsFinite(distance)) distance = 0f;

        float shape = Spatializer.ShapeGain(_current.Shape, distance);
        Spatializer.ChannelGains(_current, listenerParams, delta, distance, shape, out float left, out float right);

        _pairs.TryGetValue(listener, out PairGain previous);
        _pairs[listener] = new PairGain { Left = left, Right = right };

        if (previous.Left == 0f && previous.Right == 0f && left == 0f && right == 0f) return;

        Span<StereoSample> target = _current.IgnoreEffects ? listener.DryBuffer(frame) : listener.WetBuffer(frame);
        SampleBuffers.MixRamped(target, _scratch.AsSpan(0, frame), previous.Left, left, previous.Right, right);
    }

    private void AdvanceRate(AudioListenerVoice? dopplerListener)
    {
        double target = _current.Pitch;
        if (dopplerListener != null)
        {
            ref readonly AudioListenerParams listenerParams = ref dopplerListener.CurrentParams;
            float3 delta = _current.Position - listenerParams.Position;
            float distance = delta.Length;
            if (float.IsFinite(distance)) target *= Spatializer.Doppler(_current, listenerParams, delta, distance);
        }

        if (!(target > MinRate)) target = MinRate;
        else if (target > MaxRate) target = MaxRate;

        if (!_rateStarted)
        {
            _rate = target;
            _rateStarted = true;
            return;
        }

        double step = System.Math.Max(_rate * MaxRateStep, 0.0005);
        double delta2 = target - _rate;
        if (delta2 > step) delta2 = step;
        else if (delta2 < -step) delta2 = -step;
        _rate += delta2;
        if (!(_rate > MinRate)) _rate = MinRate;
        else if (_rate > MaxRate) _rate = MaxRate;
    }

    private void ResetStream()
    {
        _fifo.Clear();
        _fifoPosition = 0;
        _fifoFraction = 0.0;
        _fifoLast = default;
        _rateStarted = false;
        _peak = 0f;
    }
}
