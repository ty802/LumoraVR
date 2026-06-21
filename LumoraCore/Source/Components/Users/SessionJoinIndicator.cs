// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Networking.Session;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// 3D loading indicator that appears during session joining.
/// Tracks a target world's joining progress and displays it as 3D text in front of the user.
/// </summary>
public class SessionJoinIndicator : Component
{
    /// <summary>
    /// The world being joined (target world).
    /// </summary>
    public World TargetWorld { get; set; } = null!;

    /// <summary>
    /// The session sync manager providing progress information.
    /// </summary>
    public SessionSyncManager SessionSync { get; set; } = null!;

    // Sync fields for the visual elements
    public readonly SyncRef<Slot> Visual;
    public readonly SyncRef<TextRenderer> StatusText;
    public readonly SyncRef<TextRenderer> ProgressText;

    private float _disappearThreshold = 0f;

    // Progress tracking
    private float _lastProgress = 0f;
    private string _lastStatus = "";

    public bool Visible
    {
        get => Visual?.Target?.ActiveSelf.Value ?? false;
        set
        {
            if (Visual?.Target != null)
                Visual.Target.ActiveSelf.Value = value;
        }
    }

    public SessionJoinIndicator()
    {
        Visual = new SyncRef<Slot>(this);
        StatusText = new SyncRef<TextRenderer>(this);
        ProgressText = new SyncRef<TextRenderer>(this);
    }

    public override void OnInit()
    {
        base.OnInit();

        // Build the visual hierarchy in OnInit, NOT OnAwake. OnAwake runs inside the reference controller's
        // allocation soft-block, so creating slots/components/text there logs a "RefID allocation during OnAwake"
        // warning for EVERY allocation (the text setup alone floods dozens of lines). OnInit runs after that block
        // is released - it's the correct place to spawn child objects. -xlinka
        // Create visual hierarchy
        Visual.Target = Slot.AddSlot("SessionJoinVisual");

        var fontProvider = Visual.Target.AttachComponent<Lumora.Core.Components.Assets.FontProvider>();
        fontProvider.URL.Value = new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");

        // Main status text slot
        var statusSlot = Visual.Target.AddSlot("StatusText");
        var statusRenderer = statusSlot.AttachComponent<TextRenderer>();
        statusRenderer.Text.Value = "Joining session...";
        statusRenderer.Size.Value = 0.1f;
        statusRenderer.Color.Value = color.White;
        statusRenderer.Font.Target = fontProvider;
        StatusText.Target = statusRenderer;

        // Progress details text slot (smaller, below main text)
        var progressSlot = Visual.Target.AddSlot("ProgressText");
        progressSlot.LocalPosition.Value = new float3(0, -0.15f, 0);
        var progressRenderer = progressSlot.AttachComponent<TextRenderer>();
        progressRenderer.Text.Value = "Connecting...";
        progressRenderer.Size.Value = 0.05f;
        progressRenderer.Color.Value = new color(0.8f, 0.8f, 0.8f, 1f);
        progressRenderer.Font.Target = fontProvider;
        ProgressText.Target = progressRenderer;

        // Initialize sync members created in OnAwake
        InitializeNewSyncMembers();

        // Start at visible position in front of user
        UpdatePosition();

        LumoraLogger.Log("SessionJoinIndicator: Created visual elements");
    }

    public override void OnUpdate(float delta)
    {
        if (TargetWorld == null)
        {
            LumoraLogger.Warn("SessionJoinIndicator: TargetWorld is null, destroying indicator");
            Slot.Destroy();
            return;
        }

        // Check if target world is destroyed
        if (TargetWorld.IsDestroyed)
        {
            LumoraLogger.Log("SessionJoinIndicator: Target world destroyed, removing indicator");
            Slot.Destroy();
            return;
        }

        // Check if we should disappear (world is running and fully synchronized)
        if (TargetWorld.State == World.WorldState.Running)
        {
            _disappearThreshold += delta;
            if (_disappearThreshold >= 1f) // Wait 1 second after Running to ensure stability
            {
                LumoraLogger.Log("SessionJoinIndicator: Target world is running and stable, removing indicator");
                Slot.Destroy();
                return;
            }
        }
        else
        {
            _disappearThreshold = 0f;
        }

        // Update progress from SessionSyncManager
        UpdateProgress();

        // Smoothly follow user's head
        UpdatePosition(delta);
    }

    /// <summary>
    /// Update progress display based on SessionSyncManager state.
    /// </summary>
    private void UpdateProgress()
    {
        // The session connects a moment after we're created, so pick up the sync manager lazily from the target
        // world once it exists, rather than relying solely on an external setter that can race our creation. -xlinka
        if (SessionSync == null)
            SessionSync = TargetWorld?.Session?.Sync!;

        if (SessionSync == null)
            return;

        try
        {
            var progress = SessionSync.GetInitializationProgress();
            var status = SessionSync.GetInitializationStatus();

            // Only update if changed to reduce performance impact
            if (System.Math.Abs(progress - _lastProgress) > 0.01f || status != _lastStatus)
            {
                _lastProgress = progress;
                _lastStatus = status;

                // Update main status text
                if (StatusText?.Target != null)
                {
                    StatusText.Target.Text.Value = GetMainStatusText();
                }

                // Update progress details text
                if (ProgressText?.Target != null)
                {
                    var progressPercent = (int)(progress * 100f);
                    var progressText = $"{status}";
                    
                    if (progress > 0f && TargetWorld.InitState == World.InitializationState.InitializingDataModel)
                    {
                        progressText = $"Loading world components... {progressPercent}%\n{status}";
                    }

                    ProgressText.Target.Text.Value = progressText;
                }
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"SessionJoinIndicator: Error updating progress: {ex.Message}");
        }
    }

    /// <summary>
    /// Get main status text based on world state.
    /// </summary>
    private string GetMainStatusText()
    {
        if (TargetWorld == null)
        {
            return "Joining session...";
        }

        if (TargetWorld.State == World.WorldState.Running)
        {
            return "Ready!";
        }

        return TargetWorld.InitState switch
        {
            World.InitializationState.InitializingNetwork => "Connecting to session...",
            World.InitializationState.WaitingForJoinGrant => "Requesting permission...",
            World.InitializationState.InitializingDataModel => "Loading world...",
            World.InitializationState.Finished => "Ready!",
            World.InitializationState.Failed => "Connection failed",
            _ => "Joining session..."
        };
    }

    /// <summary>
    /// Update indicator position to follow user's head.
    /// Uses InputInterface for head tracking since LocalUser may not be available in userspace.
    /// </summary>
    private void UpdatePosition(float delta = 0f)
    {
        try
        {
            // Get head position from InputInterface (works even without LocalUser)
            var inputInterface = Engine.Current?.InputInterface;
            var headDevice = inputInterface?.HeadDevice;
            if (headDevice == null || !headDevice.IsTracked)
            {
                // Use default position if no head tracking
                var defaultPos = new float3(0, 1.5f, 2f);
                Slot.GlobalPosition = defaultPos;
                Slot.GlobalRotation = floatQ.LookRotation(float3.Backward, float3.Up);
                return;
            }

            // Get head position and rotation from HeadDevice
            var headPos = new float3(headDevice.Position.X, headDevice.Position.Y, headDevice.Position.Z);
            var headRot = new floatQ(headDevice.Rotation.X, headDevice.Rotation.Y, headDevice.Rotation.Z, headDevice.Rotation.W);

            // Position indicator 1.5m in front of user, slightly down
            var forward = headRot * float3.Forward;
            var targetPosition = headPos + forward * 1.5f + float3.Down * 0.3f;

            // Face the user: build the yaw directly so the readable +Z front
            // of the text quads points at the viewer (floatQ.LookRotation
            // returns inverse rotations - see FaceLocalUser).
            var toUser = headPos - targetPosition;
            toUser.y = 0; // Keep level
            floatQ targetRotation;
            if (toUser.LengthSquared > 0.001f)
            {
                float yaw = MathF.Atan2(toUser.x, toUser.z);
                targetRotation = floatQ.AxisAngle(float3.Up, yaw);
            }
            else
            {
                targetRotation = floatQ.Identity;
            }

            if (delta > 0f)
            {
                // Smooth interpolation
                var currentPos = Slot.GlobalPosition;
                var currentRot = Slot.GlobalRotation;
                Slot.GlobalPosition = float3.Lerp(currentPos, targetPosition, delta * 3f);
                Slot.GlobalRotation = floatQ.Slerp(currentRot, targetRotation, delta * 5f);
            }
            else
            {
                // Immediate positioning for initial setup
                Slot.GlobalPosition = targetPosition;
                Slot.GlobalRotation = targetRotation;
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"SessionJoinIndicator: Error updating position: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a SessionJoinIndicator in the specified world.
    /// Create a SessionJoinIndicator in the specified world.
    /// </summary>
    public static SessionJoinIndicator CreateIndicator(World currentWorld, World targetWorld, SessionSyncManager sessionSync)
    {
        SessionJoinIndicator indicator = null!;

        currentWorld.RunSynchronously(() =>
        {
            try
            {
                var indicatorSlot = currentWorld.AddSlot("SessionJoinIndicator");
                indicator = indicatorSlot.AttachComponent<SessionJoinIndicator>();
                indicator.TargetWorld = targetWorld;
                indicator.SessionSync = sessionSync;

                LumoraLogger.Log($"SessionJoinIndicator: Created indicator in world '{currentWorld.Name}' for target '{targetWorld.Name}'");
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"SessionJoinIndicator: Error creating indicator: {ex.Message}");
            }
        });

        return indicator!;
    }

    /// <summary>
    /// Static helper to create indicator asynchronously.
    /// </summary>
    public static void CreateIndicatorAsync(World currentWorld, World targetWorld, SessionSyncManager sessionSync, Action<SessionJoinIndicator> onCreated = null!)
    {
        if (currentWorld == null || targetWorld == null || sessionSync == null)
        {
            LumoraLogger.Warn("SessionJoinIndicator: Cannot create indicator - null parameters");
            onCreated?.Invoke(null!);
            return;
        }

        // Queue creation on current world's main thread
        currentWorld.RunSynchronously(() =>
        {
            var indicator = CreateIndicator(currentWorld, targetWorld, sessionSync);
            onCreated?.Invoke(indicator);
        });
    }

    public override void OnDestroy()
    {
        LumoraLogger.Log("SessionJoinIndicator: Indicator destroyed");
        base.OnDestroy();
    }
}

