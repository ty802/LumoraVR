// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Lumora.Audio.Wasapi;

// One Windows output device as a second way out: a camera's sound played into a virtual cable that a
// streaming program captures, while the headset keeps hearing the room. No thread of its own; whoever
// feeds it polls FramesWritable and calls Write the way the mixer feeds its main device, and the shared
// engine converts the float stereo it is handed into whatever the device mixes in.
//
// Start and Stop come from the main thread while Write runs on the audio thread, so the stream sits
// behind one lock. The client is opened and closed on a multithreaded apartment: a COM object belongs to
// the apartment that made it, and the main thread may be a single-threaded one the audio thread cannot
// call into. -xlinka
public sealed class WasapiEndpointOutput : IAudioDeviceOutput, IDisposable
{
    private const int MixRate = 48000;

    // 100 ns units. A tenth of a second: a hitch on the audio thread costs latency rather than a hole,
    // and the sound stays close to the picture it belongs to.
    private const long BufferDuration = 1_000_000;

    private readonly object _control = new();
    private readonly object _gate = new();
    private IAudioClient? _client;
    private IAudioRenderClient? _render;
    private uint _bufferFrames;
    private bool _failed;
    private volatile bool _playing;
    private double _latency = BufferDuration / 10_000_000.0;

    public string Name { get; }
    public string EndpointId { get; }
    public int SampleRate => MixRate;
    public bool IsPlaying => _playing;
    public double LatencySeconds => Volatile.Read(ref _latency);

    public WasapiEndpointOutput(string endpointId, string? name = null)
    {
        EndpointId = endpointId;
        Name = string.IsNullOrEmpty(name) ? endpointId : name;
    }

    public int FramesWritable
    {
        get
        {
            lock (_gate)
                return WritableLocked();
        }
    }

    // Never more than the device has room for and never a wait for more: what does not fit is dropped,
    // because the thread calling this is also the one feeding the headset.
    public unsafe void Write(ReadOnlySpan<StereoSample> frames)
    {
        if (frames.IsEmpty)
            return;
        lock (_gate)
        {
            int count = Math.Min(frames.Length, WritableLocked());
            if (count <= 0 || _render == null)
                return;
            int result = _render.GetBuffer((uint)count, out IntPtr data);
            if (result < 0)
            {
                FailLocked(result);
                return;
            }
            // Opened as float stereo, which is a StereoSample laid end to end.
            frames.Slice(0, count).CopyTo(new Span<StereoSample>((void*)data, count));
            result = _render.ReleaseBuffer((uint)count, 0);
            if (result < 0)
                FailLocked(result);
        }
    }

    // Opens on the first call and again on any call after a failure, so a device that went away and came
    // back is picked up by starting it once more.
    public void Start()
    {
        if (!OperatingSystem.IsWindows())
            return;
        lock (_control)
        {
            if (_playing)
                return;
            Close();
            WasapiEndpoints.OnMtaThread(Open);
        }
    }

    public void Stop()
    {
        if (!OperatingSystem.IsWindows())
            return;
        lock (_control)
            Close();
    }

    public void Dispose() => Stop();

    private int WritableLocked()
    {
        if (_client == null || _failed)
            return 0;
        int result = _client.GetCurrentPadding(out uint padding);
        if (result < 0)
        {
            FailLocked(result);
            return 0;
        }
        return padding >= _bufferFrames ? 0 : (int)(_bufferFrames - padding);
    }

    // Said once per failure. The dead client is left for Stop or the next Start to let go, so teardown
    // has one path and it is never on the audio thread.
    private void FailLocked(int result)
    {
        if (_failed)
            return;
        _failed = true;
        _playing = false;
        WasapiEndpoints.Report(result == WasapiConstants.DeviceInvalidated
            ? $"{Name}: the device went away (0x{result:X8})"
            : $"{Name}: playback stopped (0x{result:X8})");
    }

    [SupportedOSPlatform("windows")]
    private void Open()
    {
        IAudioClient? client = null;
        IAudioRenderClient? render = null;
        IntPtr format = WasapiEndpoints.AllocateFloatStereo(MixRate);
        try
        {
            client = WasapiEndpoints.ActivateEndpoint(EndpointId);
            if (client == null)
                return;
            // The mixer's own rate whatever the device runs at; the engine resamples on its side.
            client.Initialize(WasapiConstants.ShareModeShared,
                WasapiConstants.StreamFlagsAutoConvertPcm | WasapiConstants.StreamFlagsSrcDefaultQuality,
                BufferDuration, 0, format, IntPtr.Zero);
            client.GetBufferSize(out uint bufferFrames);
            client.GetStreamLatency(out long streamLatency);
            // Shared streams answer zero here on every device tried, yet the engine still holds each
            // buffer for one of its periods before the device sees it.
            if (streamLatency <= 0)
                client.GetDevicePeriod(out streamLatency, out _);
            var iid = WasapiConstants.IidAudioRenderClient;
            client.GetService(ref iid, out object service);
            render = (IAudioRenderClient)service;
            client.Start();

            lock (_gate)
            {
                _client = client;
                _render = render;
                _bufferFrames = bufferFrames;
                _failed = false;
                _playing = true;
            }
            Volatile.Write(ref _latency, (double)bufferFrames / MixRate + streamLatency / 10_000_000.0);
            client = null;
            render = null;
        }
        catch (Exception ex)
        {
            WasapiEndpoints.Report($"{Name}: could not open for playback: {ex.Message}");
        }
        finally
        {
            WasapiEndpoints.Release(render);
            WasapiEndpoints.Release(client);
            Marshal.FreeHGlobal(format);
        }
    }

    [SupportedOSPlatform("windows")]
    private void Close()
    {
        IAudioClient? client;
        IAudioRenderClient? render;
        lock (_gate)
        {
            client = _client;
            render = _render;
            _client = null;
            _render = null;
            _bufferFrames = 0;
            _failed = false;
            _playing = false;
        }
        if (client != null)
            WasapiEndpoints.OnMtaThread(() => Shut(client, render));
    }

    // A device that is already gone refuses the Stop and has nothing left to stop.
    [SupportedOSPlatform("windows")]
    private static void Shut(IAudioClient client, IAudioRenderClient? render)
    {
        try
        {
            client.Stop();
        }
        catch
        {
        }
        WasapiEndpoints.Release(render);
        WasapiEndpoints.Release(client);
    }
}
