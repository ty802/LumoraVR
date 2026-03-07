// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Godot.UI;

/// <summary>
/// Partial class: progress bar display and import status reporting.
/// </summary>
public partial class ImportDialog
{
    /// <summary>
    /// Show or hide the progress panel and update UI lock state.
    /// </summary>
    private void SetImportInProgress(bool importing, string status = "", float progress = 0f)
    {
        _isImporting = importing;

        if (_progressPanel != null)
            _progressPanel.Visible = importing;

        if (_progressStatusLabel != null)
            _progressStatusLabel.Text = importing ? status : string.Empty;

        if (_progressBar != null)
            _progressBar.Value = Mathf.Clamp(progress * 100f, 0f, 100f);

        if (_btnClose != null)
            _btnClose.Disabled = importing;

        if (_btnInfo != null)
            _btnInfo.Disabled = importing;

        foreach (var button in _optionButtons)
            button.Disabled = importing;

        if (_btnAvatarSetup != null)
            _btnAvatarSetup.Disabled = importing || _lastImportedAvatarSlot == null || _lastImportedAvatarSlot.IsDestroyed;
    }

    /// <summary>
    /// Thread-safe progress report — deferred to main thread via CallDeferred.
    /// </summary>
    private void ReportProgress(float progress, string status)
    {
        CallDeferred(nameof(ApplyProgressUpdate), progress, status ?? string.Empty);
    }

    private void ApplyProgressUpdate(float progress, string status)
    {
        SetImportInProgress(true, status, progress);
    }

    /// <summary>
    /// Transition out of "in-progress" mode while keeping the status message visible.
    /// Re-enables all interactive controls.
    /// </summary>
    private void SetCompletedStatus(string status, bool success = true)
    {
        _isImporting = false;

        if (_progressPanel != null)
            _progressPanel.Visible = true;

        if (_progressStatusLabel != null)
            _progressStatusLabel.Text = status;

        if (_progressBar != null)
            _progressBar.Value = success ? 100f : 0f;

        if (_btnClose != null)
            _btnClose.Disabled = false;

        if (_btnInfo != null)
            _btnInfo.Disabled = false;

        foreach (var button in _optionButtons)
            button.Disabled = false;

        UpdateAvatarSetupButton();
    }
}
