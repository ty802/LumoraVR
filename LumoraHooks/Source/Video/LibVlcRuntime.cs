// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

#if GODOT_PC
using System;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using LibVLCSharp.Shared;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks.Video;

// One process-wide libVLC. Core.Initialize loads the native library and must happen exactly once
// before any LibVLCSharp type is touched, so everything goes through here and a failure is recorded
// as a reason string instead of an exception - a machine without libVLC still has to run, it just
// cannot play anything but Theora. -xlinka
public static class LibVlcRuntime
{
    private static readonly object _sync = new();
    private static bool _attempted;
    private static LibVLC? _instance;

    public static string? LogFilePath { get; private set; }

    public static string? UnavailableReason { get; private set; }

    public static bool IsAvailable => Instance != null;

    // Called at boot so the plugin scan and library load happen on a worker before anyone pastes a
    // link; Instance stays lazy and thread-safe for everything else.
    public static void WarmUp()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try { _ = Instance; }
            catch (Exception ex) { LumoraLogger.Debug($"LibVlcRuntime: warm-up failed: {ex.Message}"); }
        });
    }

    public static LibVLC? Instance
    {
        get
        {
            lock (_sync)
            {
                if (_attempted)
                    return _instance;
                _attempted = true;
                try
                {
                    // Windows takes the directory of the natives we ship beside the assemblies. Linux and
                    // macOS have no package to ship, so null means "whatever libvlc the system installed",
                    // which is how a distro package or a Homebrew VLC gets picked up.
                    string? nativeDir = OperatingSystem.IsWindows() ? FindWindowsNativeDirectory() : null;
                    if (OperatingSystem.IsWindows() && nativeDir == null)
                    {
                        UnavailableReason = "libvlc natives not found next to the executable";
                        LumoraLogger.Warn($"LibVlcRuntime: {UnavailableReason}");
                        return null;
                    }

                    LibVLCSharp.Shared.Core.Initialize(nativeDir);

                    // Without plugins.dat every start scans and loads all hundred-odd plugin DLLs, which
                    // is the multi-second stall the first video paste used to pay on the main thread.
                    // libVLC only writes the cache when told to, so the first run does; later runs read it.
                    var args = new System.Collections.Generic.List<string>
                    {
                        "--intf=dummy",
                        "--no-osd",
                        "--no-video-title-show",
                        "--no-snapshot-preview",
                        "--no-stats",
                        "--verbose=1",
                    };
                    if (nativeDir != null && !File.Exists(Path.Combine(nativeDir, "plugins", "plugins.dat")))
                        args.Add("--reset-plugins-cache");

                    _instance = new LibVLC(args.ToArray());

                    // libVLC writes its own log file. Its managed Log event is not used on purpose: on
                    // Windows LibVLCSharp renders every message (all levels, before any filter) by
                    // pushing libVLC's va_list through msvcrt's vsprintf, and a format that path
                    // mishandles becomes garbage the string marshaller reads off the end of - an
                    // access violation inside the runtime, seconds into any fMP4 stream. -xlinka
                    try
                    {
                        string logDir = ProjectSettings.GlobalizePath("user://logs");
                        Directory.CreateDirectory(logDir);
                        LogFilePath = Path.Combine(logDir, "libvlc.log");
                        _instance.SetLogFile(LogFilePath);
                    }
                    catch (Exception ex)
                    {
                        LumoraLogger.Debug($"LibVlcRuntime: no libVLC log file: {ex.Message}");
                    }

                    LumoraLogger.Log($"LibVlcRuntime: libVLC {_instance.Version} ready (natives: {nativeDir ?? "system"}, log: {LogFilePath ?? "none"})");
                }
                catch (Exception ex)
                {
                    UnavailableReason = ex.Message;
                    _instance = null;
                    LumoraLogger.Warn($"LibVlcRuntime: libVLC unavailable: {ex.Message}");
                }
                return _instance;
            }
        }
    }

    // VideoLAN.LibVLC.Windows drops libvlc/<rid>/ into the build output, and a Godot Windows export
    // copies that whole output into data_<name>_<arch>/ next to the .exe. Both layouts are searched
    // because an editor run and an exported run resolve the assembly directory differently. -xlinka
    private static string? FindWindowsNativeDirectory()
    {
        string rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64",
        };

        foreach (var root in CandidateRoots())
        {
            if (string.IsNullOrEmpty(root))
                continue;
            string ridDir = Path.Combine(root, "libvlc", rid);
            if (File.Exists(Path.Combine(ridDir, "libvlc.dll")))
                return ridDir;
            string flatDir = Path.Combine(root, "libvlc");
            if (File.Exists(Path.Combine(flatDir, "libvlc.dll")))
                return flatDir;
        }
        return null;
    }

    private static string?[] CandidateRoots()
    {
        string? exeDir = null;
        try { exeDir = Path.GetDirectoryName(OS.GetExecutablePath()); }
        catch (Exception ex) { LumoraLogger.Debug($"LibVlcRuntime: executable path unavailable: {ex.Message}"); }

        string? dataDir = null;
        if (!string.IsNullOrEmpty(exeDir))
        {
            try
            {
                foreach (var candidate in Directory.GetDirectories(exeDir!, "data_*"))
                {
                    dataDir = candidate;
                    break;
                }
            }
            catch (Exception ex) { LumoraLogger.Debug($"LibVlcRuntime: no data directory beside the executable: {ex.Message}"); }
        }

        return new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(LibVlcRuntime).Assembly.Location),
            dataDir,
            exeDir,
        };
    }
}
#endif
