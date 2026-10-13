// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Lumora.Core.Persistence;

namespace Lumora.Core;

// The engine's settings database: one key-value store for everything durable, keys as dotted paths
// (Engine.Video.VSync, Security.Ban.Blacklist.<id>.UserId, WebAccess.Sites.<host>). Subsystems keep
// what they need here instead of inventing files of their own. Saved as one data tree in config.dat in
// the data folder.
//
// Some keys follow the account rather than the machine (AccountSettings says which). Those carry the
// time they last changed, and a removed one leaves that time behind as a tombstone, so another computer
// signed into the same account can tell which side is newer and a removal reaches it too. -xlinka
public static class Settings
{
    // Nested in the same file under names no setting can have, which also keeps them out of the way of
    // builds that only read plain values.
    private const string ChangedNode = "@changed";
    private const string RemovedNode = "@removed";

    private static readonly object _lock = new();
    private static Dictionary<string, string>? _values;
    private static Dictionary<string, long> _changed = new(StringComparer.Ordinal);
    private static HashSet<string> _removed = new(StringComparer.Ordinal);

    // A key whose value changed: by this machine (fromAccount false) or from the account (true).
    public static event Action<string, bool>? Changed;

    private static string StorePath => PathResolver.Resolve("config.dat");

    // One account key as stored: Value null when it was removed, Changed in Unix milliseconds.
    public readonly record struct AccountEntry(string Key, string? Value, long Changed);

    public static void WriteValue(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        value ??= "";
        bool changed;
        lock (_lock)
        {
            EnsureLoaded();
            changed = !_values!.TryGetValue(key, out var old) || old != value;
            _values[key] = value;
            if (changed && AccountSettings.Follows(key))
            {
                _changed[key] = Now();
                _removed.Remove(key);
            }
            Save();
        }
        if (changed)
            Changed?.Invoke(key, false);
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
        bool removed;
        lock (_lock)
        {
            EnsureLoaded();
            removed = RemoveLocked(key);
            if (removed)
                Save();
        }
        if (removed)
            Changed?.Invoke(key, false);
    }

    // Distinct immediate child segment names directly under root.
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

    // Every whole key that starts with prefix, for keys whose last part has dots of its own (a host name).
    public static List<string> KeysUnder(string prefix)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var keys = new List<string>();
            foreach (var key in _values!.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    keys.Add(key);
            }
            return keys;
        }
    }

    // Remove every key at or under root.
    public static void ClearSettings(string root)
    {
        var removed = new List<string>();
        lock (_lock)
        {
            EnsureLoaded();
            var prefix = root + ".";
            foreach (var key in new List<string>(_values!.Keys))
            {
                if ((key == root || key.StartsWith(prefix, StringComparison.Ordinal)) && RemoveLocked(key))
                    removed.Add(key);
            }
            if (removed.Count == 0)
                return;
            Save();
        }
        foreach (var key in removed)
            Changed?.Invoke(key, false);
    }

    private static bool RemoveLocked(string key)
    {
        if (!_values!.Remove(key))
            return false;
        if (AccountSettings.Follows(key))
        {
            _changed[key] = Now();
            _removed.Add(key);
        }
        return true;
    }

    // ACCOUNT

    // Every account key this machine has a time for, removals included.
    public static List<AccountEntry> AccountEntries()
    {
        lock (_lock)
        {
            EnsureLoaded();
            var entries = new List<AccountEntry>(_changed.Count);
            foreach (var (key, changed) in _changed)
            {
                if (!AccountSettings.Follows(key))
                    continue;
                entries.Add(new AccountEntry(key, _removed.Contains(key) ? null : _values!.GetValueOrDefault(key), changed));
            }
            return entries;
        }
    }

    // Takes an entry from the account if it is newer than what this machine has. True when it changed
    // anything here.
    public static bool ApplyAccountEntry(AccountEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Key) || !AccountSettings.Follows(entry.Key))
            return false;
        bool changed;
        lock (_lock)
        {
            EnsureLoaded();
            if (_changed.TryGetValue(entry.Key, out long mine) && mine >= entry.Changed)
                return false;
            _changed[entry.Key] = entry.Changed;
            if (entry.Value == null)
            {
                changed = _values!.Remove(entry.Key);
                _removed.Add(entry.Key);
            }
            else
            {
                changed = !_values!.TryGetValue(entry.Key, out var old) || old != entry.Value;
                _values[entry.Key] = entry.Value;
                _removed.Remove(entry.Key);
            }
            Save();
        }
        if (changed)
            Changed?.Invoke(entry.Key, true);
        return changed;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // FILE

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
                {
                    if (pair.Value is DataTreeValue value)
                        _values[pair.Key] = value.Extract<string>() ?? "";
                }
                if (dictionary.Children.TryGetValue(ChangedNode, out var changed) && changed is DataTreeDictionary times)
                {
                    foreach (var pair in times.Children)
                    {
                        if (pair.Value is DataTreeValue time && long.TryParse(time.Extract<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long at))
                            _changed[pair.Key] = at;
                    }
                }
                if (dictionary.Children.TryGetValue(RemovedNode, out var removed) && removed is DataTreeDictionary gone)
                {
                    foreach (var key in gone.Children.Keys)
                        _removed.Add(key);
                }
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
            if (_changed.Count > 0)
            {
                var times = new DataTreeDictionary();
                foreach (var pair in _changed)
                    times.Add(pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));
                dictionary.Add(ChangedNode, times);
            }
            if (_removed.Count > 0)
            {
                var gone = new DataTreeDictionary();
                foreach (var key in _removed)
                    gone.Add(key, "1");
                dictionary.Add(RemovedNode, gone);
            }
            File.WriteAllBytes(path, DataTreeConverter.SaveToBytes(dictionary));
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Settings: failed to save: {ex.Message}");
        }
    }
}
