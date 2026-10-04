// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Lumora.Audio.Wasapi;

// One Windows audio endpoint as a capture source: a microphone read directly, an output device read back
// through loopback, or everything the machine plays except this process. Each runs its own thread with
// its own client, so streaming one never takes the voice input off the microphone it is on.
//
// Nothing opens until Start, and the engine only starts an input somebody is listening to, so listing
// every endpoint the machine has costs a name and a sample rate each. -xlinka
[SupportedOSPlatform("windows")]
public sealed class WasapiEndpointInput : IAudioEndpointInput, IDisposable
{
    // 100 ns units. Enough that a hitch on the capture thread costs latency rather than a hole.
    private const long BufferDuration = 2_000_000;

    private readonly string _endpointId;
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile bool _running;
    private StereoSample[] _buffer = Array.Empty<StereoSample>();

    public string Name { get; }
    public string DeviceId { get; }
    public int SampleRate { get; }
    public bool IsSystemDefault => false;
    public bool IsCapturing => _running;
    public AudioEndpointKind Kind { get; }

    public event AudioSamplesHandler? SamplesCaptured;

    internal WasapiEndpointInput(string endpointId, string name, AudioEndpointKind kind, int sampleRate)
    {
        _endpointId = endpointId;
        Name = name;
        Kind = kind;
        SampleRate = sampleRate;
        DeviceId = kind == AudioEndpointKind.SystemLoopback ? "wasapi:system" : "wasapi:" + endpointId;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_thread != null)
                return;
            _stopRequested = false;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "Capture: " + Name };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            _thread = null;
            _stopRequested = true;
        }
        thread?.Join(1000);
        _running = false;
    }

    public void Dispose() => Stop();

    private void Run()
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr format = IntPtr.Zero;
        bool formatIsOurs = false;
        AutoResetEvent? ready = null;
        try
        {
            uint flags;
            if (Kind == AudioEndpointKind.SystemLoopback)
            {
                client = WasapiEndpoints.ActivateSystemLoopback();
                if (client == null)
                    return;
                // Process loopback has no mix format to ask for: the format is ours to name and the
                // engine converts into it. It also only delivers through the event.
                format = WasapiEndpoints.AllocateFloatStereo(SampleRate);
                formatIsOurs = true;
                flags = WasapiConstants.StreamFlagsLoopback | WasapiConstants.StreamFlagsEventCallback
                        | WasapiConstants.StreamFlagsAutoConvertPcm | WasapiConstants.StreamFlagsSrcDefaultQuality;
                ready = new AutoResetEvent(false);
            }
            else
            {
                client = WasapiEndpoints.ActivateEndpoint(_endpointId);
                if (client == null)
                    return;
                client.GetMixFormat(out format);
                flags = Kind == AudioEndpointKind.OutputLoopback ? WasapiConstants.StreamFlagsLoopback : 0u;
            }

            var layout = WasapiSampleLayout.From(format);
            if (!layout.IsReadable)
            {
                WasapiEndpoints.Report($"{Name}: a sample format this cannot read ({layout.Channels} channels, {layout.BytesPerSample * 8} bit)");
                return;
            }

            client.Initialize(WasapiConstants.ShareModeShared, flags, BufferDuration, 0, format, IntPtr.Zero);
            if (ready != null)
                client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            var iid = WasapiConstants.IidAudioCaptureClient;
            client.GetService(ref iid, out object service);
            capture = (IAudioCaptureClient)service;
            client.Start();

            while (!_stopRequested)
            {
                if (ready != null)
                    ready.WaitOne(20);
                else
                    Thread.Sleep(5);
                if (!Drain(capture, layout))
                    break;
            }

            client.Stop();
        }
        catch (Exception ex)
        {
            WasapiEndpoints.Report($"{Name}: capture stopped: {ex.Message}");
        }
        finally
        {
            WasapiEndpoints.Release(capture);
            WasapiEndpoints.Release(client);
            if (format != IntPtr.Zero)
            {
                if (formatIsOurs)
                    Marshal.FreeHGlobal(format);
                else
                    Marshal.FreeCoTaskMem(format);
            }
            ready?.Dispose();
            _running = false;
        }
    }

    // Everything waiting, packet by packet. False when the device went away under the stream: an unplugged
    // headset or a changed default ends the capture rather than spinning on errors.
    private bool Drain(IAudioCaptureClient capture, in WasapiSampleLayout layout)
    {
        while (!_stopRequested)
        {
            int result = capture.GetNextPacketSize(out uint packet);
            if (result < 0)
            {
                WasapiEndpoints.Report($"{Name}: the device went away (0x{result:X8})");
                return false;
            }
            if (packet == 0)
                return true;

            result = capture.GetBuffer(out IntPtr data, out uint frames, out uint bufferFlags, out _, out _);
            if (result == WasapiConstants.BufferEmpty)
                return true;
            if (result < 0)
            {
                WasapiEndpoints.Report($"{Name}: could not read a packet (0x{result:X8})");
                return false;
            }

            if (frames > 0)
                Deliver(data, (int)frames, (bufferFlags & WasapiConstants.BufferFlagsSilent) != 0, layout);
            capture.ReleaseBuffer(frames);
        }
        return true;
    }

    private unsafe void Deliver(IntPtr data, int frames, bool silent, in WasapiSampleLayout layout)
    {
        if (_buffer.Length < frames)
            _buffer = new StereoSample[Math.Max(frames, _buffer.Length * 2)];
        var target = _buffer.AsSpan(0, frames);
        if (silent || data == IntPtr.Zero)
            target.Clear();
        else
            layout.Convert((byte*)data, frames, target);
        SamplesCaptured?.Invoke(target);
    }
}
