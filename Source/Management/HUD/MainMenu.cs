using Godot;
using System;
using Aquamarine.Source.Input;
using Aquamarine.Source.Management;
using Aquamarine.Source.Logging;

namespace Aquamarine.Source.Management.HUD;

/// <summary>
/// Enhanced main menu with account login functionality
/// </summary>
public partial class MainMenu : Control
{
    [Export] public Button CloseButton;

    // We'll need to reference the account button in the sidebar
    [Export] public Button AccountButton;

    // Reference main content area to hide when showing login UI
    [Export] public Control MainPanel;

    private Control _loginUI;
    private PackedScene _loginUIScene;
    private Control _mainPanelContent;

    public override void _Ready()
    {
        base._Ready();
        CloseButton.Pressed += ToggleMenu;
        Visible = false;

        // Load the login UI scene
        _loginUIScene = ResourceLoader.Load<PackedScene>("res://Scenes/UI/login_ui.tscn");

        // Find the account button by path if not explicitly set
        if (AccountButton == null)
        {
            AccountButton = GetNode<Button>("MarginContainer/HBoxContainer/Side Panel/MarginContainer/VBoxContainer2/VBoxContainer2/Button2");
        }

        // Connect the account button to show login UI
        if (AccountButton != null)
        {
            AccountButton.Pressed += ShowLoginUI;
        }
        else
        {
            Logger.Error("Failed to find Account button in MainMenu");
        }

        // Listen for login status changes to update the account button text
        if (LoginManager.Instance != null)
        {
            LoginManager.Instance.OnLoginStatusChanged += UpdateAccountButtonText;
        }
    }

    public override void _ExitTree()
    {
        // Clean up event listener
        if (LoginManager.Instance != null)
        {
            LoginManager.Instance.OnLoginStatusChanged -= UpdateAccountButtonText;
        }

        base._ExitTree();
    }

    /// <summary>
    /// Shows or hides the main menu
    /// </summary>
    void ToggleMenu()
    {
        InputManager.MovementLocked = !InputManager.MovementLocked;
        Visible = InputManager.MovementLocked;

        // Hide the login UI and restore main content when closing the menu
        if (!Visible)
        {
            HideLoginUI();
        }
    }

    /// <summary>
    /// Shows the login UI when the account button is pressed
    /// </summary>
    void ShowLoginUI()
    {
        // Find the main panel content if not already referenced
        if (MainPanel == null)
        {
            MainPanel = GetNode<Control>("MarginContainer/HBoxContainer/Main Panel");
        }

        if (_mainPanelContent == null && MainPanel != null)
        {
            // Find the current main content (primary container)
            _mainPanelContent = MainPanel.GetNode<Control>("Primary Container");
        }

        // Create the login UI if it doesn't exist yet
        if (_loginUI == null)
        {
            if (_loginUIScene != null)
            {
                _loginUI = _loginUIScene.Instantiate<Control>();

                // Add the login UI to the main panel
                if (MainPanel != null)
                {
                    MainPanel.AddChild(_loginUI);
                }
                else
                {
                    // Fallback to adding as a direct child if main panel not found
                    AddChild(_loginUI);
                }

                // Make the login UI fill the main panel area
                _loginUI.AnchorRight = 1;
                _loginUI.AnchorBottom = 1;
                _loginUI.SizeFlagsHorizontal = SizeFlags.Fill;
                _loginUI.SizeFlagsVertical = SizeFlags.Fill;
            }
            else
            {
                Logger.Error("Login UI scene could not be loaded");
                return;
            }
        }

        // Hide the main content and show the login UI
        if (_mainPanelContent != null)
        {
            _mainPanelContent.Visible = false;
        }

        // Show the login UI
        _loginUI.Visible = true;
    }

    /// <summary>
    /// Hides the login UI and shows the main content
    /// </summary>
    public void HideLoginUI()
    {
        if (_loginUI != null)
        {
            _loginUI.Visible = false;
        }

        if (_mainPanelContent != null)
        {
            _mainPanelContent.Visible = true;
        }
    }

    /// <summary>
    /// Updates the account button text based on login status
    /// </summary>
    public void UpdateAccountButtonText(bool isLoggedIn)
    {
        if (AccountButton == null) return;

        // If we have a RichTextLabelAutoSizeNode as a child of the button, update its text
        var textNode = AccountButton.GetNode<Control>("RichTextLabelAutoSizeNode");
        if (textNode == null) return;

        var textLabel = textNode.GetNode<RichTextLabel>("_RichTextLabel_61502");
        if (textLabel == null) return;

        // Update the text based on login status
        if (isLoggedIn && LoginManager.Instance != null)
        {
            var username = LoginManager.Instance.GetCurrentUsername();
            textLabel.Text = $"[center]{username} 👤[/center]";
        }
        else
        {
            textLabel.Text = "[center]Account 💻[/center]";
        }
    }
}
