using System;
using System.Collections.Generic;
using Lumora.Core.Interaction;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.GodotUI;

/// <summary>
/// Generic Godot UI panel that renders a .tscn scene to a 3D quad.
/// Can be used directly or extended for specific panel types.
/// Implements ITouchable for laser pointer interaction.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotUIPanel : ImplementableComponent, ITouchable
{
    /// <summary>
    /// Path to the Godot scene file to load (e.g. "res://Scenes/UI/MyPanel.tscn").
    /// </summary>
    public Sync<string> ScenePath { get; private set; } = null!;

    /// <summary>
    /// Size of the UI canvas in pixels.
    /// </summary>
    public Sync<float2> Size { get; private set; } = null!;

    /// <summary>
    /// Pixels per unit for world scale.
    /// </summary>
    public Sync<float> PixelsPerUnit { get; private set; } = null!;

    /// <summary>
    /// How often to refresh live data (in seconds). 0 = every frame.
    /// </summary>
    public Sync<float> RefreshRate { get; private set; } = null!;

    /// <summary>
    /// Resolution multiplier for crisp text (default 2x).
    /// </summary>
    public Sync<int> ResolutionScale { get; private set; } = null!;

    private float _refreshTimer = 0f;

    /// <summary>
    /// Event fired when live data should be refreshed.
    /// </summary>
    public event Action? OnDataRefresh;

    /// <summary>
    /// Event fired when scene is loaded (passes button names for registration).
    /// </summary>
    public event Action<List<string>>? OnSceneLoaded;

    /// <summary>
    /// Event fired when a button is pressed.
    /// </summary>
    public event Action<string>? OnButtonPressed;

    /// <summary>
    /// Default scene path. Override in derived classes.
    /// </summary>
    protected virtual string DefaultScenePath => "";

    /// <summary>
    /// Default panel size in pixels.
    /// </summary>
    protected virtual float2 DefaultSize => new float2(500, 600);

    /// <summary>
    /// Default pixels per unit.
    /// </summary>
    protected virtual float DefaultPixelsPerUnit => 800f;

    /// <summary>
    /// Default refresh rate in seconds.
    /// </summary>
    protected virtual float DefaultRefreshRate => 0.25f;

    /// <summary>
    /// Default resolution scale.
    /// </summary>
    protected virtual int DefaultResolutionScale => 2;

    public override void OnAwake()
    {
        base.OnAwake();
        InitializeSyncMembers();
    }

    private void InitializeSyncMembers()
    {
        ScenePath = new Sync<string>(this, DefaultScenePath);
        Size = new Sync<float2>(this, DefaultSize);
        PixelsPerUnit = new Sync<float>(this, DefaultPixelsPerUnit);
        RefreshRate = new Sync<float>(this, DefaultRefreshRate);
        ResolutionScale = new Sync<int>(this, DefaultResolutionScale);

        ScenePath.OnChanged += _ => NotifyChanged();
        Size.OnChanged += _ => NotifyChanged();
        PixelsPerUnit.OnChanged += _ => NotifyChanged();
        ResolutionScale.OnChanged += _ => NotifyChanged();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        _refreshTimer += delta;
        if (_refreshTimer >= RefreshRate.Value)
        {
            _refreshTimer = 0f;
            OnDataRefresh?.Invoke();
        }
    }

    /// <summary>
    /// Get UI data to populate labels. Override in derived classes.
    /// Returns dictionary of node path -> text value.
    /// </summary>
    public virtual Dictionary<string, string> GetUIData()
    {
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Get UI colors for specific labels. Override in derived classes.
    /// Returns dictionary of node path -> color.
    /// </summary>
    public virtual Dictionary<string, color> GetUIColors()
    {
        return new Dictionary<string, color>();
    }

    /// <summary>
    /// Called when a button in the UI is pressed.
    /// </summary>
    public virtual void HandleButtonPress(string buttonPath)
    {
        if (buttonPath.EndsWith("CloseButton"))
        {
            Close();
            return;
        }
        OnButtonPressed?.Invoke(buttonPath);
    }

    /// <summary>
    /// Notify that scene was loaded.
    /// </summary>
    public void NotifySceneLoaded(List<string> buttonPaths)
    {
        OnSceneLoaded?.Invoke(buttonPaths);
    }

    /// <summary>
    /// Close the panel.
    /// </summary>
    public virtual void Close()
    {
        Slot.Destroy();
    }

    #region ITouchable Implementation

    /// <summary>
    /// Current touch source interacting with this panel.
    /// </summary>
    protected TouchSource? CurrentTouchSource { get; private set; }

    /// <summary>
    /// Whether this panel is currently being touched.
    /// </summary>
    public bool IsTouched => CurrentTouchSource != null;

    /// <summary>
    /// Touch priority for this panel (higher = more priority).
    /// </summary>
    public virtual int TouchPriority => 0;

    /// <summary>
    /// Check if this panel can be touched by the given source.
    /// </summary>
    public virtual bool CanTouch(TouchSource source)
    {
        return true; // All panels are touchable by default
    }

    /// <summary>
    /// Called when a touch event occurs on this panel.
    /// </summary>
    public virtual void OnTouch(TouchEventInfo eventInfo)
    {
        // Convert world hit point to local UV coordinates
        // This is used to simulate mouse input on the viewport
        OnTouchEvent?.Invoke(eventInfo);
    }

    /// <summary>
    /// Called when touch starts on this panel.
    /// </summary>
    public virtual void OnTouchStart(TouchSource source)
    {
        CurrentTouchSource = source;
        OnTouchStarted?.Invoke(source);
    }

    /// <summary>
    /// Called when touch ends on this panel.
    /// </summary>
    public virtual void OnTouchEnd(TouchSource source)
    {
        if (CurrentTouchSource == source)
        {
            CurrentTouchSource = null;
        }
        OnTouchEnded?.Invoke(source);
    }

    /// <summary>
    /// Event fired when touch event occurs.
    /// </summary>
    public event Action<TouchEventInfo>? OnTouchEvent;

    /// <summary>
    /// Event fired when touch starts.
    /// </summary>
    public event Action<TouchSource>? OnTouchStarted;

    /// <summary>
    /// Event fired when touch ends.
    /// </summary>
    public event Action<TouchSource>? OnTouchEnded;

    #endregion
}
