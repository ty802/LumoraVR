// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;

namespace Lumora.Source.Godot.Bootstrap;

// Godot splits the command line at "--": engine args before it, user args after. A launch flag is
// written on whichever side the operator happened to put it, so every lookup walks both. Read from Godot
// once: the command line does not change, and some lookups run every frame. -xlinka
public static class LaunchArgs
{
    public const string DebugFlag = "--Lumora-Debug";

    private static string[]? _all;

    public static IEnumerable<string> All() => Args();

    private static string[] Args()
    {
        if (_all != null)
            return _all;
        var engine = OS.GetCmdlineArgs();
        var user = OS.GetCmdlineUserArgs();
        var all = new string[engine.Length + user.Length];
        engine.CopyTo(all, 0);
        user.CopyTo(all, engine.Length);
        _all = all;
        return all;
    }

    public static bool HasFlag(string flag)
    {
        foreach (var arg in Args())
        {
            if (arg.Trim().Equals(flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // --flag=value. Null when the flag is absent; quotes around the value are stripped.
    public static string? ReadValue(string flag)
    {
        var prefix = flag + "=";
        foreach (var raw in Args())
        {
            var arg = raw.Trim();
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg.Substring(prefix.Length).Trim('"');
        }
        return null;
    }
}
