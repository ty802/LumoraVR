// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Persistence;

// One registered answer to "this saved type, written at this version, is now that type - and its member
// data needs this much rewriting to fit".
public sealed class TypeMigration
{
    // The name as it appears in the file. Matched verbatim, before any rename alias, so a type can be
    // replaced and renamed independently without the two rules fighting.
    public string SavedTypeName { get; }

    // Inclusive. A save that stamped no version for the type reads as 0, which is what every file
    // written before the type declared a [SaveTypeVersion] carries.
    public int MinVersion { get; }
    public int MaxVersion { get; }

    // Null keeps whatever SavedTypeName resolves to and applies only the data transform.
    public string? ReplacementTypeName { get; }

    // Runs on the worker's member dictionary before it reaches Load. Must not mutate the node it is
    // given: a cached graph can be loaded many times (inventory spawns), and a rewritten shared tree
    // would hand the second load something the first one already chewed. Return the input unchanged
    // when there is nothing to do. -xlinka
    public Func<DataTreeNode, DataTreeNode>? TransformData { get; }

    public TypeMigration(string savedTypeName, string? replacementTypeName, int minVersion, int maxVersion,
                         Func<DataTreeNode, DataTreeNode>? transformData)
    {
        SavedTypeName = savedTypeName;
        ReplacementTypeName = replacementTypeName;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
        TransformData = transformData;
    }

    public bool Covers(int savedVersion) => savedVersion >= MinVersion && savedVersion <= MaxVersion;

    public override string ToString()
        => $"{SavedTypeName} [{MinVersion}..{MaxVersion}] -> {ReplacementTypeName ?? "(same type)"}"
         + (TransformData != null ? " + transform" : "");
}

// THE TYPE REPLACEMENT LEVER
//
// The rename alias map answers "this class has a new name". It cannot answer "this class was folded
// into another one and its old member data has to be reshaped on the way in", which is the case that
// actually eats content: the type resolves to something whose members no longer line up, Worker.Load
// walks members that EXIST, and the rest goes on the floor quietly.
//
// So this table is consulted FIRST, keyed on the name exactly as written in the file and filtered by
// the version that file stamped for that type. An entry can point at a different type, rewrite the
// member data, or both.
//
// COST: one dictionary probe per saved worker, skipped entirely when nothing is registered. Transforms
// only allocate for the saves that actually match. -xlinka
public static class TypeMigrations
{
    private static readonly object _lock = new();
    private static Dictionary<string, TypeMigration[]> _entries = new(StringComparer.Ordinal);
    private static volatile bool _hasAny;

    public static bool HasAny => _hasAny;

    static TypeMigrations()
    {
        RegisterBuiltIns();
    }

    public static void Register(string savedTypeName, Type? replacement = null, int minVersion = 0,
                                int maxVersion = int.MaxValue, Func<DataTreeNode, DataTreeNode>? transformData = null)
        => Register(savedTypeName, replacement?.FullName, minVersion, maxVersion, transformData);

    // The string overload is the one built-ins use: a migration must survive its own replacement type
    // being renamed later, and a hard typeof() here would quietly bind to the new name and stop
    // matching the file.
    public static void Register(string savedTypeName, string? replacementTypeName, int minVersion = 0,
                                int maxVersion = int.MaxValue, Func<DataTreeNode, DataTreeNode>? transformData = null)
    {
        if (string.IsNullOrEmpty(savedTypeName))
            throw new ArgumentException("A migration needs the type name as it appears in the file.", nameof(savedTypeName));
        if (maxVersion < minVersion)
            throw new ArgumentOutOfRangeException(nameof(maxVersion), "Version range runs low to high.");
        if (replacementTypeName == null && transformData == null)
            throw new ArgumentException("A migration that neither replaces the type nor transforms its data does nothing.", nameof(replacementTypeName));

        var entry = new TypeMigration(savedTypeName, replacementTypeName, minVersion, maxVersion, transformData);

        lock (_lock)
        {
            // Copy on write: loads read the table without a lock, and registration happens a handful of
            // times at startup.
            var next = new Dictionary<string, TypeMigration[]>(_entries, StringComparer.Ordinal);
            if (next.TryGetValue(savedTypeName, out var existing))
            {
                var grown = new TypeMigration[existing.Length + 1];
                Array.Copy(existing, grown, existing.Length);
                grown[existing.Length] = entry;
                next[savedTypeName] = grown;
            }
            else
            {
                next[savedTypeName] = new[] { entry };
            }
            _entries = next;
            _hasAny = true;
        }
    }

    // Split from the version filter on purpose: this is the probe every loaded worker pays for, and
    // almost all of them miss. Reading the version the file stamped costs another lookup, so it only
    // happens once a name is known to have entries. -xlinka
    public static bool TryGetEntries(string savedTypeName, out TypeMigration[] entries)
    {
        entries = null!;
        if (!_hasAny || string.IsNullOrEmpty(savedTypeName))
            return false;
        return _entries.TryGetValue(savedTypeName, out entries!);
    }

    public static bool TryResolve(string savedTypeName, int savedVersion, out TypeMigration migration)
    {
        migration = null!;
        if (!TryGetEntries(savedTypeName, out var candidates))
            return false;
        return TryPick(candidates, savedVersion, out migration);
    }

    public static bool TryPick(TypeMigration[] candidates, int savedVersion, out TypeMigration migration)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Covers(savedVersion))
            {
                migration = candidate;
                return true;
            }
        }
        migration = null!;
        return false;
    }

    public static IEnumerable<TypeMigration> Registered
    {
        get
        {
            foreach (var entries in _entries.Values)
                foreach (var entry in entries)
                    yield return entry;
        }
    }

    // BUILT-IN MIGRATIONS

    private static void RegisterBuiltIns()
    {
        // PLAYBACK CONSOLIDATION. An animator used to carry playback as five loose members (Playing,
        // Speed, WrapMode, AnchorPosition, AnchorTicks) and now carries one SyncPlayback that owns all
        // of it, with the wrap enum folded into the shared PlaybackLoopMode. Old member data reaches a
        // member list that no longer mentions any of those names, so without this the clip loads
        // stopped at zero.
        //
        // The anchor is deliberately dropped rather than carried: it names an instant on a clock that
        // moved on while the file sat on disk, so honouring it fast-forwards the clip by however long
        // that was. The saved position is what the file meant.
        Register(
            "Lumora.Core.Components.Animator",
            replacementTypeName: null,
            minVersion: 0,
            maxVersion: 0,
            transformData: MigrateLoosePlayback);
    }

    private const string PlaybackMember = "Playback";

    private static DataTreeNode MigrateLoosePlayback(DataTreeNode node)
    {
        if (node is not DataTreeDictionary dictionary)
            return node;

        // Already the current shape, or nothing of the old one to carry.
        if (dictionary.ContainsKey(PlaybackMember))
            return node;
        if (!dictionary.ContainsKey("AnchorPosition") && !dictionary.ContainsKey("Playing"))
            return node;

        var playback = new DataTreeDictionary();
        playback.Add("Playing", dictionary.ExtractOrDefault("Playing", false));
        // The loose form defaulted to looping; the consolidated member defaults to a single play, so
        // the default has to be carried across explicitly or every old clip comes back one-shot.
        playback.Add("Loop", DataTreeValue.RawString(LoopModeName(dictionary)));
        playback.Add("Position", dictionary.ExtractOrDefault("AnchorPosition", 0f));
        playback.Add("Speed", dictionary.ExtractOrDefault("Speed", 1f));

        var migrated = new DataTreeDictionary();
        foreach (var (key, child) in dictionary.Children)
        {
            if (key is "Playing" or "Speed" or "WrapMode" or "AnchorPosition" or "AnchorTicks")
                continue;
            migrated.Add(key, child);
        }
        migrated.Add(PlaybackMember, playback);
        return migrated;
    }

    private static string LoopModeName(DataTreeDictionary dictionary)
    {
        if (dictionary.TryGetNode("WrapMode") is DataTreeValue { IsNull: false } value)
        {
            var name = value.Extract<string>();
            if (!string.IsNullOrEmpty(name) && Enum.TryParse<PlaybackLoopMode>(name, out var parsed))
                return parsed.ToString();
        }
        return PlaybackLoopMode.Loop.ToString();
    }
}
