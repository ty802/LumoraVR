// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Lumora.Audio.Wasapi;

// Every audio endpoint the machine has, as capture sources.
//
// The engine's own audio layer captures one input device and has never heard of a loopback, so the only
// way anybody could share what their PC was playing was to turn on Stereo Mix or install a virtual cable.
// Windows has had loopback on every output since forever; this reads it directly:
//   - "System audio": everything the machine plays EXCEPT this process. The one that matters. Loopback
//     on the speakers also hears the room coming out of them, and streaming that back into the room is
//     an echo with a delay on it. Windows 11 and newer only; left off the list where it cannot work.
//   - every active output device, read back.
//   - every active capture device, so a second mic or a line in can go out while voice stays on the first.
// -xlinka
public static class WasapiEndpoints
{
    public const string SystemAudioName = "System audio";

    private const int SystemSampleRate = 48000;

    // Where the warnings go; the audio library has no logger of its own.
    public static Action<string>? Warn { get; set; }

    internal static void Report(string message)
    {
        try
        {
            Warn?.Invoke("WASAPI: " + message);
        }
        catch
        {
        }
    }

    // Process loopback arrived in build 20348; anything older fails the activation outright.
    public static bool SystemLoopbackAvailable
        => OperatingSystem.IsWindows() && Environment.Is64BitProcess && Environment.OSVersion.Version.Build >= 20348;

    public static IReadOnlyList<IAudioEndpointInput> Enumerate()
    {
        var found = new List<IAudioEndpointInput>();
        if (OperatingSystem.IsWindows())
            CollectOnMta(found);
        return found;
    }

    [SupportedOSPlatform("windows")]
    private static void CollectOnMta(List<IAudioEndpointInput> found) => OnMtaThread(() => Collect(found));

    [SupportedOSPlatform("windows")]
    private static void Collect(List<IAudioEndpointInput> found)
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = CreateEnumerator();
            if (SystemLoopbackAvailable)
                found.Add(new WasapiEndpointInput(string.Empty, SystemAudioName, AudioEndpointKind.SystemLoopback, SystemSampleRate));
            Collect(enumerator, DataFlow.Render, AudioEndpointKind.OutputLoopback, found);
            Collect(enumerator, DataFlow.Capture, AudioEndpointKind.Capture, found);
        }
        catch (Exception ex)
        {
            Report("could not list the audio endpoints: " + ex.Message);
        }
        finally
        {
            Release(enumerator);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Collect(IMMDeviceEnumerator enumerator, DataFlow flow, AudioEndpointKind kind, List<IAudioEndpointInput> found)
    {
        enumerator.EnumAudioEndpoints(flow, WasapiConstants.DeviceStateActive, out var collection);
        try
        {
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    collection.Item(i, out device);
                    device.GetId(out string id);
                    int rate = ProbeSampleRate(device);
                    if (rate > 0)
                        found.Add(new WasapiEndpointInput(id, FriendlyName(device) ?? id, kind, rate));
                }
                catch (Exception ex)
                {
                    Report("skipped an endpoint: " + ex.Message);
                }
                finally
                {
                    Release(device);
                }
            }
        }
        finally
        {
            Release(collection);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? FriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(WasapiConstants.StgmRead, out var store);
        try
        {
            var key = PropertyKey.DeviceFriendlyName;
            store.GetValue(ref key, out var value);
            try
            {
                return value.VarType == WasapiConstants.VtLpwstr && value.Pointer != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.Pointer)
                    : null;
            }
            finally
            {
                WasapiNative.PropVariantClear(ref value);
            }
        }
        finally
        {
            Release(store);
        }
    }

    // The rate is all the engine needs up front; the client is opened for real when capture starts.
    [SupportedOSPlatform("windows")]
    private static int ProbeSampleRate(IMMDevice device)
    {
        IAudioClient? client = null;
        IntPtr format = IntPtr.Zero;
        try
        {
            var iid = WasapiConstants.IidAudioClient;
            device.Activate(ref iid, WasapiConstants.ClsctxAll, IntPtr.Zero, out object instance);
            client = (IAudioClient)instance;
            client.GetMixFormat(out format);
            return WasapiSampleLayout.From(format).SampleRate;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (format != IntPtr.Zero)
                Marshal.FreeCoTaskMem(format);
            Release(client);
        }
    }

    // Every active output device as somewhere to play: a second pair of speakers, or a virtual cable a
    // streaming program captures. The id is what WasapiEndpointOutput opens.
    public static IReadOnlyList<AudioOutputEndpoint> EnumerateOutputs()
    {
        var found = new List<AudioOutputEndpoint>();
        if (OperatingSystem.IsWindows())
            CollectOutputsOnMta(found);
        return found;
    }

    [SupportedOSPlatform("windows")]
    private static void CollectOutputsOnMta(List<AudioOutputEndpoint> found) => OnMtaThread(() => CollectOutputs(found));

    [SupportedOSPlatform("windows")]
    private static void CollectOutputs(List<AudioOutputEndpoint> found)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = CreateEnumerator();
            string? defaultId = DefaultOutputId(enumerator);
            enumerator.EnumAudioEndpoints(DataFlow.Render, WasapiConstants.DeviceStateActive, out collection);
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    collection.Item(i, out device);
                    device.GetId(out string id);
                    found.Add(new AudioOutputEndpoint(id, FriendlyName(device) ?? id, id == defaultId));
                }
                catch (Exception ex)
                {
                    Report("skipped an output: " + ex.Message);
                }
                finally
                {
                    Release(device);
                }
            }
        }
        catch (Exception ex)
        {
            Report("could not list the audio outputs: " + ex.Message);
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
    }

    // A machine with every output unplugged has no default, which is not worth a warning.
    [SupportedOSPlatform("windows")]
    private static string? DefaultOutputId(IMMDeviceEnumerator enumerator)
    {
        IMMDevice? device = null;
        try
        {
            enumerator.GetDefaultAudioEndpoint(DataFlow.Render, WasapiConstants.RoleConsole, out device);
            device.GetId(out string id);
            return id;
        }
        catch
        {
            return null;
        }
        finally
        {
            Release(device);
        }
    }

    [SupportedOSPlatform("windows")]
    internal static IAudioClient? ActivateEndpoint(string id)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = CreateEnumerator();
            enumerator.GetDevice(id, out device);
            var iid = WasapiConstants.IidAudioClient;
            device.Activate(ref iid, WasapiConstants.ClsctxAll, IntPtr.Zero, out object instance);
            return (IAudioClient)instance;
        }
        catch (Exception ex)
        {
            Report("could not open an endpoint: " + ex.Message);
            return null;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    // Asks for a capture of every process except this one and its children. The answer arrives on a
    // callback, which is waited for here: the caller is the capture thread and has nothing else to do.
    [SupportedOSPlatform("windows")]
    internal static IAudioClient? ActivateSystemLoopback()
    {
        int paramsSize = Marshal.SizeOf<ProcessLoopbackActivation>();
        IntPtr parameters = Marshal.AllocHGlobal(paramsSize);
        IntPtr variant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        IActivateAudioInterfaceAsyncOperation? operation = null;
        try
        {
            Marshal.StructureToPtr(new ProcessLoopbackActivation
            {
                ActivationType = WasapiConstants.ActivationTypeProcessLoopback,
                TargetProcessId = (uint)Environment.ProcessId,
                LoopbackMode = WasapiConstants.LoopbackExcludeTargetTree,
            }, parameters, false);
            Marshal.StructureToPtr(new PropVariant
            {
                VarType = WasapiConstants.VtBlob,
                BlobSize = (uint)paramsSize,
                BlobData = parameters,
            }, variant, false);

            var completion = new ActivationCompletion();
            WasapiNative.ActivateAudioInterfaceAsync(WasapiConstants.ProcessLoopbackDevice, WasapiConstants.IidAudioClient,
                variant, completion, out operation);
            if (!completion.Wait(5000))
            {
                Report("system audio did not answer in time");
                return null;
            }
            if (completion.Result < 0 || completion.Activated is not IAudioClient client)
            {
                Report($"system audio was refused (0x{completion.Result:X8})");
                return null;
            }
            return client;
        }
        catch (Exception ex)
        {
            Report("could not open system audio: " + ex.Message);
            return null;
        }
        finally
        {
            Release(operation);
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    internal static IntPtr AllocateFloatStereo(int sampleRate)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormat>());
        Marshal.StructureToPtr(new WaveFormat
        {
            FormatTag = WasapiConstants.FormatIeeeFloat,
            Channels = 2,
            SamplesPerSecond = (uint)sampleRate,
            AverageBytesPerSecond = (uint)sampleRate * 8,
            BlockAlign = 8,
            BitsPerSample = 32,
            ExtraSize = 0,
        }, pointer, false);
        return pointer;
    }

    // Straight to the interface, never through a class type: the enumerator is one object per process,
    // so if anything else here already holds it the runtime hands back that holder's wrapper, and a cast
    // to our own class type throws where a cast to the interface is only a QueryInterface.
    [SupportedOSPlatform("windows")]
    private static IMMDeviceEnumerator CreateEnumerator()
        => (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClassId, throwOnError: true)!)!;

    private static readonly Guid EnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    [SupportedOSPlatform("windows")]
    internal static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance))
            Marshal.ReleaseComObject(instance);
    }

    // COM wants the enumerator, and any client the audio thread calls, on a multithreaded apartment, and
    // the caller may be the engine's main thread, which is whatever the host made it.
    [SupportedOSPlatform("windows")]
    internal static void OnMtaThread(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
        {
            action();
            return;
        }
        var thread = new Thread(() => action()) { IsBackground = true, Name = "Audio endpoints" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
    }
}
