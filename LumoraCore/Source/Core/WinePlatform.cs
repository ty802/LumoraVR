// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

// Whether this Windows build is actually a Windows build.
//
// Under Wine, and therefore under Proton, EVERY question the runtime can answer about the platform
// comes back "Windows". OperatingSystem.IsWindows() is true, Environment.OSVersion says Win32NT,
// Godot's OS.GetName() says "Windows", and none of it is a lie exactly: the process really is running
// against the Win32 API. It is still the wrong answer for anything that reaches past that API at a
// real machine, and we have three of those already:
//
//   - A native vendor runtime like SRanipal is a Windows SERVICE on a Windows MACHINE. Under Proton
//     the service does not exist and never will, so probing for it is time spent on nothing.
//   - Process.GetProcessesByName sees the inside of the prefix. SteamVR's vrserver under Proton is a
//     native Linux process and is simply not in that list, so "is a VR runtime up" always reads no.
//   - The registry we read for the active OpenXR runtime is the PREFIX'S registry, which may or may
//     not have been mapped through from the host.
//
// The one thing that gives the game away is Wine's own ntdll exports, which is what everybody uses.
// Asked once, cached, never asked again.
//
// TWO DELIBERATE DEPARTURES FROM HOW EVERYONE ELSE WRITES THIS, both cheap:
//
// The exports are looked up with NativeLibrary.TryGetExport rather than declared as DllImports. The
// usual shape is a DllImport in a try/catch, which means every single boot on a real Windows machine
// throws and swallows an EntryPointNotFoundException - measured at 16 ms here, and a first-chance
// exception that every attached debugger stops on and every crash reporter writes down, forever, to
// learn something that was never in doubt. A bool is a better way to ask a question whose answer is
// usually no.
//
// The functions return IntPtr, not string. A P/Invoke that returns `string` has the marshaller free
// the native buffer with CoTaskMemFree afterwards, and these point at static strings inside ntdll
// that nobody is allowed to free. -xlinka
public static class WinePlatform
{
    public static bool IsWine { get; }

    // Wine's reported version, e.g. "9.0". Null when not on Wine.
    public static string? Version { get; }

    // The REAL operating system underneath, from wine_get_host_version: "Linux", "Darwin", and the
    // kernel release. Older Wine builds do not export it, so this can be null while IsWine is true.
    public static string? HostSystem { get; }
    public static string? HostRelease { get; }

    // Proton is Wine plus Steam's runtime, and Steam marks its compatibility tool processes with
    // these. Nothing here changes on the answer; it is worth knowing which of the two you are looking
    // at when a bug report says "Linux".
    public static bool IsProton { get; }

    // Pretend. Every branch that turns on IsWine is, by construction, a branch a Windows machine can
    // never reach, so without this the only way to exercise any of them is to go and boot Linux. The
    // flag says "simulated" everywhere it is printed so nobody reads a test run as a real one.
    public static bool IsSimulated { get; }

    private const string SimulateFlag = "--simulate-wine";

    static WinePlatform()
    {
        // Read from the raw process command line rather than a launcher helper: this type is in the
        // core, runs from a static constructor, and must not depend on anything being up yet.
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (!string.Equals(arg?.Trim(), SimulateFlag, StringComparison.OrdinalIgnoreCase))
                continue;
            IsWine = true;
            IsSimulated = true;
            Version = "simulated";
            HostSystem = "Linux";
            HostRelease = "simulated";
            IsProton = true;
            return;
        }

        // There is no ntdll to ask on a platform that does not even claim to be Windows, and a real
        // Linux or Android build is not running under Wine by definition.
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // ntdll is always already mapped into a Windows process; this resolves the existing
            // handle rather than loading anything.
            if (!NativeLibrary.TryLoad("ntdll.dll", out var ntdll))
                return;

            // The one export that only exists on Wine. No Wine, no answer, no exception.
            if (!NativeLibrary.TryGetExport(ntdll, "wine_get_version", out var getVersion))
                return;

            var version = Marshal.GetDelegateForFunctionPointer<WineGetVersion>(getVersion)();
            if (version == IntPtr.Zero)
                return;

            Version = Marshal.PtrToStringAnsi(version);
            IsWine = true;

            // Exported since Wine 1.x but never promised, so its absence is not the end of anything.
            if (NativeLibrary.TryGetExport(ntdll, "wine_get_host_version", out var getHost))
            {
                Marshal.GetDelegateForFunctionPointer<WineGetHostVersion>(getHost)(out var sysname, out var release);
                if (sysname != IntPtr.Zero)
                    HostSystem = Marshal.PtrToStringAnsi(sysname);
                if (release != IntPtr.Zero)
                    HostRelease = Marshal.PtrToStringAnsi(release);
            }
        }
        catch
        {
            // Nothing here is worth failing startup over. Not Wine, as far as anyone is concerned.
            return;
        }

        IsProton =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH"));
    }

    // One line for a log or a bug report. "Windows" when this is a real one.
    public static string Describe()
    {
        if (!IsWine)
            return OperatingSystem.IsWindows() ? "Windows (native)" : "not Windows";

        if (IsSimulated)
            return $"SIMULATED Wine ({SimulateFlag}); this is really Windows";

        var layer = IsProton ? "Proton" : "Wine";
        var host = HostSystem == null
            ? "unknown host"
            : string.IsNullOrEmpty(HostRelease) ? HostSystem : $"{HostSystem} {HostRelease}";
        return $"{layer} {Version ?? "?"} on {host}";
    }

    private static bool _logged;

    // Says it once, from wherever notices it first.
    public static void LogOnce()
    {
        if (_logged)
            return;
        _logged = true;

        if (!IsWine)
            return;

        LumoraLogger.Log($"Platform: running under {Describe()}. Windows-only host integrations are off; " +
                         "host processes and the host registry are not visible from inside the prefix.");
    }

    // const char * CDECL wine_get_version(void)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr WineGetVersion();

    // void CDECL wine_get_host_version(const char **sysname, const char **release)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WineGetHostVersion(out IntPtr sysname, out IntPtr release);
}
