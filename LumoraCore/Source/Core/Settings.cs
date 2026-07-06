// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Lumora.Core.Persistence;

namespace Lumora.Core;

/// <summary>
/// General hierarchical key-value config store (the engine's small "settings database").
/// Keys are dotted paths (e.g.
/// <c>Security.Ban.Blacklist.<id>.UserId</c>); subsystems that need durable config - bans,
/// per-world security, etc. - read/write here instead of inventing their own files. Persisted as a
/// single JSON map under the user's application data folder. Typed user/video/audio prefs stay in
/// <see cref="EngineSettings"/>; this is for arbitrary keyed data.
/// </summary>
public static class Settings
{
    private static readonly object _lock = new();
    private static Dictionary<string, string>? _values;

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lumora", "config.dat");

    public static void WriteValue(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_lock)
        {
            EnsureLoaded();
            _values![key] = value ?? "";
            Save();
        }
    }

    public static void WriteValue<T>(string key, T value)
        => WriteValue(key, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    public static T ReadValue<T>(string key, T fallback = default!)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (!_values!.TryGetValue(key, out var raw) || raw == null)
                return fallback;
            try
            {
                if (typeof(T) == typeof(string))
                    return (T)(object)raw;
                return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }
    }

    public static bool HasValue(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _values!.ContainsKey(key);
        }
    }

    public static void DeleteValue(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (_values!.Remove(key))
                Save();
        }
    }

    /// <summary>Distinct immediate child segment names directly under <paramref name="root"/>.</summary>
    public static IEnumerable<string> ListSettings(string root)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var prefix = root.EndsWith('.') ? root : root + ".";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in _values!.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                int dot = key.IndexOf('.', prefix.Length);
                var segment = dot < 0 ? key.Substring(prefix.Length) : key.Substring(prefix.Length, dot - prefix.Length);
                if (segment.Length > 0)
                    seen.Add(segment);
            }
            return seen;
        }
    }

    /// <summary>Remove every key at or under <paramref name="root"/>.</summary>
    public static void ClearSettings(string root)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var prefix = root + ".";
            var toRemove = new List<string>();
            foreach (var key in _values!.Keys)
            {
                if (key == root || key.StartsWith(prefix, StringComparison.Ordinal))
                    toRemove.Add(key);
            }
            if (toRemove.Count == 0)
                return;
            foreach (var key in toRemove)
                _values.Remove(key);
            Save();
        }
    }

    private static void EnsureLoaded()
    {
        if (_values != null) return;
        _values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var path = StorePath;
            if (!File.Exists(path)) return;
            // Stored in the engine's binary data-tree format (same as world saves), not plaintext.
            if (DataTreeConverter.LoadFromBytes(File.ReadAllBytes(path)) is DataTreeDictionary dictionary)
            {
                foreach (var pair in dictionary.Children)
                    if (pair.Value is DataTreeValue value)
                        _values[pair.Key] = value.Extract<string>() ?? "";
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Settings: failed to load: {ex.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            var path = StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var dictionary = new DataTreeDictionary();
            foreach (var pair in _values!)
                dictionary.Add(pair.Key, pair.Value);
            File.WriteAllBytes(path, DataTreeConverter.SaveToBytes(dictionary));
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Settings: failed to save: {ex.Message}");
        }
    }
}
