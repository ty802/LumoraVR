// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Persistence;

/// <summary>
/// Persists a <see cref="World"/> to and from bytes/files: <c>World.SaveWorld</c> -&gt; data tree
/// -&gt; <see cref="DataTreeConverter"/> bytes, and back. Used for the local home save; richer
/// record/cloud storage and asset bundling can layer on top later.
/// </summary>
public static class WorldStorage
{
    public static byte[] Serialize(World world) => DataTreeConverter.SaveToBytes(world.SaveWorld());

    public static void Deserialize(World world, byte[] bytes)
        => world.LoadWorld((DataTreeDictionary)DataTreeConverter.LoadFromBytes(bytes));

    /// <summary>Write the world to <paramref name="path"/>, creating directories as needed.</summary>
    public static bool SaveToFile(World world, string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Write to a temp sibling and move, so a crash mid-write can't corrupt the save.
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, Serialize(world));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"WorldStorage: failed to save '{world?.Name}' to '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Load the world from <paramref name="path"/>; returns false if absent or on failure.</summary>
    public static bool LoadFromFile(World world, string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            Deserialize(world, File.ReadAllBytes(path));
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"WorldStorage: failed to load from '{path}': {ex.Message}");
            return false;
        }
    }
}
