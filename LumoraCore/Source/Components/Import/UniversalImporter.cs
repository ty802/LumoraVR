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
            case AssetClass.PointCloud:
                SpawnModelDialog(list, world, position, rotation, silent);
                break;
            case AssetClass.Video:
                SpawnVideoDialog(list, world, position, rotation, silent);
                break;
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
                // Pipelines for these classes aren't ported yet - fall through to the
                // raw-file path so the file at least appears in the world as a grabbable
                // placeholder. Once StaticBinary/StaticAudioClip/etc. land, branch here. - xlinka
                ImportRawList(list, world, position, rotation);
                Logger.Warn($"UniversalImporter: {assetClass} pipeline not implemented yet; spawned as raw file(s).");
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

    private static void ImportRawList(List<string> files, World world, float3 position, floatQ rotation)
    {
        int rowSize = (int)MathF.Max(1f, MathF.Ceiling(MathF.Sqrt(files.Count)));
        int index = 0;
        foreach (var file in files)
        {
            var slot = world.RootSlot.AddSlot(Path.GetFileName(file) ?? file);
            var offset = GridOffset(ref index, rowSize);
            slot.GlobalPosition = position + rotation * offset;
            slot.GlobalRotation = rotation;
            slot.GlobalScale = float3.One;

            var label = slot.AttachComponent<TextRenderer>();
            label.Text.Value = Path.GetFileName(file) ?? file;
            label.Size.Value = 0.08f;

            var grab = slot.AttachComponent<Grabbable>();
            grab.AllowGrab.Value = true;
            grab.Scalable.Value = true;
        }
    }

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
