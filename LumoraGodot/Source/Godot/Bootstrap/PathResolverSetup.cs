using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
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
        // LUMORA_DATA_DIR points a run at a folder of its own: a second copy of the game on the same machine
        // then has its own store, settings, sign-in and machine id, so it behaves like another computer.
        var custom = System.Environment.GetEnvironmentVariable("LUMORA_DATA_DIR");
        bool own = !string.IsNullOrWhiteSpace(custom);
        string root = own ? custom!.Trim() : OS.GetUserDataDir();
        if (!Lumora.Source.Godot.Bootstrap.DiscordManager.PrepareLaunch(root))
        {
            GetTree().Quit();
            return;
        }
        try { Directory.CreateDirectory(root); } catch { }
        PathResolver.Initialize(root);
        StartJitProfile();

        var notes = new List<string>();
        foreach (var legacy in own ? (IEnumerable<string>)Array.Empty<string>() : LegacyRoots(PathResolver.Root))
        {
            try { FoldInto(legacy, PathResolver.Root, notes); }
            catch (Exception ex) { notes.Add($"Data folder: could not fold '{legacy}' in: {ex.Message}"); }
        }
        LumoraLogger.Log($"Data folder: {PathResolver.Root}");
        foreach (var note in notes)
            LumoraLogger.Log(note);
    }

    public override void _ExitTree()
    {
        Lumora.Source.Godot.Bootstrap.DiscordManager.StopLaunchRelay();
        // Stopping writes the profile; the runtime does not reliably do it itself when the engine exits.
        try { ProfileContext.StartProfileOptimization(null); } catch { }
        base._ExitTree();
    }

    private static AssemblyLoadContext ProfileContext
        => AssemblyLoadContext.GetLoadContext(typeof(PathResolverSetup).Assembly) ?? AssemblyLoadContext.Default;

    // Every method a run compiles goes in a profile, and the next run compiles them on spare cores from here
    // on, ahead of the code that needs them, so the first world opened stops paying for the JIT. The runtime
    // skips the entries of an assembly that changed since. Our assemblies are loaded here as well: the
    // background compiler gives up at the first one not loaded yet, which this early is most of them, and a
    // world opened a few seconds in got nothing. Measured on the shader gallery opened 5 s after start, with
    // the engine's warm-up: 692 ms to 326 ms, worst first frame 155 to 57; warm is 231. -xlinka
    private static void StartJitProfile()
    {
        try
        {
            var context = ProfileContext;
            string folder = PathResolver.Resolve("jit");
            Directory.CreateDirectory(folder);
            context.SetProfileOptimizationRoot(folder);
            context.StartProfileOptimization(OS.HasFeature("template") ? "game.jitprofile" : "editor.jitprofile");
            LoadReferences(context, typeof(PathResolverSetup).Assembly, new HashSet<string>(StringComparer.Ordinal));
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"JIT profile not started: {ex.Message}");
        }
    }

    private static void LoadReferences(AssemblyLoadContext context, Assembly from, HashSet<string> seen)
    {
        foreach (var reference in from.GetReferencedAssemblies())
        {
            string? name = reference.Name;
            if (name == null || IsPlatformAssembly(name) || !seen.Add(name))
                continue;
            try
            {
                LoadReferences(context, context.LoadFromAssemblyName(reference), seen);
            }
            catch (Exception)
            {
                // Optional at runtime; it loads, or does not, when something first needs it.
            }
        }
    }

    private static bool IsPlatformAssembly(string name)
        => name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name.StartsWith("Godot", StringComparison.Ordinal) || name is "netstandard" or "mscorlib";

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
