using Godot;
using System;

namespace Lumora.Godot.UI;

/// <summary>
/// Import dialog for selecting how to import files.
/// </summary>
public partial class ImportDialog : Control
{
    public enum ImportType
    {
        ImageTexture,
        RawFile
    }

    private Button? _btnClose;
    private Button? _btnInfo;
    private Button? _btnImageTexture;
    private Button? _btnRawFile;
    private Label? _titleLabel;

    public event Action<ImportType>? ImportTypeSelected;
    public event Action? DialogClosed;

    private ImportType? _selectedType;

    public override void _Ready()
    {
        _btnClose = GetNodeOrNull<Button>("MainMargin/VBox/Header/CloseButton");
        _btnInfo = GetNodeOrNull<Button>("MainMargin/VBox/Header/InfoButton");
        _titleLabel = GetNodeOrNull<Label>("MainMargin/VBox/Header/Title");

        _btnImageTexture = GetNodeOrNull<Button>("MainMargin/VBox/OptionsList/BtnImageTexture");
        _btnRawFile = GetNodeOrNull<Button>("MainMargin/VBox/OptionsList/BtnRawFile");

        ConnectSignals();
        GD.Print("ImportDialog: Initialized");
    }

    private void ConnectSignals()
    {
        _btnClose?.Connect("pressed", Callable.From(OnClosePressed));
        _btnInfo?.Connect("pressed", Callable.From(OnInfoPressed));

        _btnImageTexture?.Connect("pressed", Callable.From(() => OnOptionSelected(ImportType.ImageTexture)));
        _btnRawFile?.Connect("pressed", Callable.From(() => OnOptionSelected(ImportType.RawFile)));
    }

    private void OnClosePressed()
    {
        GD.Print("ImportDialog: Closed");
        DialogClosed?.Invoke();
        Hide();
    }

    private void OnInfoPressed()
    {
        GD.Print("ImportDialog: Info button pressed");
    }

    private void OnOptionSelected(ImportType type)
    {
        _selectedType = type;
        GD.Print($"ImportDialog: Selected {type}");
        ImportTypeSelected?.Invoke(type);
    }

    public void SetTitle(string title)
    {
        if (_titleLabel != null)
            _titleLabel.Text = title;
    }

    public ImportType? GetSelectedType() => _selectedType;

    public void ShowDialog()
    {
        _selectedType = null;
        Show();
    }
}
