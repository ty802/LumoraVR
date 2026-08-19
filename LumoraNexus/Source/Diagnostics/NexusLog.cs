// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Nexus.Diagnostics;

public enum NexusLogLevel
{
    Log,
    Warn,
    Error,
    Debug
}

// Logging seam for this assembly. Nexus sits below the engine and cannot see the engine's logger,
// so every line goes through a swappable sink instead. The host installs one at startup and the
// lines land in the same file/console as everything else; standalone consumers (tools, a headless
// relay, tests) get the console default and need no wiring at all. -xlinka
public static class NexusLog
{
    // Assign at startup, before any transport spins up. Null restores the console default rather
    // than silently dropping lines.
    public static Action<NexusLogLevel, string> Sink
    {
        get => _sink;
        set => _sink = value ?? WriteConsole;
    }

    private static Action<NexusLogLevel, string> _sink = WriteConsole;

    public static bool EnableDebug { get; set; }

    public static void Log(string message) => _sink(NexusLogLevel.Log, message);

    public static void Warn(string message) => _sink(NexusLogLevel.Warn, message);

    public static void Error(string message) => _sink(NexusLogLevel.Error, message);

    public static void Debug(string message)
    {
        if (EnableDebug)
            _sink(NexusLogLevel.Debug, message);
    }

    private static void WriteConsole(NexusLogLevel level, string message)
    {
        // A sink must never throw back into the transport thread that emitted the line.
        try
        {
            if (level == NexusLogLevel.Error)
                Console.Error.WriteLine($"[Nexus/{level}] {message}");
            else
                Console.WriteLine($"[Nexus/{level}] {message}");
        }
        catch (Exception)
        {
        }
    }
}
