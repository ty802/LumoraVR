// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Networking.Session;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

[ComponentCategory("Network")]
public class SessionJoinIndicator : Component
{
    public World TargetWorld { get; set; } = null!;

    public SessionSyncManager SessionSync { get; set; } = null!;

    public readonly SyncRef<Slot> Visual;
    public readonly SyncRef<TextRenderer> StatusText;
    public readonly SyncRef<TextRenderer> ProgressText;

    private float _disappearThreshold = 0f;

    private float _lastProgress = 0f;
    private string _lastStatus = "";
    private float _settleRefreshIn;
    private const float SettleRefreshSeconds = 0.25f;

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

        // Build the visual hierarchy in OnInit, NOT OnAwake. OnAwake runs inside the ReferenceController's
        // allocation soft-block, so creating slots/components/text there logs a "RefID allocation during OnAwake"
        // warning for EVERY allocation (the text setup alone floods dozens of lines). OnInit runs after that block
        // is released - it's the correct place to spawn child objects. -xlinka
        Visual.Target = Slot.AddSlot("SessionJoinVisual");

        var fontProvider = Visual.Target.AttachComponent<Lumora.Core.Components.Assets.FontProvider>();
        fontProvider.URL.Value = new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");

        var statusSlot = Visual.Target.AddSlot("StatusText");
        var statusRenderer = statusSlot.AttachComponent<TextRenderer>();
        statusRenderer.Text.Value = "Joining session...";
        statusRenderer.Size.Value = 0.1f;
        statusRenderer.Color.Value = color.White;
        statusRenderer.Font.Target = fontProvider;
        StatusText.Target = statusRenderer;

        var progressSlot = Visual.Target.AddSlot("ProgressText");
        progressSlot.LocalPosition.Value = new float3(0, -0.15f, 0);
        var progressRenderer = progressSlot.AttachComponent<TextRenderer>();
        progressRenderer.Text.Value = "Connecting...";
        progressRenderer.Size.Value = 0.05f;
        progressRenderer.Color.Value = new color(0.8f, 0.8f, 0.8f, 1f);
        progressRenderer.Font.Target = fontProvider;
        ProgressText.Target = progressRenderer;

        InitializeNewSyncMembers();

        UpdatePosition();

        LumoraLogger.Debug("SessionJoinIndicator: Created visual elements");
    }

    public override void OnUpdate(float delta)
    {
        if (TargetWorld == null)
        {
            LumoraLogger.Warn("SessionJoinIndicator: TargetWorld is null, destroying indicator");
            Slot.Destroy();
            return;
        }

        if (TargetWorld.IsDestroyed)
        {
            LumoraLogger.Debug("SessionJoinIndicator: Target world destroyed, removing indicator");
            Slot.Destroy();
            return;
        }

        // Once the world runs, the join is not over until it is the world in view: the world manager may
        // hold its focus while it settles, and this stays up with what it is still waiting on. It goes when
        // the world is focused, or a second after the focus request went somewhere else or never came.
        if (TargetWorld.State == World.WorldState.Running)
        {
            var manager = Engine.Current?.WorldManager;
            if (manager == null || manager.FocusedWorld == TargetWorld)
            {
                LumoraLogger.Debug("SessionJoinIndicator: Target world is in view, removing indicator");
                Slot.Destroy();
                return;
            }

            if (manager.PendingFocusWorld == TargetWorld)
            {
                _disappearThreshold = 0f;
                UpdateSettleProgress(delta);
            }
            else
            {
                _disappearThreshold += delta;
                if (_disappearThreshold >= 1f)
                {
                    LumoraLogger.Debug("SessionJoinIndicator: Target world is running but not wanted in view, removing indicator");
                    Slot.Destroy();
                    return;
                }
                UpdateProgress();
            }
        }
        else
        {
            _disappearThreshold = 0f;
            UpdateProgress();
        }

        UpdatePosition(delta);
    }

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

                if (StatusText?.Target != null)
                {
                    StatusText.Target.Text.Value = GetMainStatusText();
                }

                if (ProgressText?.Target != null)
                {
                    var progressPercent = (int)(progress * 100f);
                    var progressText = $"{status}";
                    
                    if (progress > 0f && TargetWorld?.InitState == World.InitializationState.InitializingDataModel)
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

    // The world is running and waiting for its focus: say what it is still setting up.
    private void UpdateSettleProgress(float delta)
    {
        _settleRefreshIn -= delta;
        if (_settleRefreshIn > 0f)
            return;
        _settleRefreshIn = SettleRefreshSeconds;

        var waiting = TargetWorld.DescribeSettle();
        var status = string.IsNullOrEmpty(waiting) ? "Finishing up..." : waiting;
        if (status == _lastStatus)
            return;
        _lastStatus = status;

        if (StatusText?.Target != null)
            StatusText.Target.Text.Value = "Almost there...";
        if (ProgressText?.Target != null)
            ProgressText.Target.Text.Value = status;
    }

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

    private void UpdatePosition(float delta = 0f)
    {
        try
        {
            // Head comes from InputInterface: LocalUser may not exist in userspace.
            var inputInterface = Engine.Current?.InputInterface;
            var headDevice = inputInterface?.HeadDevice;
            if (headDevice == null || !headDevice.IsTracked)
            {
                var defaultPos = new float3(0, 1.5f, 2f);
                Slot.GlobalPosition = defaultPos;
                Slot.GlobalRotation = floatQ.LookRotation(float3.Backward, float3.Up);
                return;
            }

            var headPos = new float3(headDevice.Position.X, headDevice.Position.Y, headDevice.Position.Z);
            var headRot = new floatQ(headDevice.Rotation.X, headDevice.Rotation.Y, headDevice.Rotation.Z, headDevice.Rotation.W);

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

                LumoraLogger.Debug($"SessionJoinIndicator: Created indicator in world '{currentWorld.Name}' for target '{targetWorld.Name}'");
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"SessionJoinIndicator: Error creating indicator: {ex.Message}");
            }
        });

        return indicator!;
    }

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
        LumoraLogger.Debug("SessionJoinIndicator: Indicator destroyed");
        base.OnDestroy();
    }
}

