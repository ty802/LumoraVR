// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Security;

/// <summary>
/// Tracks banned users. In-memory <b>temp bans</b> (this process only)
/// plus durable bans persisted through the shared <see cref="Settings"/> config store — keyed by user
/// id and/or machine id, scoped either globally or to one world. The authority consults
/// <see cref="IsBanned"/> when a user tries to join.
/// </summary>
public static class BanManager
{
    private const string GlobalBanRoot = "Security.Ban.Blacklist";
    private const string WorldBanRoot = "Security.Ban.WorldBlacklist";

    public readonly struct BanEntry
    {
        public readonly string UserId;
        public readonly string MachineId;
        public readonly string Username;

        public BanEntry(string userId, string machineId, string username)
        {
            UserId = userId ?? "";
            MachineId = machineId ?? "";
            Username = username ?? "";
        }
    }

    private static readonly object _lock = new();
    private static readonly HashSet<string> _tempUserIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _tempMachineIds = new(StringComparer.Ordinal);

    // TEMP BANS (this process only) — "keep them out until restart" without persisting.

    public static void TempBan(string? userId, string? machineId)
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(userId)) _tempUserIds.Add(userId);
            if (!string.IsNullOrEmpty(machineId)) _tempMachineIds.Add(machineId);
        }
    }

    public static bool IsTempBanned(string? userId, string? machineId)
    {
        lock (_lock)
        {
            return (!string.IsNullOrEmpty(userId) && _tempUserIds.Contains(userId))
                || (!string.IsNullOrEmpty(machineId) && _tempMachineIds.Contains(machineId));
        }
    }

    // PERSISTENT BANS (via the Settings store). worldKey null/empty = a global ban.

    public static string BanRoot(string? worldKey)
        => string.IsNullOrEmpty(worldKey) ? GlobalBanRoot : WorldBanRoot + "." + Sanitize(worldKey!);

    private static string BanEntryRoot(string banId, string? worldKey) => BanRoot(worldKey) + "." + banId;

    public static void AddBan(string? username, string? userId, string? machineId, string? worldKey = null)
    {
        // Replace any existing ban for this user in this scope.
        if (!string.IsNullOrEmpty(userId))
            RemoveBanByUserId(userId, worldKey);

        var root = BanEntryRoot(Guid.NewGuid().ToString("N"), worldKey) + ".";
        Settings.WriteValue(root + "Username", username ?? "");
        if (!string.IsNullOrWhiteSpace(userId)) Settings.WriteValue(root + "UserId", userId!);
        if (!string.IsNullOrWhiteSpace(machineId)) Settings.WriteValue(root + "MachineId", machineId!);
    }

    public static bool RemoveBanByUserId(string userId, string? worldKey = null)
    {
        var banId = FindBanByProperty("UserId", userId, worldKey);
        if (banId == null) return false;
        Settings.ClearSettings(BanEntryRoot(banId, worldKey));
        return true;
    }

    /// <summary>True if temp-banned, globally banned, or banned in <paramref name="worldKey"/>.</summary>
    public static bool IsBanned(string? userId, string? machineId, string? worldKey = null)
    {
        if (IsTempBanned(userId, machineId)) return true;
        if (IsBannedIn(userId, machineId, null)) return true;
        return !string.IsNullOrEmpty(worldKey) && IsBannedIn(userId, machineId, worldKey);
    }

    private static bool IsBannedIn(string? userId, string? machineId, string? worldKey)
    {
        foreach (var banId in Settings.ListSettings(BanRoot(worldKey)))
        {
            var root = BanEntryRoot(banId, worldKey) + ".";
            if (!string.IsNullOrEmpty(userId) && Settings.ReadValue<string?>(root + "UserId", null) == userId)
                return true;
            if (!string.IsNullOrEmpty(machineId) && Settings.ReadValue<string?>(root + "MachineId", null) == machineId)
                return true;
        }
        return false;
    }

    private static string? FindBanByProperty(string property, string matchValue, string? worldKey)
    {
        foreach (var banId in Settings.ListSettings(BanRoot(worldKey)))
        {
            if (Settings.ReadValue<string?>(BanEntryRoot(banId, worldKey) + "." + property, null) == matchValue)
                return banId;
        }
        return null;
    }

    /// <summary>Persistent bans in a scope (global when worldKey is null).</summary>
    public static List<BanEntry> ListBans(string? worldKey = null)
    {
        var result = new List<BanEntry>();
        foreach (var banId in Settings.ListSettings(BanRoot(worldKey)))
        {
            var root = BanEntryRoot(banId, worldKey) + ".";
            result.Add(new BanEntry(
                Settings.ReadValue<string?>(root + "UserId", null) ?? "",
                Settings.ReadValue<string?>(root + "MachineId", null) ?? "",
                Settings.ReadValue<string?>(root + "Username", null) ?? ""));
        }
        return result;
    }

    // World keys become part of a dotted settings path, so strip dots.
    private static string Sanitize(string value) => value.Replace('.', '_');
}
