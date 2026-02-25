using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Godot.UI;

/// <summary>
/// Import dialog for selecting how to import files.
/// Supports images, 3D models, avatars, and raw files.
/// Functionality is split across partial class files:
///   ImportDialog.TypeSelector.cs  — option button creation/filtering
///   ImportDialog.Progress.cs      — progress bar and status reporting
///   ImportDialog.Executor.cs      — per-type import logic
///   ImportDialog.AvatarSetup.cs   — avatar setup pedestal flow
/// </summary>
public partial class ImportDialog : Control
{
    public enum ImportType
    {
        ImageTexture,
        Model3D,
        Avatar,
        RawFile
    }

    private const string OptionButtonScenePath = "res://Scenes/UI/Components/ImportOptionButton.tscn";
    internal const string AvatarSetupMarkersRootName = "AvatarSetupMarkers";

    // ── UI node references ──────────────────────────────────────────────────
    private Button _btnClose;
    private Button _btnInfo;
    private Button _btnAvatarSetup;
    private Label _titleLabel;
    private Label _subtitleLabel;
    private VBoxContainer _optionsList;
    private Label _fileInfoLabel;
    private Control _progressPanel;
    private Label _progressStatusLabel;
    private ProgressBar _progressBar;
    private PackedScene _optionButtonScene;
    private readonly List<Button> _optionButtons = new();

    // ── State ───────────────────────────────────────────────────────────────
    private bool _isInitialized;
    private bool _isImporting;
    private ImportType? _selectedType;
    private string _filePath;
    private LocalDB _localDB;
    private Slot _targetSlot;
    private Slot _lastImportedAvatarSlot;
    private Slot _lastAvatarSetupPedestal;

    public event Action<ImportType, string> ImportRequested;
    public event Action DialogClosed;

    // ── Godot lifecycle ─────────────────────────────────────────────────────

    public override void _Ready()
    {
        _btnClose            = GetNodeOrNull<Button>("MainMargin/VBox/Header/CloseButton");
        _btnInfo             = GetNodeOrNull<Button>("MainMargin/VBox/Header/InfoButton");
        _btnAvatarSetup      = GetNodeOrNull<Button>("MainMargin/VBox/AvatarSetupButton");
        _titleLabel          = GetNodeOrNull<Label>("MainMargin/VBox/Header/Title");
        _subtitleLabel       = GetNodeOrNull<Label>("MainMargin/VBox/Subtitle");
        _optionsList         = GetNodeOrNull<VBoxContainer>("MainMargin/VBox/OptionsList");
        _fileInfoLabel       = GetNodeOrNull<Label>("MainMargin/VBox/FileInfo");
        _progressPanel       = GetNodeOrNull<Control>("MainMargin/VBox/ProgressPanel");
        _progressStatusLabel = GetNodeOrNull<Label>("MainMargin/VBox/ProgressPanel/Status");
        _progressBar         = GetNodeOrNull<ProgressBar>("MainMargin/VBox/ProgressPanel/Bar");

        _optionButtonScene = GD.Load<PackedScene>(OptionButtonScenePath);
        if (_optionButtonScene == null)
            GD.PrintErr($"ImportDialog: Failed to load option button scene from {OptionButtonScenePath}");

        ConnectSignals();
        _isInitialized = true;
        SetImportInProgress(false);
        UpdateAvatarSetupButton();
        RefreshDialogForCurrentFile();
        GD.Print("ImportDialog: Initialized");
    }

    private void ConnectSignals()
    {
        _btnClose?.Connect("pressed", Callable.From(OnClosePressed));
        _btnInfo?.Connect("pressed", Callable.From(OnInfoPressed));
        _btnAvatarSetup?.Connect("pressed", Callable.From(OnAvatarSetupPressed));
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Show the import dialog pre-configured for a specific file.</summary>
    public void ShowForFile(string filePath, Slot targetSlot = null, LocalDB localDB = null)
    {
        _filePath = filePath;
        _targetSlot = targetSlot;
        _localDB = localDB;
        _selectedType = null;
        _isImporting = false;
        _lastImportedAvatarSlot = null;
        _lastAvatarSetupPedestal = null;

        if (_isInitialized)
        {
            UpdateAvatarSetupButton();
            RefreshDialogForCurrentFile();
            Show();
        }
    }

    /// <summary>Show the import dialog in generic (no file pre-selected) mode.</summary>
    public void ShowDialog(Slot targetSlot = null, LocalDB localDB = null)
    {
        _filePath = null;
        _targetSlot = targetSlot;
        _localDB = localDB;
        _selectedType = null;
        _isImporting = false;
        _lastImportedAvatarSlot = null;
        _lastAvatarSetupPedestal = null;

        if (_isInitialized)
        {
            UpdateAvatarSetupButton();
            RefreshDialogForCurrentFile();
            Show();
        }
    }

    public void SetTitle(string title)
    {
        if (_titleLabel != null)
            _titleLabel.Text = title;
    }

    public void SetSubtitle(string subtitle)
    {
        if (_subtitleLabel != null)
            _subtitleLabel.Text = subtitle;
    }

    public ImportType? GetSelectedType() => _selectedType;
    public string GetFilePath() => _filePath;

    /// <summary>Set the LocalDB instance used for asset imports.</summary>
    public void SetLocalDB(LocalDB localDB) => _localDB = localDB;

    /// <summary>Set the target slot that imported objects are placed into.</summary>
    public void SetTargetSlot(Slot targetSlot) => _targetSlot = targetSlot;

    // ── Internal UI coordination ─────────────────────────────────────────────

    private void RefreshDialogForCurrentFile()
    {
        if (!_isInitialized) return;

        if (string.IsNullOrWhiteSpace(_filePath))
        {
            SetTitle("Import");
            SetSubtitle("What are you importing?");
            if (_fileInfoLabel != null)
                _fileInfoLabel.Visible = false;
            CreateAllOptionButtons();
        }
        else
        {
            SetTitle($"Import: {Path.GetFileName(_filePath)}");
            SetSubtitle("Choose import type");
            if (_fileInfoLabel != null)
            {
                _fileInfoLabel.Text = _filePath;
                _fileInfoLabel.Visible = true;
            }
            CreateOptionButtonsForFile(Path.GetExtension(_filePath).ToLowerInvariant());
        }

        SetImportInProgress(_isImporting);
    }

    private void OnClosePressed()
    {
        if (_isImporting) return;
        GD.Print("ImportDialog: Closed");
        DialogClosed?.Invoke();
        Hide();
    }

    private void OnInfoPressed()
    {
        GD.Print("ImportDialog: Info - Supported formats:");
        GD.Print("  Images: PNG, JPG, JPEG, WebP, BMP, TGA");
        GD.Print("  3D Models: GLB, GLTF");
        GD.Print("  Avatars: VRM, GLB, GLTF");
    }

    private async void OnOptionSelected(ImportType type)
    {
        if (_isImporting) return;

        if (type != ImportType.Avatar)
        {
            _lastImportedAvatarSlot = null;
            _lastAvatarSetupPedestal = null;
            UpdateAvatarSetupButton();
        }

        _selectedType = type;
        GD.Print($"ImportDialog: Selected {type}");
        ImportRequested?.Invoke(type, _filePath);

        bool success = true;
        if (!string.IsNullOrEmpty(_filePath) && _targetSlot != null)
            success = await PerformImport(type, _filePath);

        if (success)
            SetSubtitle("Import finished. Close when done.");
    }
}
