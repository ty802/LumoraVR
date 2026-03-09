// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using Lumora.CDN;

namespace Lumora.Godot.UI;

/// <summary>
/// Settings page with Profile, Security, and Preferences tabs.
/// </summary>
public partial class Settings : Control
{
    // Tab buttons
    private Button? _tabProfile;
    private Button? _tabSecurity;
    private Button? _tabPreferences;

    // Tab content
    private Control? _profileTab;
    private Control? _securityTab;
    private Control? _preferencesTab;

    // Profile tab elements
    private Label? _avatarLabel;
    private TextEdit? _bioInput;
    private Button? _btnChangeAvatar;
    private Button? _btnSaveProfile;

    // Security tab elements
    private LineEdit? _currentPasswordInput;
    private LineEdit? _newPasswordInput;
    private Button? _btnChangePassword;
    private Label? _twoFactorStatus;
    private Button? _btnEnable2FA;

    // Preferences tab elements
    private HSlider? _masterVolumeSlider;
    private HSlider? _musicVolumeSlider;
    private Label? _masterVolumeValue;
    private Label? _musicVolumeValue;
    private OptionButton? _outputDeviceOption;
    private OptionButton? _inputDeviceOption;
    private OptionButton? _qualityOption;
    private CheckButton? _fpsLimitToggle;
    private HSlider? _fpsLimitSlider;
    private Label? _fpsLimitValue;
    private CheckButton? _vsyncToggle;
    private CheckButton? _fullscreenToggle;
    private ProgressBar? _outputActivityBar;
    private ProgressBar? _inputActivityBar;
    private int _outputMeterBus = -1;
    private int _inputMeterBus = -1;
    private float _outputMeterLevel;
    private float _inputMeterLevel;
    private AudioStreamPlayer? _inputMonitorPlayer;
    private AudioEffectCapture? _inputCaptureEffect;

    private const string InputMonitorBusName = "LumoraInputMonitor";
    private const float MeterAttackSpeed = 4.0f;
    private const float MeterReleaseSpeed = 1.8f;

    // Style resources for tab switching
    private StyleBox? _tabActiveStyle;
    private StyleBox? _tabNormalStyle;

    // Auth
    private LumoraClient? _client;
    private bool _has2FA;

    private string _currentTab = "Profile";

    public override void _Ready()
    {
        // Get tab buttons
        _tabProfile = GetNodeOrNull<Button>("VBox/Header/TabProfile");
        _tabSecurity = GetNodeOrNull<Button>("VBox/Header/TabSecurity");
        _tabPreferences = GetNodeOrNull<Button>("VBox/Header/TabPreferences");

        // Get tab content containers
        _profileTab = GetNodeOrNull<Control>("VBox/ContentPanel/Margin/TabContainer/ProfileTab");
        _securityTab = GetNodeOrNull<Control>("VBox/ContentPanel/Margin/TabContainer/SecurityTab");
        _preferencesTab = GetNodeOrNull<Control>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab");

        // Get profile elements
        _avatarLabel = GetNodeOrNull<Label>("VBox/ContentPanel/Margin/TabContainer/ProfileTab/ProfileContent/AvatarSection/AvatarMargin/AvatarHBox/AvatarPreview/AvatarLabel");
        _bioInput = GetNodeOrNull<TextEdit>("VBox/ContentPanel/Margin/TabContainer/ProfileTab/ProfileContent/BioSection/BioInput");
        _btnChangeAvatar = GetNodeOrNull<Button>("VBox/ContentPanel/Margin/TabContainer/ProfileTab/ProfileContent/AvatarSection/AvatarMargin/AvatarHBox/AvatarInfo/BtnChangeAvatar");
        _btnSaveProfile = GetNodeOrNull<Button>("VBox/ContentPanel/Margin/TabContainer/ProfileTab/ProfileContent/SaveButton");

        // Get security elements
        _currentPasswordInput = GetNodeOrNull<LineEdit>("VBox/ContentPanel/Margin/TabContainer/SecurityTab/SecurityContent/PasswordSection/PasswordMargin/PasswordVBox/CurrentPassword");
        _newPasswordInput = GetNodeOrNull<LineEdit>("VBox/ContentPanel/Margin/TabContainer/SecurityTab/SecurityContent/PasswordSection/PasswordMargin/PasswordVBox/NewPassword");
        _btnChangePassword = GetNodeOrNull<Button>("VBox/ContentPanel/Margin/TabContainer/SecurityTab/SecurityContent/PasswordSection/PasswordMargin/PasswordVBox/BtnChangePassword");
        _twoFactorStatus = GetNodeOrNull<Label>("VBox/ContentPanel/Margin/TabContainer/SecurityTab/SecurityContent/TwoFactorSection/TwoFactorMargin/TwoFactorHBox/TwoFactorInfo/TwoFactorStatus");
        _btnEnable2FA = GetNodeOrNull<Button>("VBox/ContentPanel/Margin/TabContainer/SecurityTab/SecurityContent/TwoFactorSection/TwoFactorMargin/TwoFactorHBox/BtnEnable2FA");

        // Get preferences elements
        _masterVolumeSlider = GetNodeOrNull<HSlider>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/AudioSection/AudioMargin/AudioVBox/MasterVolumeHBox/MasterSlider");
        _musicVolumeSlider = GetNodeOrNull<HSlider>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/AudioSection/AudioMargin/AudioVBox/MusicVolumeHBox/MusicSlider");
        _masterVolumeValue = GetNodeOrNull<Label>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/AudioSection/AudioMargin/AudioVBox/MasterVolumeHBox/MasterValue");
        _musicVolumeValue = GetNodeOrNull<Label>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/AudioSection/AudioMargin/AudioVBox/MusicVolumeHBox/MusicValue");
        _qualityOption = GetNodeOrNull<OptionButton>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/GraphicsSection/GraphicsMargin/GraphicsVBox/QualityHBox/QualityOption");
        _fpsLimitToggle = GetNodeOrNull<CheckButton>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/GraphicsSection/GraphicsMargin/GraphicsVBox/FPSLimitHBox/FPSLimitToggle");
        _fpsLimitSlider = GetNodeOrNull<HSlider>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/GraphicsSection/GraphicsMargin/GraphicsVBox/FPSLimitHBox/FPSLimitSlider");
        _fpsLimitValue = GetNodeOrNull<Label>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/GraphicsSection/GraphicsMargin/GraphicsVBox/FPSLimitHBox/FPSLimitValue");
        _vsyncToggle = GetNodeOrNull<CheckButton>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/GraphicsSection/GraphicsMargin/GraphicsVBox/VSyncHBox/VSyncToggle");
        _fullscreenToggle = GetNodeOrNull<CheckButton>("VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/GraphicsSection/GraphicsMargin/GraphicsVBox/FullscreenHBox/FullscreenToggle");

        BuildAudioDeviceRows();

        // Get styles for tab switching
        _tabActiveStyle = _tabProfile?.GetThemeStylebox("normal");
        _tabNormalStyle = _tabSecurity?.GetThemeStylebox("normal");

        ConnectSignals();
        UpdateTabVisuals();
        SetProcess(true);
        ResolveMeterBuses();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateMeterBar(_outputActivityBar, ref _outputMeterLevel, _outputMeterBus, dt);
        UpdateInputMeterBar(dt);
    }

    private void ConnectSignals()
    {
        // Tab buttons
        _tabProfile?.Connect("pressed", Callable.From(() => SwitchTab("Profile")));
        _tabSecurity?.Connect("pressed", Callable.From(() => SwitchTab("Security")));
        _tabPreferences?.Connect("pressed", Callable.From(() => SwitchTab("Preferences")));

        // Profile actions
        _btnChangeAvatar?.Connect("pressed", Callable.From(OnChangeAvatarPressed));
        _btnSaveProfile?.Connect("pressed", Callable.From(OnSaveProfilePressed));

        // Security actions
        _btnChangePassword?.Connect("pressed", Callable.From(OnChangePasswordPressed));
        _btnEnable2FA?.Connect("pressed", Callable.From(OnEnable2FAPressed));

        // Preferences sliders
        _masterVolumeSlider?.Connect("value_changed", Callable.From<double>(OnMasterVolumeChanged));
        _musicVolumeSlider?.Connect("value_changed", Callable.From<double>(OnMusicVolumeChanged));
        _outputDeviceOption?.Connect("item_selected", Callable.From<long>(OnOutputDeviceSelected));
        _inputDeviceOption?.Connect("item_selected", Callable.From<long>(OnInputDeviceSelected));
        _qualityOption?.Connect("item_selected", Callable.From<long>(OnQualitySelected));
        _fpsLimitToggle?.Connect("toggled", Callable.From<bool>(OnFPSLimitToggled));
        _fpsLimitSlider?.Connect("value_changed", Callable.From<double>(OnFPSLimitChanged));
        _vsyncToggle?.Connect("toggled", Callable.From<bool>(OnVSyncToggled));
        _fullscreenToggle?.Connect("toggled", Callable.From<bool>(OnFullscreenToggled));
    }

    private void SwitchTab(string tab)
    {
        if (_currentTab == tab) return;
        _currentTab = tab;

        // Update visibility
        if (_profileTab != null) _profileTab.Visible = tab == "Profile";
        if (_securityTab != null) _securityTab.Visible = tab == "Security";
        if (_preferencesTab != null) _preferencesTab.Visible = tab == "Preferences";

        UpdateTabVisuals();
    }

    private void UpdateTabVisuals()
    {
        UpdateTabButton(_tabProfile, _currentTab == "Profile");
        UpdateTabButton(_tabSecurity, _currentTab == "Security");
        UpdateTabButton(_tabPreferences, _currentTab == "Preferences");
    }

    private void UpdateTabButton(Button? button, bool isActive)
    {
        if (button == null) return;

        if (isActive && _tabActiveStyle != null)
        {
            button.AddThemeStyleboxOverride("normal", _tabActiveStyle);
            button.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f, 1f));
        }
        else if (_tabNormalStyle != null)
        {
            button.AddThemeStyleboxOverride("normal", _tabNormalStyle);
            button.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f, 1f));
        }
    }

    private void OnChangeAvatarPressed()
    {
        // TODO: Open avatar picker
    }

    private void OnSaveProfilePressed()
    {
        // TODO: Save to LumoraClient
    }

    private async void OnChangePasswordPressed()
    {
        var currentPass = _currentPasswordInput?.Text ?? "";
        var newPass = _newPasswordInput?.Text ?? "";

        if (string.IsNullOrEmpty(currentPass) || string.IsNullOrEmpty(newPass))
        {
            return;
        }

        if (_client == null)
        {
            return;
        }

        if (_btnChangePassword != null)
            _btnChangePassword.Disabled = true;

        var result = await _client.ChangePassword(currentPass, newPass);

        if (result.Success)
        {
        }
        else
        {
            GD.PrintErr($"Settings: Password change failed - {result.Message}");
        }

        // Clear fields after attempt
        if (_currentPasswordInput != null) _currentPasswordInput.Text = "";
        if (_newPasswordInput != null) _newPasswordInput.Text = "";

        if (_btnChangePassword != null)
            _btnChangePassword.Disabled = false;
    }

    private async void OnEnable2FAPressed()
    {
        if (_client == null)
        {
            return;
        }

        if (_btnEnable2FA != null)
            _btnEnable2FA.Disabled = true;

        if (_has2FA)
        {
            // Disable 2FA - would need a code input dialog in real implementation
            // For now just log - real implementation would show code input dialog
            // var result = await _client.Disable2FA(code);
        }
        else
        {
            // Enable 2FA
            var result = await _client.Enable2FA();

            if (result.Success && result.Data != null)
            {
                // TODO: Show QR code dialog with result.Data.QrCode and result.Data.RecoveryCodes
                // After user verifies with code, call _client.Verify2FA(code)
            }
            else
            {
                GD.PrintErr($"Settings: 2FA setup failed - {result.Message}");
            }
        }

        if (_btnEnable2FA != null)
            _btnEnable2FA.Disabled = false;
    }

    private void OnMasterVolumeChanged(double value)
    {
        if (_masterVolumeValue != null)
            _masterVolumeValue.Text = $"{(int)value}%";

        // TODO: Apply volume setting
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), LinearToDb((float)value / 100f));
    }

    private void OnMusicVolumeChanged(double value)
    {
        if (_musicVolumeValue != null)
            _musicVolumeValue.Text = $"{(int)value}%";

        // TODO: Apply music volume setting
        var musicBus = AudioServer.GetBusIndex("Music");
        if (musicBus >= 0)
            AudioServer.SetBusVolumeDb(musicBus, LinearToDb((float)value / 100f));
    }

    private void OnQualitySelected(long index)
    {
        string quality = index switch
        {
            0 => "Low",
            1 => "Medium",
            2 => "High",
            _ => "Medium"
        };
        // TODO: Apply graphics quality setting
    }

    private void OnFPSLimitToggled(bool enabled)
    {
        if (_fpsLimitSlider != null)
            _fpsLimitSlider.Editable = enabled;

        if (enabled)
        {
            int fps = (int)(_fpsLimitSlider?.Value ?? 60);
            Engine.MaxFps = fps;
            if (_fpsLimitValue != null)
                _fpsLimitValue.Text = $"{fps} FPS";
        }
        else
        {
            Engine.MaxFps = 0; // Unlimited
            if (_fpsLimitValue != null)
                _fpsLimitValue.Text = "Off";
        }
    }

    private void OnFPSLimitChanged(double value)
    {
        // Only apply if toggle is on
        if (_fpsLimitToggle?.ButtonPressed != true)
            return;

        int fps = (int)value;
        if (_fpsLimitValue != null)
            _fpsLimitValue.Text = $"{fps} FPS";

        Engine.MaxFps = fps;
    }

    private void OnVSyncToggled(bool enabled)
    {
        DisplayServer.WindowSetVsyncMode(
            enabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled
        );
    }

    private void OnFullscreenToggled(bool enabled)
    {
        DisplayServer.WindowSetMode(
            enabled ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed
        );
    }

    private void BuildAudioDeviceRows()
    {
        var audioVBox = GetNodeOrNull<VBoxContainer>(
            "VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/AudioSection/AudioMargin/AudioVBox");
        if (audioVBox == null) return;

        // Expand the AudioSection panel to fit the extra rows
        var audioSection = GetNodeOrNull<Panel>(
            "VBox/ContentPanel/Margin/TabContainer/PreferencesTab/PreferencesContent/AudioSection");
        if (audioSection != null)
            audioSection.CustomMinimumSize = new Vector2(0, 300);

        _outputDeviceOption = BuildDeviceRow(audioVBox, "Output Device",
            AudioServer.GetOutputDeviceList(), AudioServer.OutputDevice, out _outputActivityBar);

        _inputDeviceOption = BuildDeviceRow(audioVBox, "Input Device",
            AudioServer.GetInputDeviceList(), AudioServer.InputDevice, out _inputActivityBar);

        ResolveMeterBuses();
    }

    private OptionButton BuildDeviceRow(VBoxContainer parent, string labelText,
        string[] devices, string currentDevice, out ProgressBar activityBar)
    {
        var block = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        block.AddThemeConstantOverride("separation", 6);

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 12);

        var label = new Label
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(100, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(label);

        var option = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 32),
        };
        int selectIdx = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            option.AddItem(devices[i]);
            if (devices[i] == currentDevice)
                selectIdx = i;
        }
        option.Select(selectIdx);
        ApplyDeviceOptionTheme(option);
        row.AddChild(option);
        block.AddChild(row);

        activityBar = CreateActivityBar();
        var meterWrap = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        meterWrap.AddThemeConstantOverride("margin_left", 112);
        meterWrap.AddChild(activityBar);
        block.AddChild(meterWrap);

        parent.AddChild(block);
        return option;
    }

    private void OnOutputDeviceSelected(long index)
    {
        var device = _outputDeviceOption?.GetItemText((int)index) ?? "Default";
        AudioServer.OutputDevice = device;
        ResolveMeterBuses();
    }

    private void OnInputDeviceSelected(long index)
    {
        var device = _inputDeviceOption?.GetItemText((int)index) ?? "Default";
        AudioServer.InputDevice = device;
        ResolveMeterBuses();
    }

    private void ApplyDeviceOptionTheme(OptionButton option)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.12f, 0.20f, 1f),
            BorderColor = new Color(0.40f, 0.38f, 0.85f, 0.90f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };
        normal.SetCornerRadiusAll(8);

        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.14f, 0.16f, 0.28f, 1f);
        hover.BorderColor = new Color(0.52f, 0.50f, 0.95f, 1f);

        option.AddThemeStyleboxOverride("normal", normal);
        option.AddThemeStyleboxOverride("hover", hover);
        option.AddThemeStyleboxOverride("focus", hover);
        option.AddThemeColorOverride("font_color", new Color(0.93f, 0.93f, 0.98f, 1f));
        option.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f, 1f));
        option.AddThemeColorOverride("font_focus_color", new Color(1f, 1f, 1f, 1f));

        var popup = option.GetPopup();
        var popupPanel = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.11f, 0.18f, 0.98f),
            BorderColor = new Color(0.34f, 0.35f, 0.70f, 0.90f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };
        popupPanel.SetCornerRadiusAll(8);
        popup.AddThemeStyleboxOverride("panel", popupPanel);
        popup.AddThemeColorOverride("font_color", new Color(0.90f, 0.90f, 0.96f, 1f));
        popup.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f, 1f));
        popup.AddThemeColorOverride("font_selected_color", new Color(0.74f, 0.83f, 1f, 1f));
    }

    private static ProgressBar CreateActivityBar()
    {
        var meter = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 12),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Device activity"
        };

        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.14f, 1f),
            BorderColor = new Color(0.30f, 0.31f, 0.46f, 0.8f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1
        };
        bg.SetCornerRadiusAll(5);

        var fill = new StyleBoxFlat
        {
            BgColor = new Color(0.20f, 0.82f, 0.54f, 0.95f)
        };
        fill.SetCornerRadiusAll(5);

        meter.AddThemeStyleboxOverride("background", bg);
        meter.AddThemeStyleboxOverride("fill", fill);
        return meter;
    }

    private void ResolveMeterBuses()
    {
        _outputMeterBus = AudioServer.GetBusIndex("Master");
        if (_outputMeterBus < 0 && AudioServer.BusCount > 0)
            _outputMeterBus = 0;

        if (_outputActivityBar != null)
        {
            _outputActivityBar.TooltipText = _outputMeterBus >= 0
                ? $"Output meter on bus: {AudioServer.GetBusName(_outputMeterBus)}"
                : "No output bus found";
        }

        // Prefer a dedicated microphone monitor path so input meter does not depend
        // on project-specific bus naming/routing.
        _inputMeterBus = -1;
        _inputCaptureEffect = null;
        EnsureInputMonitorPath();

        // Fallback if monitor setup fails on this machine/runtime.
        if (_inputMeterBus < 0)
            _inputMeterBus = FindInputMeterBus();

        if (_inputActivityBar != null)
        {
            _inputActivityBar.TooltipText = _inputMeterBus >= 0
                ? $"Input meter on bus: {AudioServer.GetBusName(_inputMeterBus)}"
                : "No input monitoring bus found";
        }
    }

    private static int FindInputMeterBus()
    {
        string[] candidates = { "Voice", "Record", "Input", "Mic", "Microphone", "Capture" };
        for (int i = 0; i < candidates.Length; i++)
        {
            int bus = AudioServer.GetBusIndex(candidates[i]);
            if (bus >= 0) return bus;
        }
        return -1;
    }

    private void EnsureInputMonitorPath()
    {
        try
        {
            // Attempt to enforce input-enabled setting at runtime for this project.
            bool enabledInput = ProjectSettings.GetSetting("audio/driver/enable_input", false).AsBool();
            if (!enabledInput)
            {
                ProjectSettings.SetSetting("audio/driver/enable_input", true);
            }

            int monitorBus = AudioServer.GetBusIndex(InputMonitorBusName);
            if (monitorBus < 0)
            {
                monitorBus = AudioServer.BusCount;
                AudioServer.AddBus(monitorBus);
                AudioServer.SetBusName(monitorBus, InputMonitorBusName);
            }
            // Keep bus effectively silent to avoid feedback while still allowing metering.
            AudioServer.SetBusVolumeDb(monitorBus, -60f);

            _inputMeterBus = monitorBus;

            bool hasCaptureEffect = false;
            int effects = AudioServer.GetBusEffectCount(monitorBus);
            for (int i = 0; i < effects; i++)
            {
                var effect = AudioServer.GetBusEffect(monitorBus, i);
                if (effect is AudioEffectCapture capture)
                {
                    _inputCaptureEffect = capture;
                    hasCaptureEffect = true;
                    break;
                }
            }

            if (!hasCaptureEffect)
            {
                _inputCaptureEffect = new AudioEffectCapture();
                AudioServer.AddBusEffect(monitorBus, _inputCaptureEffect, 0);
            }

            if (_inputMonitorPlayer == null)
            {
                _inputMonitorPlayer = new AudioStreamPlayer
                {
                    Name = "InputMeterMonitor",
                    Bus = InputMonitorBusName,
                    Stream = new AudioStreamMicrophone(),
                    Autoplay = true,
                    VolumeDb = 0f
                };
                AddChild(_inputMonitorPlayer);
            }
            else
            {
                _inputMonitorPlayer.Bus = InputMonitorBusName;
                _inputMonitorPlayer.Stream ??= new AudioStreamMicrophone();
            }

            if (!_inputMonitorPlayer.Playing)
                _inputMonitorPlayer.Play();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Settings: Failed to initialize input monitor path - {ex.Message}");
            _inputMeterBus = -1;
            _inputCaptureEffect = null;
        }
    }

    private static void UpdateMeterBar(ProgressBar? bar, ref float smoothedLevel, int busIndex, float delta)
    {
        if (bar == null)
            return;

        float target = 0f;
        if (busIndex >= 0)
        {
            int channels = Math.Max(1, AudioServer.GetBusChannels(busIndex));
            for (int ch = 0; ch < channels; ch++)
            {
                float left = AudioServer.GetBusPeakVolumeLeftDb(busIndex, ch);
                float right = AudioServer.GetBusPeakVolumeRightDb(busIndex, ch);
                target = Mathf.Max(target, Mathf.Max(DbToMeter(left), DbToMeter(right)));
            }
        }

        smoothedLevel = SmoothMeter(smoothedLevel, target, delta);
        bar.Value = smoothedLevel * 100f;
        bar.Modulate = smoothedLevel > 0.05f
            ? new Color(1f, 1f, 1f, 1f)
            : new Color(0.72f, 0.72f, 0.72f, 0.80f);
    }

    private void UpdateInputMeterBar(float delta)
    {
        if (_inputActivityBar == null)
            return;

        if (_inputMonitorPlayer != null && !_inputMonitorPlayer.Playing)
            _inputMonitorPlayer.Play();

        float target = 0f;
        bool capturedAnyFrames = false;

        if (_inputCaptureEffect != null)
        {
            int frames = _inputCaptureEffect.GetFramesAvailable();
            if (frames > 0)
            {
                capturedAnyFrames = true;
                int take = Math.Min(frames, 1024);
                var buffer = _inputCaptureEffect.GetBuffer(take);
                for (int i = 0; i < buffer.Length; i++)
                {
                    var s = buffer[i];
                    target = Mathf.Max(target, Mathf.Max(Mathf.Abs(s.X), Mathf.Abs(s.Y)));
                }
            }
        }

        // Fallback to bus-peak query when capture has no frames (or no capture effect).
        if (!capturedAnyFrames && _inputMeterBus >= 0)
        {
            int channels = Math.Max(1, AudioServer.GetBusChannels(_inputMeterBus));
            for (int ch = 0; ch < channels; ch++)
            {
                float left = AudioServer.GetBusPeakVolumeLeftDb(_inputMeterBus, ch);
                float right = AudioServer.GetBusPeakVolumeRightDb(_inputMeterBus, ch);
                target = Mathf.Max(target, Mathf.Max(DbToMeter(left), DbToMeter(right)));
            }
        }

        _inputMeterLevel = SmoothMeter(_inputMeterLevel, Mathf.Clamp(target, 0f, 1f), delta);
        _inputActivityBar.Value = _inputMeterLevel * 100f;
        _inputActivityBar.Modulate = _inputMeterLevel > 0.05f
            ? new Color(1f, 1f, 1f, 1f)
            : new Color(0.72f, 0.72f, 0.72f, 0.80f);
    }

    private static float SmoothMeter(float current, float target, float delta)
    {
        float speed = target > current ? MeterAttackSpeed : MeterReleaseSpeed;
        float alpha = 1f - Mathf.Exp(-speed * Mathf.Max(delta, 0f));
        return current + (target - current) * alpha;
    }

    private static float DbToMeter(float db)
    {
        if (float.IsNaN(db) || float.IsInfinity(db))
            return 0f;

        const float floorDb = -60f;
        if (db <= floorDb)
            return 0f;

        return Mathf.Clamp((db - floorDb) / -floorDb, 0f, 1f);
    }

    private static float LinearToDb(float linear)
    {
        return linear > 0 ? 20f * Mathf.Log(linear) / Mathf.Log(10) : -80f;
    }

    /// <summary>
    /// Set the LumoraClient for API calls.
    /// </summary>
    public void SetClient(LumoraClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Set user data to populate the settings fields.
    /// </summary>
    public void SetUserData(string username, string bio, bool has2FA)
    {
        _has2FA = has2FA;

        if (_bioInput != null)
            _bioInput.Text = bio;

        if (_avatarLabel != null && !string.IsNullOrEmpty(username))
            _avatarLabel.Text = username[0].ToString().ToUpper();

        if (_twoFactorStatus != null)
        {
            _twoFactorStatus.Text = has2FA ? "Enabled" : "Not enabled";
            _twoFactorStatus.AddThemeColorOverride("font_color",
                has2FA ? new Color(0.3f, 0.8f, 0.5f, 1f) : new Color(0.9f, 0.5f, 0.5f, 1f));
        }

        if (_btnEnable2FA != null)
            _btnEnable2FA.Text = has2FA ? "Disable" : "Enable";
    }
}
