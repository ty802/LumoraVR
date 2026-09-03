// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core.Persistence;

// One asset the save depends on, by content hash. What the manifest carries and what the service is
// told about, so a texture used by ten saves is stored once and counted once.
public sealed class CloudAssetEntry
{
    public string Hash = "";
    public string Name = "";
    public string Extension = "";
    public string Type = "other";
    public long SizeBytes;
}

// A saved graph references its textures, meshes and clips by URL, and on the machine that made it
// those are local:// records in the asset database. A save that leaves the machine cannot carry those
// URLs, so before the graph is uploaded every local:// asset is uploaded by its own hash (skipped
// when the store already has it) and the URL is rewritten to lumora:///<hash>. Loading does the
// reverse: every lumora:/// asset the graph names is brought into the local database before the
// graph is built, and the provider resolves the URL to that file. Hashes are SHA-256 of the bytes on
// both sides, so the local record and the cloud blob are the same name for the same thing. -xlinka
public static class CloudAssetPacker
{
    public const string ManifestKey = "CloudAssets";
    public const string CloudScheme = "lumora";

    // Uploads every local:// asset the tree references, rewrites the URLs, and writes the manifest into
    // the tree. Returns the manifest. Missing local files are left as they are and reported.
    public static async Task<List<CloudAssetEntry>> PackAsync(DataTreeDictionary root, LumoraClient client, LocalDB? db, List<string>? problems = null)
    {
        var manifest = new List<CloudAssetEntry>();
        var byUri = new Dictionary<string, string>(StringComparer.Ordinal);
        var locals = new List<string>();
        CollectUrls(root, "local", locals);

        foreach (var uri in locals)
        {
            if (byUri.ContainsKey(uri))
                continue;
            var path = db?.GetFilePath(uri);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                problems?.Add($"asset missing on this machine: {uri}");
                continue;
            }
            var stored = await client.StoreContent(path).ConfigureAwait(false);
            if (stored.Failed || stored.Data == null)
            {
                problems?.Add($"upload failed for {Path.GetFileName(path)}: {stored.Message ?? "no answer"}");
                continue;
            }
            byUri[uri] = stored.Data.Hash;
            var ext = Path.GetExtension(path) ?? string.Empty;
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }
            manifest.Add(new CloudAssetEntry
            {
                Hash = stored.Data.Hash,
                Name = Path.GetFileNameWithoutExtension(path) ?? stored.Data.Hash,
                Extension = ext,
                Type = TypeOf(ext),
                SizeBytes = size,
            });
        }

        // Built-in assets the build shipped go by their published hash too, so the save stands on
        // its own; a build that was never published leaves them as they are.
        var builtins = new List<string>();
        CollectUrls(root, "res", builtins);
        CollectUrls(root, "lumres", builtins);
        foreach (var uri in builtins)
        {
            if (byUri.ContainsKey(uri))
                continue;
            var hash = Assets.BuiltinAssetRegistry.HashFor(uri);
            if (hash != null)
                byUri[uri] = hash;
        }

        if (byUri.Count > 0)
            Rewrite(root, uri => byUri.TryGetValue(uri, out var hash) ? new Uri($"{CloudScheme}:///{hash}") : null);

        var list = new DataTreeList();
        foreach (var entry in manifest)
        {
            var node = new DataTreeDictionary();
            node.Add("hash", entry.Hash);
            node.Add("name", entry.Name);
            node.Add("ext", entry.Extension);
            node.Add("type", entry.Type);
            node.Add("size", entry.SizeBytes);
            list.Add(node);
        }
        root.AddOrUpdate(ManifestKey, list);
        return manifest;
    }

    public static List<CloudAssetEntry> ReadManifest(DataTreeDictionary root)
    {
        var manifest = new List<CloudAssetEntry>();
        var list = root.TryGetList(ManifestKey);
        if (list == null)
            return manifest;
        foreach (var node in list)
        {
            if (node is not DataTreeDictionary d)
                continue;
            manifest.Add(new CloudAssetEntry
            {
                Hash = d.ExtractOrDefault("hash", string.Empty),
                Name = d.ExtractOrDefault("name", string.Empty),
                Extension = d.ExtractOrDefault("ext", string.Empty),
                Type = d.ExtractOrDefault("type", "other"),
                SizeBytes = d.ExtractOrDefault("size", 0L),
            });
        }
        return manifest;
    }

    // Brings every asset the tree names into the local database, skipping the ones already there.
    // Returns how many came down; problems collect the ones that did not.
    public static async Task<int> PrefetchAsync(DataTreeDictionary root, LumoraClient client, LocalDB db, List<string>? problems = null)
    {
        var manifest = ReadManifest(root);
        var extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest)
            extensions[entry.Hash] = entry.Extension;

        var cloud = new List<string>();
        CollectUrls(root, CloudScheme, cloud);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in cloud)
        {
            var hash = HashOf(uri);
            if (!string.IsNullOrEmpty(hash))
                wanted.Add(hash);
        }

        int fetched = 0;
        foreach (var hash in wanted)
        {
            if (db.Exists($"local://cloud/{hash}"))
                continue;
            var result = await client.FetchContent(hash).ConfigureAwait(false);
            if (result.Failed || result.Data == null)
            {
                problems?.Add($"could not fetch {hash}: {result.Message ?? "no answer"}");
                continue;
            }
            var ext = extensions.TryGetValue(hash, out var known) && !string.IsNullOrEmpty(known) ? known : ".bin";
            await db.SaveAssetAsync(result.Data, ext).ConfigureAwait(false);
            fetched++;
        }
        return fetched;
    }

    // The hash a lumora:///<hash> URL names, or null for anything else.
    public static string? HashOf(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith(CloudScheme + ":", StringComparison.Ordinal))
            return null;
        var trimmed = uri.Substring(CloudScheme.Length + 1).Trim('/');
        int slash = trimmed.IndexOf('/');
        if (slash >= 0)
            trimmed = trimmed.Substring(slash + 1);
        int query = trimmed.IndexOf('?');
        if (query >= 0)
            trimmed = trimmed.Substring(0, query);
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static string TypeOf(string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".png": case ".jpg": case ".jpeg": case ".webp": case ".tga": case ".bmp": case ".ktx": case ".ktx2": case ".dds": case ".exr": case ".hdr":
                return "texture";
            case ".lmesh": case ".obj": case ".fbx": case ".glb": case ".gltf":
                return "mesh";
            case ".wav": case ".mp3": case ".ogg": case ".flac":
                return "audio";
            case ".lanm":
                return "animation";
            case ".lumshader": case ".gdshader":
                return "shader";
            default:
                return "other";
        }
    }

    // TREE WALKS

    private static void CollectUrls(DataTreeNode node, string scheme, List<string> into)
    {
        switch (node)
        {
            case DataTreeDictionary d:
                foreach (var child in d.Children.Values)
                    CollectUrls(child, scheme, into);
                break;
            case DataTreeList l:
                foreach (var child in l.Children)
                    CollectUrls(child, scheme, into);
                break;
            case DataTreeValue v when v.IsUrl:
            {
                var text = ((string)v.Value!).Substring(1);
                if (text.StartsWith(scheme + ":", StringComparison.Ordinal))
                    into.Add(text);
                break;
            }
        }
    }

    // A value node cannot change what it holds, so a rewritten URL is a new node in the old node's place.
    private static void Rewrite(DataTreeNode node, Func<string, Uri?> map)
    {
        switch (node)
        {
            case DataTreeDictionary d:
            {
                var keys = new List<string>(d.Children.Keys);
                foreach (var key in keys)
                {
                    var child = d.Children[key];
                    if (child is DataTreeValue v && v.IsUrl)
                    {
                        var replacement = map(((string)v.Value!).Substring(1));
                        if (replacement != null)
                            d.Children[key] = new DataTreeValue(replacement);
                    }
                    else
                    {
                        Rewrite(child, map);
                    }
                }
                break;
            }
            case DataTreeList l:
            {
                for (int i = 0; i < l.Children.Count; i++)
                {
                    var child = l.Children[i];
                    if (child is DataTreeValue v && v.IsUrl)
                    {
                        var replacement = map(((string)v.Value!).Substring(1));
                        if (replacement != null)
                            l.Children[i] = new DataTreeValue(replacement);
                    }
                    else
                    {
                        Rewrite(child, map);
                    }
                }
                break;
            }
        }
    }
}
