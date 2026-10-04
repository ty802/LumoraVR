// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lumora.Audio.Wasapi;

// The slice of the Windows audio session API the endpoint capture and playback need, declared by hand:
// endpoint enumeration, shared-mode capture and render clients, and the process loopback activation.
// Method order in each interface IS the vtable order; move one and every call after it lands on the
// wrong function. -xlinka

internal enum DataFlow
{
    Render = 0,
    Capture = 1,
}

internal static class WasapiConstants
{
    public const uint DeviceStateActive = 0x1;
    public const uint ClsctxAll = 0x17;
    public const uint StgmRead = 0x0;
    public const int ShareModeShared = 0;
    public const int RoleConsole = 0;

    public const uint StreamFlagsLoopback = 0x00020000;
    public const uint StreamFlagsEventCallback = 0x00040000;
    public const uint StreamFlagsSrcDefaultQuality = 0x08000000;
    public const uint StreamFlagsAutoConvertPcm = 0x80000000;

    public const uint BufferFlagsSilent = 0x2;
    public const int BufferEmpty = 0x08890001;
    public const int DeviceInvalidated = unchecked((int)0x88890004);

    public const ushort VtLpwstr = 31;
    public const ushort VtBlob = 65;

    public const ushort FormatPcm = 1;
    public const ushort FormatIeeeFloat = 3;
    public const ushort FormatExtensible = 0xFFFE;

    public const int ActivationTypeProcessLoopback = 1;
    public const int LoopbackExcludeTargetTree = 1;
    public const string ProcessLoopbackDevice = "VAD\\Process_Loopback";

    public static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    public static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    public static readonly Guid IidAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    public static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(Guid formatId, int propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    public static PropertyKey DeviceFriendlyName => new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
}

// A PROPVARIANT as it lays out in a 64-bit process: the type tag, three reserved words, then the value.
// A string is a pointer at 8; a blob is its size at 8 and its data pointer at 16.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(8)] public uint BlobSize;
    [FieldOffset(16)] public IntPtr BlobData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessLoopbackActivation
{
    public int ActivationType;
    public uint TargetProcessId;
    public int LoopbackMode;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormat
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    void GetDefaultAudioEndpoint(DataFlow dataFlow, int role, out IMMDevice endpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IntPtr client);
    void UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsctx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(uint access, out IPropertyStore properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetState(out uint state);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    void GetBufferSize(out uint frames);
    void GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
    void GetMixFormat(out IntPtr format);
    void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    void Start();
    void Stop();
    void Reset();
    void SetEventHandle(IntPtr eventHandle);
    void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
    [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
}

// No members: implementing it is the declaration that the handler may be called from any thread, which
// the activation insists on before it will call back at all.
[ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject
{
}

internal sealed class ActivationCompletion : IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    private readonly ManualResetEventSlim _done = new(false);

    public int Result { get; private set; } = unchecked((int)0x80004005);

    public object? Activated { get; private set; }

    public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
    {
        try
        {
            operation.GetActivateResult(out int result, out object activated);
            Result = result;
            Activated = activated;
        }
        catch (Exception ex)
        {
            Result = ex.HResult;
        }
        finally
        {
            _done.Set();
        }
    }

    public bool Wait(int milliseconds) => _done.Wait(milliseconds);
}

internal static class WasapiNative
{
    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation operation);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern int PropVariantClear(ref PropVariant value);
}
