// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;

namespace Lumora.Core;

/// <summary>
/// Saved-object store: writes a slot's subtree (with its asset dependencies) to a ".litem" file and
/// spawns it back into a world. Backs the dashboard Inventory tab and the grabbed-object
/// "Save to Inventory" action. Items live under the user's application data folder.
/// </summary>
public static class Inventory
{
    /// <summary>Folder holding saved item files (created on demand).</summary>
    public static string Folder
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lumora", "inventory");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Save a slot's subtree (and its asset dependencies) as a new inventory item.
    /// Returns the file path, or null if the slot can't be saved.
    /// </summary>
    public static string? SaveItem(Slot slot, string? name = null)
    {
        if (slot == null || slot.IsDestroyed || slot.World == null)
            return null;

        var label = Sanitize(string.IsNullOrWhiteSpace(name) ? slot.Name : name!);
        var path = Path.Combine(Folder, $"{label}_{DateTime.Now:yyyyMMdd_HHmmss}.litem");
        try
        {
            // Inventory items are encrypted at rest (worlds/items shouldn't sit in plaintext on disk).
            slot.SaveObjectToFile(path, Persistence.DependencyHandling.CollectAssets, encrypt: true);
            return path;
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Inventory: failed to save '{slot.Name}': {ex.Message}");
            return null;
        }
    }

    /// <summary>List saved item files, newest first.</summary>
    public static IReadOnlyList<string> ListItems()
    {
        try
        {
            var files = Directory.GetFiles(Folder, "*.litem");
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return files;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Spawn a saved item into the world at <paramref name="position"/>. Returns the new slot.</summary>
    public static Slot? SpawnItem(World world, string path, float3 position)
    {
        if (world?.RootSlot == null || !File.Exists(path))
            return null;

        try
        {
            var slot = world.RootSlot.AddSlot(DisplayName(path));
            slot.LoadObjectFromFile(path);
            slot.GlobalPosition = position;
            return slot;
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Inventory: failed to spawn '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Human-readable name for an item file (strips the timestamp suffix when present).</summary>
    public static string DisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        // Trim a trailing "_yyyyMMdd_HHmmss" stamp for display.
        int stamp = name.LastIndexOf('_');
        if (stamp > 0)
        {
            int prev = name.LastIndexOf('_', stamp - 1);
            if (prev > 0 && name.Length - prev == 16)
                return name.Substring(0, prev);
        }
        return name;
    }

    public static bool DeleteItem(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Inventory: failed to delete '{path}': {ex.Message}");
        }
        return false;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Item" : name;
    }
}
