// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Lumora.Core.Persistence;

// Every file the engine writes lives under ONE folder: logs, the local asset database, the content
// cache, config, the local home, keys, temp files, the video tools. The folder is Godot's user://
// directory, which the project pins to a custom name so it reads <platform data dir>/LumoraVR instead
// of the stock <platform data dir>/Godot/app_userdata/LumoraVR. Godot's own log and shader cache land
// in the same place that way, so there is exactly one folder to open and one folder to wipe.
//
// The static default mirrors what Godot resolves on each desktop platform, so anything that runs
// before the bootstrap autoload, or outside Godot entirely like the check harnesses, agrees with it.
// Android has no stable rule from managed code, so there the bootstrap's Initialize call is the only
// source of truth. -xlinka
public static class PathResolver
{
    public const string FolderName = "LumoraVR";

    private static bool _initialized;

    public static string Root { get; private set; } = DefaultRoot();

    public static bool IsInitialized => _initialized;

    // The three-way split older code grew up on. All of them mean the same folder now.
    public static string LocalPath => Root;
    public static string RoamingPath => Root;
    public static string CachePath => Root;

    public static string LogsPath => Resolve("logs");
    public static string TempPath => Resolve("tmp");

    public static string Resolve(params string[] parts)
    {
        if (parts == null || parts.Length == 0)
            return Root;
        var all = new string[parts.Length + 1];
        all[0] = Root;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return Path.Combine(all);
    }

    public static void Initialize(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("A data folder is required", nameof(root));
        if (_initialized)
            throw new InvalidOperationException("PathResolver was already initialized");
        Root = Path.GetFullPath(root.TrimEnd('/', '\\'));
        _initialized = true;
    }

    // The old entry point. The first folder is the one that survives; the other two were never
    // anything but the same folder under a different name once everything moved in together.
    public static void initialize(string localPath, string roamingPath, string cachePath) => Initialize(localPath);

    private static string DefaultRoot()
    {
        try
        {
            string? baseDir;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support");
            }
            else
            {
                baseDir = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            }
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Path.GetTempPath();
            return Path.Combine(baseDir, FolderName);
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), FolderName);
        }
    }
}
