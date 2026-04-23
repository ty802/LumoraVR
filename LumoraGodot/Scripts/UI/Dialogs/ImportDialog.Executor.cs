// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Assets;

namespace Lumora.Godot.UI;

/// <summary>
/// Partial class: per-type import execution.
/// </summary>
public partial class ImportDialog
{
    private async Task<bool> PerformImport(ImportType type, string filePath)
    {
        GD.Print($"ImportDialog: Performing {type} import of '{filePath}'");
        _isImporting = true;
        SetImportInProgress(true, $"Importing {type}...", 0f);
        SetSubtitle("Import in progress...");

        var progress = new Progress<(float progress, string status)>(
            update => ReportProgress(update.progress, update.status));

        try
        {
            switch (type)
            {
                case ImportType.ImageTexture:
                    ReportProgress(0.1f, "Importing image...");
                    await ImportImage(filePath);
                    ReportProgress(1.0f, "Image import complete");
                    break;

                case ImportType.Model3D:
                    await ImportModel(filePath, isAvatar: false, progress);
                    break;

                case ImportType.Avatar:
                    await ImportModel(filePath, isAvatar: true, progress);
                    break;

                case ImportType.RawFile:
                    ReportProgress(0.1f, "Importing raw file...");
                    await ImportRawFile(filePath);
                    ReportProgress(1.0f, "Raw file import complete");
                    break;
            }

            SetSubtitle("Import completed");
            if (type == ImportType.Avatar)
                SetCompletedStatus("Avatar imported as draft. Open Creator to finish it.");
            else
                SetCompletedStatus("Import queued. Model may take a moment to appear in-world.");

            GD.Print("ImportDialog: Import completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ImportDialog: Import failed: {ex.Message}");
            SetSubtitle($"Import failed: {ex.Message}");
            SetCompletedStatus($"Import failed: {ex.Message}", success: false);
            return false;
        }
        finally
        {
            _isImporting = false;
        }
    }

    private async Task ImportImage(string filePath)
    {
        string localUri = null;
        if (_localDB != null)
            localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);

        _targetSlot.AddSlot(Path.GetFileNameWithoutExtension(filePath));
        GD.Print($"ImportDialog: Image imported to {localUri ?? filePath}");
    }

    private async Task ImportModel(string filePath, bool isAvatar, IProgress<(float progress, string status)> progress)
    {
        ModelImportResult result;

        if (isAvatar)
        {
            progress?.Report((0.05f, "Preparing avatar import..."));
            result = await ModelImporter.ImportAvatarAsync(filePath, _targetSlot, _localDB, progress);
        }
        else
        {
            progress?.Report((0.05f, "Preparing model import..."));
            result = await ModelImporter.ImportModelAsync(filePath, _targetSlot, null, _localDB, progress);
        }

        if (!result.Success)
        {
            GD.PrintErr($"ImportDialog: Model import failed: {result.ErrorMessage}");
            progress?.Report((0f, $"Import failed: {result.ErrorMessage}"));
            throw new InvalidOperationException(result.ErrorMessage);
        }

        if (isAvatar)
        {
            ResetAvatarCreatorState();
            _lastImportedAvatarSlot = result.RootSlot;
            UpdateAvatarSetupButton();
        }
        else
        {
            _lastImportedAvatarSlot = null;
            UpdateAvatarSetupButton();
        }

        progress?.Report((0.90f, isAvatar ? "Avatar imported as draft..." : "Finalizing model..."));
        GD.Print($"ImportDialog: Model imported successfully to slot '{result.RootSlot?.SlotName.Value}'");
        progress?.Report((1.0f, isAvatar ? "Avatar imported as draft" : "Model import complete"));
    }

    private async Task ImportRawFile(string filePath)
    {
        if (_localDB != null)
        {
            var localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
            GD.Print($"ImportDialog: Raw file imported to {localUri}");
        }
    }
}
