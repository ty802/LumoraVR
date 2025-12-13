using Godot;
using System;

namespace Lumora.Godot.UI;

/// <summary>
/// Home dashboard UI controller for userspace.
/// Provides navigation between Home, Worlds, Friends, Inventory, and Settings.
/// </summary>
public partial class HomeDash : Control
{
    // Navigation buttons
    private Button? _btnHome;
    private Button? _btnWorlds;
    private Button? _btnFriends;
    private Button? _btnGroups;
    private Button? _btnInventory;
    private Button? _btnSettings;
    private Button? _btnExit;

    // Quick action cards
    private Panel? _joinWorldCard;
    private Panel? _createWorldCard;
    private Panel? _avatarsCard;

    // UI elements
    private Label? _usernameLabel;
    private Label? _patreonRoleLabel;
    private Label? _storageLabel;
    private Panel? _storageBarFill;
    private Label? _avatarIconLabel;
    private Label? _welcomeTitle;
    private Label? _statusText;
    private Label? _versionLabel;
    private Label? _fpsValueLabel;

    // Current active tab
    private string _currentTab = "Home";

    public override void _Ready()
    {
        // Get navigation buttons
        _btnHome = GetNodeOrNull<Button>("MainContainer/VBox/ContentArea/Sidebar/SidebarMargin/NavButtons/BtnHome");
        _btnWorlds = GetNodeOrNull<Button>("MainContainer/VBox/ContentArea/Sidebar/SidebarMargin/NavButtons/BtnWorlds");
        _btnFriends = GetNodeOrNull<Button>("MainContainer/VBox/ContentArea/Sidebar/SidebarMargin/NavButtons/BtnFriends");
        _btnGroups = GetNodeOrNull<Button>("MainContainer/VBox/ContentArea/Sidebar/SidebarMargin/NavButtons/BtnGroups");
        _btnInventory = GetNodeOrNull<Button>("MainContainer/VBox/ContentArea/Sidebar/SidebarMargin/NavButtons/BtnInventory");
        _btnSettings = GetNodeOrNull<Button>("MainContainer/VBox/ContentArea/Sidebar/SidebarMargin/NavButtons/BtnSettings");
        _btnExit = GetNodeOrNull<Button>("MainContainer/VBox/ContentArea/Sidebar/SidebarMargin/NavButtons/BtnExit");

        // Get quick action cards
        _joinWorldCard = GetNodeOrNull<Panel>("MainContainer/VBox/ContentArea/MainContent/ContentMargin/ContentVBox/QuickActions/JoinWorld");
        _createWorldCard = GetNodeOrNull<Panel>("MainContainer/VBox/ContentArea/MainContent/ContentMargin/ContentVBox/QuickActions/CreateWorld");
        _avatarsCard = GetNodeOrNull<Panel>("MainContainer/VBox/ContentArea/MainContent/ContentMargin/ContentVBox/QuickActions/BrowseAvatars");

        // Get user section elements
        _usernameLabel = GetNodeOrNull<Label>("MainContainer/VBox/Header/HeaderContent/UserSectionPanel/UserSectionMargin/UserSection/UserInfoVBox/Username");
        _patreonRoleLabel = GetNodeOrNull<Label>("MainContainer/VBox/Header/HeaderContent/UserSectionPanel/UserSectionMargin/UserSection/UserInfoVBox/PatreonRole");
        _storageLabel = GetNodeOrNull<Label>("MainContainer/VBox/Header/HeaderContent/UserSectionPanel/UserSectionMargin/UserSection/UserInfoVBox/StorageContainer/StorageLabel");
        _storageBarFill = GetNodeOrNull<Panel>("MainContainer/VBox/Header/HeaderContent/UserSectionPanel/UserSectionMargin/UserSection/UserInfoVBox/StorageContainer/StorageBarBg/StorageBarFill");
        _avatarIconLabel = GetNodeOrNull<Label>("MainContainer/VBox/Header/HeaderContent/UserSectionPanel/UserSectionMargin/UserSection/AvatarContainer/AvatarCircle/AvatarIcon");

        // Get other labels
        _welcomeTitle = GetNodeOrNull<Label>("MainContainer/VBox/ContentArea/MainContent/ContentMargin/ContentVBox/WelcomeSection/WelcomeTitle");
        _statusText = GetNodeOrNull<Label>("MainContainer/VBox/StatusBar/StatusContent/ConnectionPanel/ConnectionCenter/ConnectionStatus/StatusText");
        _versionLabel = GetNodeOrNull<Label>("MainContainer/VBox/StatusBar/StatusContent/VersionPanel/VersionCenter/Version");
        _fpsValueLabel = GetNodeOrNull<Label>("MainContainer/VBox/Header/HeaderContent/FPSPanel/FPSMargin/FPSVBox/FPSValue");

        // Connect button signals
        ConnectButtons();

        GD.Print("HomeDash: Initialized");
    }

    private void ConnectButtons()
    {
        _btnHome?.Connect("pressed", Callable.From(() => OnNavButtonPressed("Home")));
        _btnWorlds?.Connect("pressed", Callable.From(() => OnNavButtonPressed("Worlds")));
        _btnFriends?.Connect("pressed", Callable.From(() => OnNavButtonPressed("Friends")));
        _btnGroups?.Connect("pressed", Callable.From(() => OnNavButtonPressed("Groups")));
        _btnInventory?.Connect("pressed", Callable.From(() => OnNavButtonPressed("Inventory")));
        _btnSettings?.Connect("pressed", Callable.From(() => OnNavButtonPressed("Settings")));
        _btnExit?.Connect("pressed", Callable.From(OnExitPressed));

        // Connect card interactions (make them clickable)
        if (_joinWorldCard != null)
        {
            _joinWorldCard.GuiInput += (e) => OnCardInput(e, "JoinWorld");
        }
        if (_createWorldCard != null)
        {
            _createWorldCard.GuiInput += (e) => OnCardInput(e, "CreateWorld");
        }
        if (_avatarsCard != null)
        {
            _avatarsCard.GuiInput += (e) => OnCardInput(e, "Avatars");
        }
    }

    private void OnNavButtonPressed(string tab)
    {
        if (_currentTab == tab) return;

        _currentTab = tab;
        UpdateActiveButton();

        GD.Print($"HomeDash: Navigated to {tab}");

        // TODO: Switch content panels based on tab
        // For now just log the navigation
    }

    private void UpdateActiveButton()
    {
        // Reset all buttons to normal style, set active button to pressed style
        Button?[] buttons = { _btnHome, _btnWorlds, _btnFriends, _btnGroups, _btnInventory, _btnSettings };
        string[] tabs = { "Home", "Worlds", "Friends", "Groups", "Inventory", "Settings" };

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;

            // We'll handle visual feedback via button state
            // The pressed style is applied when button toggles
        }
    }

    private void OnCardInput(InputEvent @event, string cardType)
    {
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            OnCardPressed(cardType);
        }
    }

    private void OnCardPressed(string cardType)
    {
        GD.Print($"HomeDash: Card pressed - {cardType}");

        switch (cardType)
        {
            case "JoinWorld":
                // TODO: Show world browser
                OnNavButtonPressed("Worlds");
                break;
            case "CreateWorld":
                // TODO: Show create world dialog
                break;
            case "Avatars":
                // TODO: Show avatar selector
                OnNavButtonPressed("Inventory");
                break;
        }
    }

    private void OnExitPressed()
    {
        GD.Print("HomeDash: Exit pressed");
        // TODO: Implement exit confirmation or directly quit
        // GetTree().Quit();
    }

    /// <summary>
    /// Set the displayed username.
    /// </summary>
    public void SetUsername(string username)
    {
        if (_usernameLabel != null)
        {
            _usernameLabel.Text = username;
        }
        if (_welcomeTitle != null)
        {
            _welcomeTitle.Text = $"Welcome back, {username}!";
        }
        if (_avatarIconLabel != null)
        {
            // Set first letter of username as avatar placeholder
            _avatarIconLabel.Text = string.IsNullOrEmpty(username) ? "?" : username[0].ToString().ToUpper();
        }
    }

    /// <summary>
    /// Set the Patreon role display.
    /// </summary>
    public void SetPatreonRole(string role)
    {
        if (_patreonRoleLabel != null)
        {
            _patreonRoleLabel.Text = role;
            _patreonRoleLabel.Visible = !string.IsNullOrEmpty(role);
        }
    }

    /// <summary>
    /// Set storage usage display.
    /// </summary>
    /// <param name="usedGB">Used storage in GB</param>
    /// <param name="totalGB">Total storage in GB</param>
    public void SetStorageUsage(float usedGB, float totalGB)
    {
        if (_storageLabel != null)
        {
            _storageLabel.Text = $"Storage: {usedGB:F1} GB / {totalGB:F0} GB";
        }
        if (_storageBarFill != null)
        {
            float percent = totalGB > 0 ? usedGB / totalGB : 0;
            // Storage bar bg is 120px wide, fill should be proportional (with 1px margin)
            float fillWidth = Mathf.Clamp(percent * 118f, 2f, 118f);
            _storageBarFill.CustomMinimumSize = new Vector2(fillWidth, 4);
        }
    }

    /// <summary>
    /// Set the connection status display.
    /// </summary>
    public void SetConnectionStatus(string status, bool isConnected = true)
    {
        if (_statusText != null)
        {
            _statusText.Text = status;
            // TODO: Update status dot color based on isConnected
        }
    }

    /// <summary>
    /// Set the version label text.
    /// </summary>
    public void SetVersion(string version)
    {
        if (_versionLabel != null)
        {
            _versionLabel.Text = $"Lumora {version}";
        }
    }

    /// <summary>
    /// Set the FPS display value.
    /// </summary>
    public void SetFPS(int fps)
    {
        if (_fpsValueLabel != null)
        {
            _fpsValueLabel.Text = fps.ToString();
            // Color code: green for good, yellow for medium, red for low
            if (fps >= 60)
                _fpsValueLabel.Modulate = new Color(0.3f, 0.85f, 0.5f);
            else if (fps >= 30)
                _fpsValueLabel.Modulate = new Color(0.9f, 0.75f, 0.2f);
            else
                _fpsValueLabel.Modulate = new Color(0.9f, 0.4f, 0.35f);
        }
    }
}
