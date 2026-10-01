// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lumora.Core.Desktop;

// The PipeWire calls a video consumer needs. Layouts are from the 0.3 and 1.x headers, which agree on
// every struct read here; the stream events table is version 2. -xlinka
[SupportedOSPlatform("linux")]
internal static unsafe class PipeWireNative
{
    public const string Library = "libpipewire-0.3.so.0";

    public const int DirectionInput = 0;
    public const uint FlagAutoconnect = 1u << 0;
    public const uint FlagMapBuffers = 1u << 2;
    public const int StateError = -1;
    public const int StateStreaming = 3;
    public const uint StreamEventsVersion = 2;

    [DllImport(Library)] public static extern void pw_init(int* argc, byte*** argv);
    [DllImport(Library)] public static extern IntPtr pw_thread_loop_new(byte* name, IntPtr props);
    [DllImport(Library)] public static extern IntPtr pw_thread_loop_get_loop(IntPtr loop);
    [DllImport(Library)] public static extern int pw_thread_loop_start(IntPtr loop);
    [DllImport(Library)] public static extern void pw_thread_loop_stop(IntPtr loop);
    [DllImport(Library)] public static extern void pw_thread_loop_lock(IntPtr loop);
    [DllImport(Library)] public static extern void pw_thread_loop_unlock(IntPtr loop);
    [DllImport(Library)] public static extern void pw_thread_loop_destroy(IntPtr loop);
    [DllImport(Library)] public static extern IntPtr pw_context_new(IntPtr loop, IntPtr props, nuint userDataSize);
    [DllImport(Library)] public static extern void pw_context_destroy(IntPtr context);
    [DllImport(Library)] public static extern IntPtr pw_context_connect_fd(IntPtr context, int fd, IntPtr props, nuint userDataSize);
    [DllImport(Library)] public static extern int pw_core_disconnect(IntPtr core);
    [DllImport(Library)] public static extern IntPtr pw_properties_new_string(byte* text);
    [DllImport(Library)] public static extern IntPtr pw_stream_new(IntPtr core, byte* name, IntPtr props);
    [DllImport(Library)] public static extern void pw_stream_add_listener(IntPtr stream, SpaHook* listener, StreamEvents* events, IntPtr data);
    [DllImport(Library)] public static extern int pw_stream_connect(IntPtr stream, int direction, uint target, uint flags, byte** parameters, uint count);
    [DllImport(Library)] public static extern int pw_stream_disconnect(IntPtr stream);
    [DllImport(Library)] public static extern void pw_stream_destroy(IntPtr stream);
    [DllImport(Library)] public static extern int pw_stream_update_params(IntPtr stream, byte** parameters, uint count);
    [DllImport(Library)] public static extern PwBuffer* pw_stream_dequeue_buffer(IntPtr stream);
    [DllImport(Library)] public static extern int pw_stream_queue_buffer(IntPtr stream, PwBuffer* buffer);

    public static bool Available => NativeLibrary.TryLoad(Library, out _);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct StreamEvents
{
    public uint Version;
    public IntPtr Destroy;
    public IntPtr StateChanged;
    public IntPtr ControlInfo;
    public IntPtr IoChanged;
    public IntPtr ParamChanged;
    public IntPtr AddBuffer;
    public IntPtr RemoveBuffer;
    public IntPtr Process;
    public IntPtr Drained;
    public IntPtr Command;
    public IntPtr TriggerDone;
}

// Owned by the stream once added; only ever zeroed and handed over.
[StructLayout(LayoutKind.Sequential)]
internal struct SpaHook
{
    public IntPtr LinkPrevious, LinkNext, Functions, Data, Removed, Private;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PwBuffer
{
    public SpaBuffer* Buffer;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SpaBuffer
{
    public uint MetaCount;
    public uint DataCount;
    public IntPtr Metas;
    public SpaData* Datas;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SpaData
{
    public uint Type;
    public uint Flags;
    public long Fd;
    public uint MapOffset;
    public uint MaxSize;
    public byte* Data;
    public SpaChunk* Chunk;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpaChunk
{
    public uint Offset;
    public uint Size;
    public int Stride;
    public int Flags;
}
