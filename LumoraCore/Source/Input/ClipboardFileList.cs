// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;

namespace Lumora.Core.Input;

// File managers can expose file:/// URI lists or plain paths as clipboard text. Parse the entire
// payload before treating it as files, so ordinary multiline text is never partially imported.
public static class ClipboardFileList
{
    public static List<string> Parse(string? text, bool windows, Func<string, bool>? exists = null)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 * 1024) return paths;
        exists ??= static path => File.Exists(path) || Directory.Exists(path);
        var seen = new HashSet<string>(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string entry = line.Trim();
            if (entry.Length == 0 || entry.StartsWith("#", StringComparison.Ordinal)) continue;
            if (paths.Count == 0 && (entry is "copy" or "cut" or "x-special/gnome-copied-files")) continue;
            if (!TryPath(entry, windows, out var path) || !exists(path)) return new List<string>();
            if (seen.Add(path)) paths.Add(path);
            if (paths.Count > 1024) return new List<string>();
        }
        return paths;
    }

    private static bool TryPath(string text, bool windows, out string path)
    {
        path = text;
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsFile
                || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
            bool remote = uri.Host.Length > 0 && !uri.IsLoopback;
            if (!windows && remote) return false;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (windows)
            {
                if (remote) path = "//" + uri.Host + path;
                else if (path.Length >= 3 && path[0] == '/' && path[2] == ':') path = path[1..];
                path = path.Replace('/', '\\');
            }
        }
        if (path.IndexOf('\0') >= 0 || path.IndexOf('\n') >= 0 || path.IndexOf('\r') >= 0) return false;
        if (!windows && path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return windows
            ? path.StartsWith("\\\\", StringComparison.Ordinal) || path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] is '/' or '\\')
            : path.StartsWith("/", StringComparison.Ordinal);
    }
}
