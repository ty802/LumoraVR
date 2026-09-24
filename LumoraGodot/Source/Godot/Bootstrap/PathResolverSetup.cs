using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;

// First autoload in the tree: pins the one data folder before anything can write to disk, then folds
// the folders older builds scattered across the profile into it. Runs before the engine exists, so no
// file is open yet and a folder move is a rename on the same volume. -xlinka
public partial class PathResolverSetup : Node
{
    public override void _EnterTree()
    {
        base._EnterTree();
        string root = OS.GetUserDataDir();
        try { Directory.CreateDirectory(root); } catch { }
        PathResolver.Initialize(root);

        var notes = new List<string>();
        foreach (var legacy in LegacyRoots(PathResolver.Root))
        {
            try { FoldInto(legacy, PathResolver.Root, notes); }
            catch (Exception ex) { notes.Add($"Data folder: could not fold '{legacy}' in: {ex.Message}"); }
        }
        LumoraLogger.Log($"Data folder: {PathResolver.Root}");
        foreach (var note in notes)
            LumoraLogger.Log(note);
    }

    // Where earlier builds put things, per platform, in the order they must be folded: a folder nested
    // inside another candidate goes first so its parent's pass does not carry it across as one entry.
    private static IEnumerable<string> LegacyRoots(string root)
    {
        string godotDir = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "godot" : "Godot";
        string oldGodotUserDir = Path.Combine(OS.GetDataDir(), godotDir, "app_userdata", PathResolver.FolderName);
        var candidates = new[]
        {
            Path.Combine(oldGodotUserDir, PathResolver.FolderName),
            Path.Combine(OS.GetCacheDir(), PathResolver.FolderName),
            Path.Combine(OS.GetConfigDir(), PathResolver.FolderName),
            Path.Combine(OS.GetDataDir(), PathResolver.FolderName),
            oldGodotUserDir,
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            string full;
            try { full = Path.GetFullPath(candidate); } catch { continue; }
            if (!seen.Add(full) || !Directory.Exists(full))
                continue;
            if (SamePath(full, root) || IsUnder(root, full) || IsUnder(full, root))
                continue;
            yield return full;
        }
    }

    private static void FoldInto(string source, string destination, List<string> notes)
    {
        int moved = 0, kept = 0;
        MergeDirectory(source, destination, ref moved, ref kept);
        TryDeleteEmpty(source);
        if (moved > 0 || kept > 0)
            notes.Add($"Data folder: folded '{source}' in ({moved} moved, {kept} left behind because the folder already had them)");
    }

    // Move what the destination does not have; recurse into what it does; never overwrite. A file the
    // destination already has stays where it was, so nothing this session opened gets pulled out from
    // under it and nothing newer is replaced by something older.
    private static void MergeDirectory(string source, string destination, ref int moved, ref int kept)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(dir));
            if (!Directory.Exists(target) && !File.Exists(target))
            {
                try { Directory.Move(dir, target); moved++; continue; }
                catch { }
            }
            if (Directory.Exists(target))
            {
                MergeDirectory(dir, target, ref moved, ref kept);
                TryDeleteEmpty(dir);
            }
            else
            {
                kept++;
            }
        }
        foreach (var file in Directory.GetFiles(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(target) || Directory.Exists(target))
            {
                kept++;
                continue;
            }
            try { File.Move(file, target); moved++; }
            catch { kept++; }
        }
    }

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory)
                && Directory.GetFiles(directory).Length == 0
                && Directory.GetDirectories(directory).Length == 0)
                Directory.Delete(directory);
        }
        catch { }
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string ancestor)
        => Normalize(path).StartsWith(Normalize(ancestor) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
