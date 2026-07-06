// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

public static class UniversalImporter
{
    public static void Import(AssetClass assetClass, IEnumerable<string> files, World world, float3 position, floatQ rotation, bool silent = false)
    {
        if (world == null) return;
        var list = new List<string>();
        foreach (var f in files)
        {
            if (!string.IsNullOrEmpty(f)) list.Add(f.Trim());
        }
        if (list.Count == 0) return;

        Logger.Log($"UniversalImporter: Importing {list.Count} file(s) as {assetClass}");

        switch (assetClass)
        {
            case AssetClass.Folder:
                SpawnFolderDialog(list[0], world, position, rotation);
                break;
            case AssetClass.Texture:
                SpawnImageDialog(list, world, position, rotation, silent);
                break;
            case AssetClass.Model:
                SpawnModelDialog(list, world, position, rotation, silent);
                break;
            case AssetClass.Video:
                SpawnVideoDialog(list, world, position, rotation, silent);
                break;
            // PointCloud and the rest have no import pipeline yet. PointCloud in particular
            // can NOT go through the model dialog - its extensions aren't in ModelImporter's
            // supported set and it throws. Route every unsupported class to an honest notice
            // dialog (with a raw-file drop) rather than faking success with a labeled
            // placeholder. As pipelines land (audio/font/text/etc.), give them their own
            // case above. - xlinka
            case AssetClass.PointCloud:
            case AssetClass.Unknown:
            case AssetClass.Document:
            case AssetClass.Audio:
            case AssetClass.Font:
            case AssetClass.Subtitle:
            case AssetClass.Animation:
            case AssetClass.Object:
            case AssetClass.Text:
            case AssetClass.Volume:
            case AssetClass.Cubemap:
            case AssetClass.Special:
            case AssetClass.Shader:
                Logger.Warn($"UniversalImporter: {assetClass} import is not supported yet ({list.Count} file(s)).");
                SpawnUnsupportedDialog(assetClass, list, world, position, rotation);
                break;
        }
    }

    public static void Import(string path, World world, float3 position, floatQ rotation, bool silent = false, bool rawFile = false)
    {
        var cls = rawFile ? AssetClass.Unknown : AssetHelper.IdentifyClass(path);
        Import(cls, new[] { path }, world, position, rotation, silent);
    }

    public static float3 GridOffset(ref int index, int rowSize)
    {
        var result = new float3(index % rowSize, index / rowSize, 0f);
        index++;
        return result;
    }

    // Spawn an honest "not supported yet" dialog for an asset class with no pipeline.
    // It tells the user the format isn't handled and offers a raw-file drop - it never
    // fabricates a placeholder that pretends the content loaded. - xlinka
    private static void SpawnUnsupportedDialog(AssetClass assetClass, List<string> files, World world, float3 position, floatQ rotation)
    {
        var slot = CreateDialogSlot(world, "Unsupported Importer", position, rotation);
        var dialog = slot.AttachComponent<UnsupportedImportDialog>();
        dialog.TargetWorld = world;
        dialog.ClassName = ClassDisplayName(assetClass);
        dialog.Paths.AddRange(files);
        dialog.SetLocalUserAsImporting();
    }

    private static string ClassDisplayName(AssetClass assetClass) => assetClass switch
    {
        AssetClass.PointCloud => "Point cloud",
        AssetClass.Unknown => "This file type",
        _ => assetClass.ToString(),
    };

    private static Slot CreateDialogSlot(World world, string name, float3 position, floatQ rotation)
    {
        // Dialog lives in the focused world (where the user and their laser
        // pointer are). Local slot so it's per-user/non-synced. The dialog
        // creates its own FontProvider in this same world via DefaultFontUrl
        // so no cross-world asset refs are needed. - xlinka
        var slot = world.RootSlot.AddLocalSlot(name);
        slot.GlobalPosition = position;
        slot.GlobalRotation = rotation;
        slot.GlobalScale = float3.One;
        return slot;
    }

    private static void SpawnFolderDialog(string folder, World world, float3 position, floatQ rotation)
    {
        var slot = CreateDialogSlot(world, "Folder Importer", position, rotation);
        var dialog = slot.AttachComponent<FolderImportDialog>();
        dialog.TargetWorld = world;
        dialog.Path.Value = folder;
        dialog.SetLocalUserAsImporting();
    }

    private static void SpawnImageDialog(List<string> files, World world, float3 position, floatQ rotation, bool silent)
    {
        var slot = CreateDialogSlot(world, "Image Importer", position, rotation);
        var dialog = slot.AttachComponent<ImageImportDialog>();
        dialog.TargetWorld = world;
        dialog.Paths.AddRange(files);
        dialog.SetLocalUserAsImporting();
        if (silent) dialog.RunImport();
    }

    private static void SpawnModelDialog(List<string> files, World world, float3 position, floatQ rotation, bool silent)
    {
        var slot = CreateDialogSlot(world, "Model Importer", position, rotation);
        var dialog = slot.AttachComponent<ModelImportDialog>();
        dialog.TargetWorld = world;
        dialog.Paths.AddRange(files);
        dialog.SetLocalUserAsImporting();
        if (silent)
        {
            dialog.AutoScale.Value = true;
            dialog.RunImport();
        }
    }

    private static void SpawnVideoDialog(List<string> files, World world, float3 position, floatQ rotation, bool silent)
    {
        var slot = CreateDialogSlot(world, "Video Importer", position, rotation);
        var dialog = slot.AttachComponent<VideoImportDialog>();
        dialog.TargetWorld = world;
        dialog.Paths.AddRange(files);
        dialog.SetLocalUserAsImporting();
        if (silent) dialog.RunImport();
    }
}
