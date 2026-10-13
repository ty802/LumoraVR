// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using Lumora.Nexus.Cloud.Cdn;
using Lumora.Script;

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
    // The local:// address it was uploaded from, when it came from this machine.
    public string LocalUri = "";
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
    // the tree. Returns the manifest. The upload is handed in: a file to the hash it was stored under, or
    // no hash and why not.
    //
    // A file this machine no longer has can never go up from here, so the save goes up without it: its URL
    // is cleared, a script constant naming it is blanked, and it is named in dropped for the person saving.
    // A file that is here but would not upload is a problem instead: the tree is left as it was and the
    // caller fails the save, which can be tried again. -xlinka
    public static Task<List<CloudAssetEntry>> PackAsync(DataTreeDictionary root, Func<string, Task<(string? Hash, string? Error)>> upload,
        LocalDB? db, List<string>? problems = null, List<string>? dropped = null)
    {
        var packing = new Packing(async (_, path) =>
        {
            var (hash, error) = await upload(path).ConfigureAwait(false);
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }
            return (hash, size, error);
        }, db);
        return PackAsync(root, packing, problems, dropped, null);
    }

    // The same through a sink, the way the inventory sync sends a save up. Every file is read through the
    // asset database, so one kept encrypted on this machine goes up as its plain bytes, and before anything
    // is sent the store is asked once which of the files it lacks: the others are not sent at all. -xlinka
    public static Task<List<CloudAssetEntry>> PackAsync(DataTreeDictionary root, ICloudUploadSink sink, LocalDB db,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyCollection<string>?>>? missing, List<string>? problems,
        List<string>? dropped, CancellationToken ct)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packing = new Packing(async (source, path) =>
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await db.ReadAssetBytesAsync(source).ConfigureAwait(false);
            if (bytes == null)
                return (null, 0L, "could not be read on this machine");
            var hash = ContentHash.FromBytes(bytes);
            if (present.Contains(hash))
                return (hash, bytes.LongLength, null);
            var (stored, error) = await sink.StoreAsync(bytes, Path.GetExtension(path) ?? string.Empty, ct).ConfigureAwait(false);
            return (stored, bytes.LongLength, error);
        }, db);
        return PackAsync(root, packing, problems, dropped, missing == null ? null : async found =>
        {
            // A record is filed under the hash of its bytes, so the question needs no reading; the
            // upload still hashes what it reads, and only an answer for that hash skips a file.
            var asked = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var uri in found)
            {
                var key = uri.Substring(uri.IndexOf('/', 8) + 1);
                if (IsContentHash(key) && seen.Add(key))
                    asked.Add(key);
            }
            if (asked.Count == 0)
                return;
            var lacking = await missing(asked, ct).ConfigureAwait(false);
            if (lacking == null)
                return;
            var gone = new HashSet<string>(lacking, StringComparer.OrdinalIgnoreCase);
            foreach (var key in asked)
            {
                if (!gone.Contains(key))
                    present.Add(key);
            }
        });
    }

    // A save's files as this machine sees them: the ones it no longer has, named the way the person saving
    // knows them, and what the ones it has weigh. It only looks: nothing is uploaded, nothing stored again.
    public static async Task<(List<string> Missing, long LocalBytes)> CheckFilesAsync(DataTreeDictionary root, LocalDB db)
    {
        var packing = new Packing((_, _) => Task.FromResult<(string?, long, string?)>((null, 0L, "not sent")), db);
        await FindAsync(root, packing).ConfigureAwait(false);
        var named = new List<string>(packing.Missing.Count);
        if (packing.Missing.Count > 0)
        {
            var uses = new Dictionary<string, string>(StringComparer.Ordinal);
            DescribeUses(root, null, null, uses);
            foreach (var uri in packing.Missing)
                named.Add(packing.NameOf(uri, uses));
        }
        return (named, packing.FoundBytes());
    }

    // Every local file the tree names, as a URL or as a constant on an open script node, looked up.
    private static async Task<(List<string> Locals, List<(DataTreeDictionary Entry, string Uri)> Constants)> FindAsync(
        DataTreeDictionary root, Packing packing)
    {
        var locals = new List<string>();
        CollectUrls(root, "local", locals);

        // An open script graph keeps its constants on its node slots, each the JSON of one value.
        var constants = new List<(DataTreeDictionary Entry, string Uri)>();
        var entries = new List<(DataTreeDictionary Entry, string? Node)>();
        CollectNodeValues(root, null, entries);
        foreach (var (entry, node) in entries)
        {
            if (entry.TryGetNode("Json") is not DataTreeValue { IsUrl: false, Value: string } json)
                continue;
            GraphValue value;
            try
            {
                value = GraphValue.ParseJson(json.Extract<string>());
            }
            catch (GraphFormatException)
            {
                continue;
            }
            if (value.Kind != GraphValueKind.String || !IsLocalAddress(value.String))
                continue;
            constants.Add((entry, value.String!));
            packing.NameIn(value.String!, node != null ? $"a file named on the node \"{node}\"" : "a file named on a script node");
        }

        foreach (var uri in locals)
            await packing.FindAsync(uri).ConfigureAwait(false);
        foreach (var (_, uri) in constants)
            await packing.FindAsync(uri).ConfigureAwait(false);
        return (locals, constants);
    }

    private static async Task<List<CloudAssetEntry>> PackAsync(DataTreeDictionary root, Packing packing, List<string>? problems,
        List<string>? dropped, Func<IReadOnlyList<string>, Task>? beforeUpload)
    {
        var (locals, constants) = await FindAsync(root, packing).ConfigureAwait(false);
        if (beforeUpload != null)
            await beforeUpload(packing.Found).ConfigureAwait(false);
        foreach (var uri in locals)
            await packing.UploadAsync(uri).ConfigureAwait(false);
        foreach (var (_, uri) in constants)
            await packing.UploadAsync(uri).ConfigureAwait(false);

        if (packing.Problems.Count > 0 || packing.Missing.Count > 0)
        {
            var uses = new Dictionary<string, string>(StringComparer.Ordinal);
            DescribeUses(root, null, null, uses);
            foreach (var (uri, why) in packing.Problems)
                problems?.Add($"{packing.NameOf(uri, uses)} {why}");
            if (packing.Problems.Count > 0)
                return new List<CloudAssetEntry>();
            foreach (var uri in packing.Missing)
                dropped?.Add(packing.NameOf(uri, uses));
        }

        var gone = new HashSet<string>(packing.Missing, StringComparer.Ordinal);
        var byUri = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (uri, hash) in packing.Hashes)
        {
            if (hash != null)
                byUri[uri] = hash;
        }
        foreach (var (entry, uri) in constants)
        {
            if (byUri.TryGetValue(uri, out var hash))
                entry.Children["Json"] = new DataTreeValue(GraphValue.Of($"{CloudScheme}:///{hash}").ToJson());
            else if (gone.Contains(uri))
                entry.Children["Json"] = new DataTreeValue(string.Empty);
        }
        if (gone.Count > 0)
            Clear(root, gone);

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

        var manifest = packing.Manifest;
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

    // Brings every asset the tree names into the local database, skipping the ones already there. A
    // texture whose uploader registered its variants comes down as its sidecar and the smallest rung a
    // load starts on instead of the original: the load puts that up at once and fetches a bigger rung,
    // or the original, only when it is asked for one. Returns how many came down; problems collect the
    // ones that did not. -xlinka
    public static Task<int> PrefetchAsync(DataTreeDictionary root, LumoraClient client, LocalDB db, List<string>? problems = null)
        => PrefetchAsync(root, CloudVariants.SourceFor(client), db, problems);

    // The same with the service handed in.
    public static async Task<int> PrefetchAsync(DataTreeDictionary root, ICloudVariantSource source, LocalDB db, List<string>? problems = null)
    {
        var manifest = ReadManifest(root);
        var extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest)
        {
            extensions[entry.Hash] = entry.Extension;
            if (entry.Type == "texture")
                textures.Add(entry.Hash);
        }

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
            if (textures.Contains(hash) && await CloudVariants.PrefetchAsync(db, hash, source).ConfigureAwait(false))
            {
                fetched++;
                continue;
            }
            var (data, error) = await source.FetchAsync(hash).ConfigureAwait(false);
            if (data == null)
            {
                problems?.Add($"could not fetch {hash}: {error ?? "no answer"}");
                continue;
            }
            var ext = extensions.TryGetValue(hash, out var known) && !string.IsNullOrEmpty(known) ? known : ".bin";
            await db.SaveAssetAsync(data, ext, received: true).ConfigureAwait(false);
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
        // An extension after the hash names the same blob.
        int dot = trimmed.IndexOf('.');
        if (dot >= 0)
            trimmed = trimmed.Substring(0, dot);
        return trimmed.Length == 0 ? null : trimmed;
    }

    // A record key that is the sha256 of its bytes, the way the asset database files them.
    private static bool IsContentHash(string key)
    {
        if (key.Length != 64)
            return false;
        foreach (var c in key)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }

    public static string TypeOf(string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".png": case ".jpg": case ".jpeg": case ".webp": case ".tga": case ".bmp": case ".ktx": case ".ktx2": case ".dds": case ".exr": case ".hdr":
            case ".tif": case ".tiff":
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

    // Every local file a tree names stays on this machine from now on, whatever brought it here: a world
    // saved here must still open when nobody who sent its files is around.
    public static void KeepLocalFiles(DataTreeNode root, LocalDB? db)
    {
        if (db == null)
            return;
        var locals = new List<string>();
        CollectUrls(root, "local", locals);
        foreach (var uri in locals)
            db.Keep(uri);
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
    // Every URL value naming one of the given cleared, the way an empty reference is saved.
    private static void Clear(DataTreeNode node, HashSet<string> gone)
    {
        switch (node)
        {
            case DataTreeDictionary d:
                foreach (var key in new List<string>(d.Children.Keys))
                {
                    if (d.Children[key] is DataTreeValue { IsUrl: true } v && gone.Contains(((string)v.Value!).Substring(1)))
                        d.Children[key] = new DataTreeValue((Uri?)null);
                    else
                        Clear(d.Children[key], gone);
                }
                break;
            case DataTreeList l:
                for (int i = 0; i < l.Children.Count; i++)
                {
                    if (l.Children[i] is DataTreeValue { IsUrl: true } v && gone.Contains(((string)v.Value!).Substring(1)))
                        l.Children[i] = new DataTreeValue((Uri?)null);
                    else
                        Clear(l.Children[i], gone);
                }
                break;
        }
    }

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

    private static readonly string NodeTypeName = typeof(Components.Scripting.ScriptGraphNode).FullName!;

    // The constants of every open script graph node in the tree, with the name of the node's slot.
    private static void CollectNodeValues(DataTreeNode node, string? slot, List<(DataTreeDictionary Entry, string? Node)> into)
    {
        switch (node)
        {
            case DataTreeDictionary d:
                if (d.ContainsKey("Components") && d.TryGetNode("Name") is DataTreeValue { IsUrl: false, Value: string } name)
                    slot = name.Extract<string>();
                if (d.TryGetNode("Type") is DataTreeValue { Value: string type } && type == NodeTypeName
                    && d.TryGetDictionary("Data")?.TryGetList("Values") is { } values)
                {
                    foreach (var value in values.Children)
                    {
                        if (value is DataTreeDictionary entry)
                            into.Add((entry, slot));
                    }
                    break;
                }
                foreach (var child in d.Children.Values)
                    CollectNodeValues(child, slot, into);
                break;
            case DataTreeList l:
                foreach (var child in l.Children)
                    CollectNodeValues(child, slot, into);
                break;
        }
    }

    // Where each URL in the tree is used, as a person would name the place: the slot and the component.
    private static void DescribeUses(DataTreeNode node, string? slot, string? component, Dictionary<string, string> uses)
    {
        switch (node)
        {
            case DataTreeDictionary d:
                if (d.ContainsKey("Components") && d.TryGetNode("Name") is DataTreeValue { IsUrl: false, Value: string } name)
                    slot = name.Extract<string>();
                if (d.ContainsKey("Data") && d.TryGetNode("Type") is DataTreeValue { Value: string type })
                    component = ShortTypeName(type);
                foreach (var child in d.Children.Values)
                    DescribeUses(child, slot, component, uses);
                break;
            case DataTreeList l:
                foreach (var child in l.Children)
                    DescribeUses(child, slot, component, uses);
                break;
            case DataTreeValue v when v.IsUrl:
                uses.TryAdd(((string)v.Value!).Substring(1), slot != null ? $"\"{slot}\" ({component})" : $"its {component ?? "asset"}");
                break;
        }
    }

    private static string ShortTypeName(string type)
    {
        int generic = type.IndexOfAny(new[] { '`', '[' });
        if (generic > 0)
            type = type.Substring(0, generic);
        return type.Substring(type.LastIndexOf('.') + 1);
    }

    // A whole value that is a local asset address and nothing else, in the form the asset store hands out:
    // local://<machine>/<record>. Text that only mentions one is not a URL value and stays as it is.
    private static bool IsLocalAddress(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith("local://", StringComparison.Ordinal))
            return false;
        var rest = text.AsSpan(8);
        int slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1 || rest.Slice(slash + 1).IndexOf('/') >= 0)
            return false;
        foreach (char c in rest)
        {
            if (c != '/' && c != '-' && c != '_' && c != '.' && c != '~' && !char.IsAsciiLetterOrDigit(c))
                return false;
        }
        return true;
    }

    // One save's files: every local file it names, found once and uploaded once, by the hash it went up
    // under.
    private sealed class Packing
    {
        // Stores one file: the local address its bytes are read from and its path on disk, to the hash it
        // went up under, the size that went up, or no hash and why not.
        private readonly Func<string, string, Task<(string? Hash, long Size, string? Error)>> _store;
        private readonly LocalDB? _db;
        private readonly Dictionary<string, string?> _files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptGraph> _graphs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _namedIn = new(StringComparer.Ordinal);

        public readonly Dictionary<string, string?> Hashes = new(StringComparer.Ordinal);
        public readonly List<CloudAssetEntry> Manifest = new();
        public readonly List<(string Uri, string Why)> Problems = new();

        // Files this machine no longer has, which the save goes up without, and the ones it has.
        public readonly List<string> Missing = new();
        public readonly List<string> Found = new();

        public Packing(Func<string, string, Task<(string? Hash, long Size, string? Error)>> store, LocalDB? db)
        {
            _store = store;
            _db = db;
        }

        public void NameIn(string uri, string where) => _namedIn.TryAdd(uri, where);

        // What the files this machine has weigh on disk, each file once.
        public long FoundBytes()
        {
            long total = 0;
            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _files.Values)
            {
                if (path == null || !counted.Add(path))
                    continue;
                try
                {
                    total += new FileInfo(path).Length;
                }
                catch (IOException)
                {
                }
            }
            return total;
        }

        // The file behind a URL, and behind every URL constant of a stored script graph it names.
        public async Task FindAsync(string uri)
        {
            if (_files.ContainsKey(uri))
                return;
            var path = _db?.GetFilePath(uri);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _files[uri] = null;
                Missing.Add(uri);
                return;
            }
            _files[uri] = path;
            Found.Add(uri);
            // A save names it, so it stays on this machine whatever brought it here.
            _db?.Keep(uri);
            if (!string.Equals(Path.GetExtension(path), ScriptGraphAsset.Extension, StringComparison.OrdinalIgnoreCase))
                return;

            var bytes = await _db!.ReadAssetBytesAsync(uri).ConfigureAwait(false);
            if (bytes == null)
                return;
            ScriptGraph graph;
            try
            {
                graph = ScriptGraph.Parse(bytes);
            }
            catch (GraphFormatException)
            {
                return;
            }
            var named = new List<string>();
            foreach (var node in graph.Nodes)
            {
                foreach (var value in node.Values.Values)
                {
                    if (value.Kind == GraphValueKind.String && IsLocalAddress(value.String))
                        named.Add(value.String!);
                }
            }
            if (named.Count == 0)
                return;
            _graphs[uri] = graph;
            foreach (var inner in named)
            {
                NameIn(inner, uri);
                await FindAsync(inner).ConfigureAwait(false);
            }
        }

        // Uploads what a URL names and answers its hash, or null with the problem noted. A stored script graph
        // that names files of its own, a texture typed into a node, has those go up first and is stored again
        // naming them by cloud address, so the copy that goes up names nothing left behind here.
        public async Task<string?> UploadAsync(string uri)
        {
            if (Hashes.TryGetValue(uri, out var known))
                return known;
            Hashes[uri] = null;
            if (!_files.TryGetValue(uri, out var path) || path == null)
                return null;

            var source = uri;
            if (_graphs.TryGetValue(uri, out var graph))
            {
                foreach (var node in graph.Nodes)
                {
                    foreach (var port in new List<string>(node.Values.Keys))
                    {
                        var value = node.Values[port];
                        if (value.Kind != GraphValueKind.String || !IsLocalAddress(value.String))
                            continue;
                        // A file the script names that this machine no longer has goes out of it, as from the tree.
                        if (Missing.Contains(value.String!))
                        {
                            node.Values.Remove(port);
                            continue;
                        }
                        if (value.String == uri || await UploadAsync(value.String!).ConfigureAwait(false) is not { } inner)
                        {
                            Problems.Add((uri, "names a file that did not go up"));
                            return null;
                        }
                        node.Values[port] = GraphValue.Of($"{CloudScheme}:///{inner}");
                    }
                }
                var restored = await _db!.SaveAssetAsync(graph.ToUtf8Json(), ScriptGraphAsset.Extension).ConfigureAwait(false);
                path = restored == null ? null : _db.GetFilePath(restored);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Problems.Add((uri, "could not be stored again"));
                    return null;
                }
                source = restored!;
            }

            var (stored, size, error) = await _store(source, path).ConfigureAwait(false);
            if (stored == null)
            {
                Problems.Add((uri, $"did not upload: {error ?? "no answer"}"));
                return null;
            }
            Hashes[uri] = stored;
            var extension = Path.GetExtension(path) ?? string.Empty;
            Manifest.Add(new CloudAssetEntry
            {
                Hash = stored,
                Name = Path.GetFileNameWithoutExtension(path) ?? stored,
                Extension = extension,
                Type = TypeOf(extension),
                SizeBytes = size,
                LocalUri = source,
            });
            return stored;
        }

        // How the person saving would know a file: the name it was imported under, where the save uses it,
        // or the node or stored script that names it.
        public string NameOf(string uri, Dictionary<string, string> uses, int depth = 0)
        {
            if (FileName(uri) is { } file)
                return file;
            if (uses.TryGetValue(uri, out var place))
                return $"the file on {place}";
            if (_namedIn.TryGetValue(uri, out var namer) && depth < 8)
            {
                if (!IsLocalAddress(namer))
                    return namer;
                return $"a file named in the script on {(uses.TryGetValue(namer, out var script) ? script : NameOf(namer, uses, depth + 1))}";
            }
            return uri;
        }

        private string? FileName(string uri)
        {
            if (_db == null || !uri.StartsWith("local://", StringComparison.Ordinal))
                return null;
            var key = uri.Substring(uri.IndexOf('/', 8) + 1);
            foreach (var record in _db.GetAllRecords())
            {
                if (record.Hash == key && !string.IsNullOrEmpty(record.OriginalFileName)
                    && !record.OriginalFileName.StartsWith(record.Hash, StringComparison.OrdinalIgnoreCase))
                    return record.OriginalFileName;
            }
            return null;
        }
    }
}
