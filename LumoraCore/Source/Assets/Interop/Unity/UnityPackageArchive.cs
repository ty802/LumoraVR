// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets.Interop.Unity;

public sealed class UnityPackageAsset
{
    public string Guid = "";
    // As the package states it, "Assets/Folder/File.ext", forward slashes.
    public string Path = "";
    // Where the bytes were written, null for a folder entry.
    public string? File;
    public long Size;
    public string? Meta;

    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

// A .unitypackage is a gzipped tar of one folder per asset, named by the asset's GUID, holding
// "asset" (the bytes), "asset.meta" (import settings, YAML) and "pathname" (where it lived in the
// project). The bytes are written out under the package's own folder layout, so a model finds the
// textures beside it the way it did in the project, and the archive is deleted when disposed.
//
// The package is a file somebody handed over. Every path is rebuilt from cleaned segments and checked
// to land inside the extraction folder, and the total size and entry count are capped before a single
// byte is trusted. -xlinka
public sealed class UnityPackageArchive : IDisposable
{
    public const long MaxTotalBytes = 6L * 1024 * 1024 * 1024;
    public const int MaxEntries = 200_000;

    public readonly string SourcePath;
    public readonly string Root;
    public readonly Dictionary<string, UnityPackageAsset> ByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, UnityPackageAsset> ByPath = new(StringComparer.OrdinalIgnoreCase);
    public long TotalBytes { get; private set; }

    private bool _disposed;

    private UnityPackageArchive(string sourcePath, string root)
    {
        SourcePath = sourcePath;
        Root = root;
    }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(SourcePath);

    public static UnityPackageArchive Extract(string packagePath, string tempRoot, CancellationToken cancel = default)
    {
        string root = System.IO.Path.Combine(tempRoot, "unitypackage-" + System.Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        var archive = new UnityPackageArchive(packagePath, System.IO.Path.GetFullPath(root));
        try
        {
            archive.ReadAll(cancel);
            return archive;
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private void ReadAll(CancellationToken cancel)
    {
        string staging = System.IO.Path.Combine(Root, ".staging");
        Directory.CreateDirectory(staging);
        var pathnames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var metas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var staged = new Dictionary<string, (string File, long Size)>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;

        using (var file = System.IO.File.OpenRead(SourcePath))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var tar = new TarReader(gzip))
        {
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) != null)
            {
                cancel.ThrowIfCancellationRequested();
                if (++entries > MaxEntries)
                    throw new InvalidDataException($"The package has more than {MaxEntries} entries.");
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                    continue;
                var parts = entry.Name.Replace('\\', '/').TrimStart('.', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !IsGuid(parts[0]))
                    continue;
                string guid = parts[0].ToLowerInvariant();
                switch (parts[1])
                {
                    case "pathname":
                        pathnames[guid] = ReadSmallText(entry.DataStream, 4096);
                        break;
                    case "asset.meta":
                        metas[guid] = ReadSmallText(entry.DataStream, 4 * 1024 * 1024);
                        break;
                    case "asset":
                    {
                        long size = entry.Length;
                        if (TotalBytes + size > MaxTotalBytes)
                            throw new InvalidDataException("The package unpacks to more than the import limit.");
                        string target = System.IO.Path.Combine(staging, guid);
                        using (var output = System.IO.File.Create(target))
                            entry.DataStream.CopyTo(output);
                        TotalBytes += size;
                        staged[guid] = (target, size);
                        break;
                    }
                }
            }
        }

        foreach (var (guid, raw) in pathnames)
        {
            var path = CleanPath(raw);
            if (path == null)
            {
                Logger.Warn($"UnityPackage: skipped an entry with an unusable path '{raw}'.");
                continue;
            }
            var asset = new UnityPackageAsset { Guid = guid, Path = path };
            metas.TryGetValue(guid, out asset.Meta);
            if (staged.TryGetValue(guid, out var data))
            {
                string destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn($"UnityPackage: skipped '{path}', it resolves outside the package.");
                    continue;
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                if (System.IO.File.Exists(destination))
                    destination = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destination)!,
                        System.IO.Path.GetFileNameWithoutExtension(destination) + "_" + guid[..6] + System.IO.Path.GetExtension(destination));
                System.IO.File.Move(data.File, destination);
                asset.File = destination;
                asset.Size = data.Size;
            }
            ByGuid[guid] = asset;
            ByPath.TryAdd(path, asset);
        }
        try { Directory.Delete(staging, true); } catch { }
    }

    public UnityPackageAsset? Find(string? guid)
        => guid != null && ByGuid.TryGetValue(guid, out var asset) ? asset : null;

    public string? ReadText(UnityPackageAsset? asset)
    {
        if (asset?.File == null || !System.IO.File.Exists(asset.File))
            return null;
        return System.IO.File.ReadAllText(asset.File, Encoding.UTF8);
    }

    public IEnumerable<UnityPackageAsset> OfExtension(string extension)
    {
        foreach (var asset in ByGuid.Values)
            if (asset.File != null && asset.Extension == extension)
                yield return asset;
    }

    private static bool IsGuid(string s)
    {
        if (s.Length != 32)
            return false;
        foreach (char c in s)
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
    }

    private static string ReadSmallText(Stream stream, int limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit)
                throw new InvalidDataException("A package text entry is larger than any real one.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // First line only (some writers append a second), forward slashes, no empty, "." or ".." segment,
    // no character a file name cannot hold, no drive or root.
    private static string? CleanPath(string raw)
    {
        int newline = raw.IndexOfAny(new[] { '\n', '\r' });
        var line = (newline >= 0 ? raw[..newline] : raw).Trim().Replace('\\', '/');
        if (line.Length == 0 || line.Contains(':'))
            return null;
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var segments = line.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleaned = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            var s = segment.Trim();
            if (s.Length == 0 || s == "." || s == "..")
                return null;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            var part = sb.ToString().TrimEnd('.', ' ');
            if (part.Length == 0)
                return null;
            cleaned.Add(part.Length > 120 ? part[..120] : part);
        }
        return cleaned.Count == 0 ? null : string.Join('/', cleaned);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch (Exception ex)
        {
            Logger.Warn($"UnityPackage: could not remove the extraction folder {Root}: {ex.Message}");
        }
    }
}
