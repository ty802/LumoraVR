// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Desktop;

// One shared monitor, as a PipeWire video stream from the compositor. Frames arrive whole, in shared
// memory, on PipeWire's own thread; only the newest of a burst is looked at, it is compared with the last
// and only the strips that changed are passed on. The cursor is drawn into the frames by the compositor,
// so there is no pointer image of its own here.
//
// A stream is held only while it is still the one the portal shared: when PipeWire drops, the share ends
// or a new share brings new streams, it is let go of and the next one picked up.
//
// Native callbacks must never throw back into PipeWire, so each one catches everything and turns it into
// a Problem. -xlinka
[SupportedOSPlatform("linux")]
internal sealed unsafe class PortalDesktopCapture : DesktopCapture
{
    private const uint AnyTarget = 0xFFFFFFFF;
    private const uint FlagDontReconnect = 1u << 7;
    private const int StateUnconnected = 0;
    private const int ChunkCorrupted = 1;

    private static readonly object InitGate = new();
    private static bool _initialised;

    private readonly PortalDesktopBackend _backend;
    private IntPtr _stream;
    private uint _format;
    private int _width;
    private int _height;
    private byte[] _previous = Array.Empty<byte>();
    private bool _first = true;
    private volatile bool _failed;

    public PortalDesktopCapture(DesktopDisplay display, PortalDesktopBackend backend) : base(display)
    {
        _backend = backend;
    }

    protected override void Run(RunFlag run)
    {
        _backend.CaptureStarted();
        try
        {
            while (!run.Stop)
            {
                var session = _backend.Session;
                var shared = session != null ? _backend.StreamOf(session, Display) : null;
                if (session == null || shared == null)
                {
                    SetProblem(_backend.Status ?? "Waiting for the desktop to share this monitor.");
                    Nap(500, run);
                    continue;
                }
                int fd;
                try
                {
                    fd = session.OpenPipeWire();
                }
                catch (Exception ex)
                {
                    LumoraLogger.Warn($"PortalDesktopCapture: no video connection: {ex.Message}");
                    fd = -1;
                }
                if (fd < 0)
                {
                    SetProblem("The desktop would not open its video connection.");
                    Nap(1000, run);
                    continue;
                }
                SetProblem(null);
                Stream(fd, session, shared, run);
                if (!run.Stop)
                    Nap(500, run);
            }
        }
        finally
        {
            _backend.CaptureStopped();
        }
    }

    // Still the stream the portal shared for this monitor.
    private bool StillShared(PortalSession session, PortalSession.Stream shared)
        => ReferenceEquals(_backend.Session, session) && _backend.StreamOf(session, Display) == shared;

    private void Stream(int fd, PortalSession session, PortalSession.Stream shared, RunFlag run)
    {
        lock (InitGate)
        {
            if (!_initialised)
            {
                PipeWireNative.pw_init(null, null);
                _initialised = true;
            }
        }
        _failed = false;
        _first = true;
        IntPtr loop, context = IntPtr.Zero, core = IntPtr.Zero;
        fixed (byte* name = "lumora-desktop\0"u8)
            loop = PipeWireNative.pw_thread_loop_new(name, IntPtr.Zero);
        if (loop == IntPtr.Zero)
        {
            DBusConnection.close(fd);
            SetProblem("PipeWire would not start.");
            return;
        }
        var events = (StreamEvents*)NativeMemory.AllocZeroed((nuint)sizeof(StreamEvents));
        var hook = (SpaHook*)NativeMemory.AllocZeroed((nuint)sizeof(SpaHook));
        var self = GCHandle.Alloc(this);
        try
        {
            context = PipeWireNative.pw_context_new(PipeWireNative.pw_thread_loop_get_loop(loop), IntPtr.Zero, 0);
            if (context == IntPtr.Zero || PipeWireNative.pw_thread_loop_start(loop) < 0)
            {
                DBusConnection.close(fd);
                SetProblem("PipeWire would not start.");
                return;
            }
            PipeWireNative.pw_thread_loop_lock(loop);
            try
            {
                // The context owns the fd from here, and closes it itself if the connection fails.
                core = PipeWireNative.pw_context_connect_fd(context, fd, IntPtr.Zero, 0);
                if (core == IntPtr.Zero)
                {
                    SetProblem("PipeWire refused the desktop's connection.");
                    return;
                }
                // By serial where the portal gives one: node ids are reused, a serial never is.
                string target = shared.Serial != 0 ? $" target.object={shared.Serial}" : string.Empty;
                var text = System.Text.Encoding.UTF8.GetBytes("media.type=Video media.category=Capture media.role=Screen" + target + "\0");
                IntPtr properties;
                fixed (byte* line = text)
                    properties = PipeWireNative.pw_properties_new_string(line);
                fixed (byte* streamName = "Lumora desktop\0"u8)
                    _stream = PipeWireNative.pw_stream_new(core, streamName, properties);
                if (_stream == IntPtr.Zero)
                {
                    SetProblem("PipeWire would not make a stream.");
                    return;
                }
                events->Version = PipeWireNative.StreamEventsVersion;
                events->StateChanged = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, int, byte*, void>)&OnStateChanged;
                events->ParamChanged = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, uint, byte*, void>)&OnParamChanged;
                events->Process = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&OnProcess;
                PipeWireNative.pw_stream_add_listener(_stream, hook, events, GCHandle.ToIntPtr(self));
                var formats = SpaPod.VideoFormats();
                fixed (byte* pod = formats)
                {
                    byte* parameters = pod;
                    uint flags = PipeWireNative.FlagAutoconnect | PipeWireNative.FlagMapBuffers | FlagDontReconnect;
                    if (PipeWireNative.pw_stream_connect(_stream, PipeWireNative.DirectionInput, shared.Serial != 0 ? AnyTarget : shared.Node,
                        flags, &parameters, 1) < 0)
                    {
                        SetProblem("PipeWire would not connect to the shared monitor.");
                        return;
                    }
                }
            }
            finally
            {
                PipeWireNative.pw_thread_loop_unlock(loop);
            }

            while (!run.Stop && !_failed && StillShared(session, shared))
                Nap(100, run);
        }
        finally
        {
            if (core != IntPtr.Zero)
            {
                PipeWireNative.pw_thread_loop_lock(loop);
                if (_stream != IntPtr.Zero)
                {
                    PipeWireNative.pw_stream_disconnect(_stream);
                    PipeWireNative.pw_stream_destroy(_stream);
                    _stream = IntPtr.Zero;
                }
                PipeWireNative.pw_core_disconnect(core);
                PipeWireNative.pw_thread_loop_unlock(loop);
            }
            PipeWireNative.pw_thread_loop_stop(loop);
            if (context != IntPtr.Zero)
                PipeWireNative.pw_context_destroy(context);
            PipeWireNative.pw_thread_loop_destroy(loop);
            self.Free();
            NativeMemory.Free(hook);
            NativeMemory.Free(events);
        }
    }

    private static PortalDesktopCapture? From(IntPtr data)
        => data == IntPtr.Zero ? null : GCHandle.FromIntPtr(data).Target as PortalDesktopCapture;

    // An error, or a connection that fell back to unconnected (PipeWire itself went away, or the shared
    // node did), ends this stream.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnStateChanged(IntPtr data, int old, int state, byte* error)
    {
        try
        {
            var capture = From(data);
            if (capture == null)
                return;
            if (state == PipeWireNative.StateError)
            {
                capture.SetProblem("The desktop's video stream failed: " + (error != null ? Marshal.PtrToStringUTF8((IntPtr)error) : "no reason given"));
                capture._failed = true;
            }
            else if (state == StateUnconnected && old != StateUnconnected)
            {
                capture._failed = true;
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"PortalDesktopCapture: state handler: {ex.Message}");
        }
    }

    // The format is only known once the stream settles on one; buffers are then asked for in memory
    // this side can read.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnParamChanged(IntPtr data, uint id, byte* param)
    {
        try
        {
            var capture = From(data);
            if (capture == null || id != SpaPod.ParamFormat || param == null)
                return;
            if (!SpaPod.TryReadFormat(param, out uint format, out int width, out int height) || width <= 0 || height <= 0)
                return;
            if (format is not (SpaPod.VideoBgrx or SpaPod.VideoBgra or SpaPod.VideoRgbx or SpaPod.VideoRgba))
            {
                capture.SetProblem($"The desktop sent a picture format this cannot read ({format}).");
                return;
            }
            capture._format = format;
            capture._width = width;
            capture._height = height;
            capture._previous = new byte[(long)width * height * 4];
            capture._first = true;
            capture.Resize(width, height);
            var buffers = SpaPod.MemoryBuffers();
            fixed (byte* pod = buffers)
            {
                byte* parameters = pod;
                PipeWireNative.pw_stream_update_params(capture._stream, &parameters, 1);
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"PortalDesktopCapture: format handler: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnProcess(IntPtr data)
    {
        try
        {
            From(data)?.TakeFrame();
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"PortalDesktopCapture: frame handler: {ex.Message}");
        }
    }

    // Only the newest buffer of whatever is waiting is looked at; the older ones go straight back.
    private void TakeFrame()
    {
        PwBuffer* newest = null, buffer;
        while ((buffer = PipeWireNative.pw_stream_dequeue_buffer(_stream)) != null)
        {
            if (newest != null)
                PipeWireNative.pw_stream_queue_buffer(_stream, newest);
            newest = buffer;
        }
        if (newest == null)
            return;
        try
        {
            var spa = newest->Buffer;
            if (spa == null || spa->DataCount == 0 || _width <= 0)
                return;
            var plane = spa->Datas;
            if (plane->Chunk == null)
                return;
            // A buffer the compositor marks empty or corrupted carries no new picture: a failed recording,
            // or one that only moved the cursor.
            if (plane->Chunk->Size == 0 || (plane->Chunk->Flags & ChunkCorrupted) != 0)
                return;
            if (plane->Data == null)
            {
                SetProblem("The desktop sent its picture in GPU memory, which this cannot read.");
                return;
            }
            long stride = plane->Chunk->Stride > 0 ? plane->Chunk->Stride : _width * 4L;
            if (stride < _width * 4L || plane->Chunk->Offset + stride * (_height - 1) + _width * 4L > plane->MaxSize)
                return;
            byte* picture = plane->Data + plane->Chunk->Offset;
            PassChangedStrips(picture, stride, _width, _height, _previous, _first, _format is SpaPod.VideoBgrx or SpaPod.VideoBgra);
            if (_first)
            {
                _first = false;
                MarkPicture();
            }
        }
        finally
        {
            PipeWireNative.pw_stream_queue_buffer(_stream, newest);
        }
    }
}
