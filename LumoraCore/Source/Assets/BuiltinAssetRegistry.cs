// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core.Assets;

// The engine's own assets (fonts, shaders, icons) live inside the build and are named res://. A save
// that points at one of them by that name only works on a build that has the same file, so the set
// is published to the content service once, and this registry holds the result: which hash each
// res:// path is. Saving rewrites res:// to the hash, and loading maps a known hash straight back to
// the local file, so a built-in asset never downloads and a save never depends on a build. -xlinka
public static class BuiltinAssetRegistry
{
    public const string MapFileName = "builtins.json";
    private static readonly string[] Roots = { "Assets/Fonts", "Assets/Shaders", "Shaders", "Icons" };
    private static readonly string[] Extensions = { ".ttf", ".otf", ".gdshader", ".png", ".jpg", ".webp", ".svg" };

    private static readonly Dictionary<string, string> _hashByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _pathByHash = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _gate = new();
    private static bool _loaded;

    public static int Count { get { lock (_gate) return _hashByPath.Count; } }

    // The map ships next to the resources; no map means nothing is rewritten, which is the state of a
    // build that was never published.
    public static void Load(string? resourceRoot)
    {
        lock (_gate)
        {
            _loaded = true;
            _hashByPath.Clear();
            _pathByHash.Clear();
            if (string.IsNullOrEmpty(resourceRoot))
                return;
            var file = Path.Combine(resourceRoot, MapFileName);
            if (!File.Exists(file))
                return;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    var hash = entry.Value.GetString();
                    if (string.IsNullOrEmpty(hash))
                        continue;
                    _hashByPath[entry.Name] = hash!;
                    _pathByHash[hash!] = entry.Name;
                }
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"BuiltinAssetRegistry: could not read {file}: {ex.Message}");
            }
        }
    }

    private static void EnsureLoaded()
    {
        bool loaded;
        lock (_gate) loaded = _loaded;
        if (!loaded)
            Load(Engine.Current?.ResourceRoot);
    }

    // res://Assets/Fonts/Lato/Lato-Regular.ttf -> the hash it was published under, or null.
    public static string? HashFor(string resUri)
    {
        EnsureLoaded();
        var key = Normalize(resUri);
        if (key == null)
            return null;
        lock (_gate)
            return _hashByPath.TryGetValue(key, out var hash) ? hash : null;
    }

    // A published hash -> the res:// URI of the file this build carries, or null.
    public static string? ResUriFor(string hash)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(hash))
            return null;
        lock (_gate)
            return _pathByHash.TryGetValue(hash, out var path) ? "res://" + path : null;
    }

    private static string? Normalize(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;
        string path;
        if (uri.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            path = uri.Substring(6);
        else if (uri.StartsWith("lumres://", StringComparison.OrdinalIgnoreCase))
            path = uri.Substring(9);
        else
            return null;
        return path.Replace('\\', '/').TrimStart('/');
    }

    // PUBLISHING: walks the built-in asset folders, pushes each file up by hash under the system
    // owner, and writes the map beside the resources. Run once per build with the worker key.
    public static async Task<(int published, int failed)> PublishAsync(LumoraClient client, string workerKey, string resourceRoot, Action<string>? log = null)
    {
        int published = 0, failed = 0;
        var map = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots)
        {
            var dir = Path.Combine(resourceRoot, root);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (Array.IndexOf(Extensions, ext.ToLowerInvariant()) < 0)
                    continue;
                byte[] bytes;
                try { bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false); }
                catch (Exception ex) { failed++; log?.Invoke($"skip {file}: {ex.Message}"); continue; }
                var stored = await client.WorkerStoreBlob(workerKey, bytes, ext).ConfigureAwait(false);
                if (stored.Failed || string.IsNullOrEmpty(stored.Data))
                {
                    failed++;
                    log?.Invoke($"failed {file}: {stored.Message ?? "no answer"}");
                    continue;
                }
                var rel = Path.GetRelativePath(resourceRoot, file).Replace('\\', '/');
                map[rel] = stored.Data!;
                published++;
                log?.Invoke($"{rel} -> {stored.Data}");
            }
        }
        var target = Path.Combine(resourceRoot, MapFileName);
        await File.WriteAllTextAsync(target, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        Load(resourceRoot);
        return (published, failed);
    }
}
