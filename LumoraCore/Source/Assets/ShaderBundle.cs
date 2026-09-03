// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumora.Core.Assets;

// What a shader bundle says about itself, read from its manifest.
public sealed class ShaderBundleInfo
{
    public string Name = "";
    public int FormatVersion;
    public bool Valid;
    public List<string> Errors = new();
    public List<(string name, string type)> Uniforms = new();
    public List<string> Includes = new();
    public DateTime CreatedUtc;
}

// A custom shader as one record: a .lumshader is a zip holding the shader text, every include it
// pulls in, and a manifest with the sandbox verdict and the uniforms it exposes. One file, one hash,
// so a world or object that uses the shader carries it to the cloud like a texture and any machine
// that fetches it has everything the shader needs. The text inside is compiled on the device, the
// way this engine compiles every shader, so no per-platform builds live in here. -xlinka
public static class ShaderBundle
{
    public const string Extension = ".lumshader";
    public const string EntryName = "shader.gdshader";
    public const string ManifestName = "manifest.json";
    public const string IncludeFolder = "include/";
    public const int FormatVersion = 1;
    private const int MaxIncludeDepth = 4;

    private static readonly Regex IncludeLine = new("^\\s*#include\\s+\"([^\"]+)\"\\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static bool IsBundle(byte[]? bytes)
        => bytes != null && bytes.Length > 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K' && bytes[2] == 3 && bytes[3] == 4;

    // Reads the includes a source pulls in, relative to the file it came from, and the ones those
    // pull in, a few levels deep. A missing include stays a missing include; the sandbox reports it.
    public static Dictionary<string, string> CollectIncludes(string source, string? baseDirectory)
    {
        var includes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Collect(source, baseDirectory, includes, 0);
        return includes;
    }

    private static void Collect(string source, string? baseDirectory, Dictionary<string, string> into, int depth)
    {
        if (depth > MaxIncludeDepth || string.IsNullOrEmpty(baseDirectory))
            return;
        foreach (Match match in IncludeLine.Matches(source))
        {
            var name = match.Groups[1].Value.Replace('\\', '/');
            if (into.ContainsKey(name) || name.Contains(".."))
                continue;
            var path = Path.Combine(baseDirectory, name);
            if (!File.Exists(path))
                continue;
            string text;
            try { text = File.ReadAllText(path); }
            catch { continue; }
            into[name] = text;
            Collect(text, Path.GetDirectoryName(path), into, depth + 1);
        }
    }

    // The shader with its includes pasted in place, which is what the sandbox reads and what the
    // device compiles; an include that is not in the bundle is left as the directive so the
    // sandbox can say so.
    public static string Inline(string source, IReadOnlyDictionary<string, string> includes)
    {
        string current = source;
        for (int depth = 0; depth < MaxIncludeDepth; depth++)
        {
            bool replaced = false;
            current = IncludeLine.Replace(current, match =>
            {
                var name = match.Groups[1].Value.Replace('\\', '/');
                if (!includes.TryGetValue(name, out var text))
                    return match.Value;
                replaced = true;
                return "// begin include " + name + "\n" + text + "\n// end include " + name;
            });
            if (!replaced)
                break;
        }
        return current;
    }

    public static byte[] Build(string name, string source, IReadOnlyDictionary<string, string> includes)
    {
        var inlined = Inline(source, includes);
        var verdict = ShaderSourceValidator.Validate(inlined);
        var uniforms = new List<object>();
        try
        {
            foreach (var definition in ShaderUniformParser.Parse(inlined))
                uniforms.Add(new { name = definition.Name, type = definition.Type.ToString() });
        }
        catch { }

        var manifest = new
        {
            format = "lumshader",
            version = FormatVersion,
            name,
            entry = EntryName,
            includes = new List<string>(includes.Keys),
            valid = verdict.IsValid,
            errors = verdict.Errors,
            uniforms,
            created = DateTime.UtcNow,
        };

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, ManifestName, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            Write(zip, EntryName, source);
            foreach (var (includeName, text) in includes)
                Write(zip, IncludeFolder + includeName, text);
        }
        return stream.ToArray();
    }

    private static void Write(ZipArchive zip, string entryName, string text)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    // Opens a bundle: the manifest, and the shader text with its includes pasted in. False for
    // anything that is not a bundle or has no shader entry.
    public static bool TryOpen(byte[] bytes, out ShaderBundleInfo info, out string source)
    {
        info = new ShaderBundleInfo();
        source = string.Empty;
        if (!IsBundle(bytes))
            return false;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry(EntryName);
            if (entry == null)
                return false;
            string raw = Read(entry);
            var includes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in zip.Entries)
            {
                if (item.FullName.StartsWith(IncludeFolder, StringComparison.Ordinal) && item.FullName.Length > IncludeFolder.Length)
                    includes[item.FullName.Substring(IncludeFolder.Length)] = Read(item);
            }
            source = Inline(raw, includes);
            info.Includes.AddRange(includes.Keys);

            var manifestEntry = zip.GetEntry(ManifestName);
            if (manifestEntry != null)
            {
                using var doc = JsonDocument.Parse(Read(manifestEntry));
                var root = doc.RootElement;
                info.Name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                info.FormatVersion = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                info.Valid = root.TryGetProperty("valid", out var ok) && ok.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                    foreach (var e in errors.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) info.Errors.Add(e.GetString() ?? "");
                if (root.TryGetProperty("uniforms", out var uniforms) && uniforms.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in uniforms.EnumerateArray())
                    {
                        string uname = u.TryGetProperty("name", out var un) ? un.GetString() ?? "" : "";
                        string utype = u.TryGetProperty("type", out var ut) ? ut.GetString() ?? "" : "";
                        if (uname.Length > 0)
                            info.Uniforms.Add((uname, utype));
                    }
                }
                if (root.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(created.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var when))
                    info.CreatedUtc = when;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"ShaderBundle: could not open a bundle: {ex.Message}");
            return false;
        }
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
