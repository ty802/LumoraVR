// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using LumoraMeshes = Lumora.Core.Components.Meshes;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// In-world 3D model-import progress indicator. The reference engine shows import/load progress IN the world
/// (a floating readout), not as a flat screen overlay - this is the equivalent: a title + status/percent text and
/// a determinate progress bar that float in front of the user and follow their view, driven by the importer's
/// progress callback, and self-destructing a beat after the import finishes.
///
/// Threading: Show/Hide marshal onto the world thread (they touch the data model); Report only writes volatile
/// fields (it's called from the off-thread import), and OnUpdate (world thread) applies them to the visuals - same
/// pattern SessionJoinIndicator uses to poll its progress. -xlinka
/// </summary>
public class ModelImportIndicator : Component
{
    public readonly SyncRef<Slot> Visual;
    public readonly SyncRef<TextRenderer> TitleText;
    public readonly SyncRef<TextRenderer> StatusText;
    public readonly SyncRef<Slot> ProgressFill;
    // The slot the model imports into - we float above it (robust: the model spawns in view). -xlinka
    public readonly SyncRef<Slot> Anchor;

    // Half the bar's width (the fill quad is BarHalfWidth*2 wide, centered, then left-anchored via scale+offset).
    private const float BarHalfWidth = 0.32f;

    // Written by Report() from the (off-thread) importer, read+applied by OnUpdate() on the world thread.
    private volatile float _progress;
    private volatile string _status = "Preparing...";
    private volatile bool _done;
    private string _title = "Importing";

    private float _doneTimer;
    private float _lastApplied = -1f;
    private string _lastStatus = null!;
    private bool _placed;

    // One import at a time (sequential), so a single current indicator is enough - mirrors the screen-space one.
    private static ModelImportIndicator _current = null!;

    public ModelImportIndicator()
    {
        Visual = new SyncRef<Slot>(this);
        TitleText = new SyncRef<TextRenderer>(this);
        StatusText = new SyncRef<TextRenderer>(this);
        ProgressFill = new SyncRef<Slot>(this);
        Anchor = new SyncRef<Slot>(this);
    }

    public override void OnInit()
    {
        base.OnInit();

        // Build the visual hierarchy in OnInit (not OnAwake) - OnAwake runs inside the RefID allocation soft-block
        // and would log a warning per allocation (see SessionJoinIndicator). -xlinka
        Visual.Target = Slot.AddSlot("ImportIndicatorVisual");

        var font = Visual.Target.AttachComponent<FontProvider>();
        font.URL.Value = new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");

        // Title line
        var titleSlot = Visual.Target.AddSlot("Title");
        titleSlot.LocalPosition.Value = new float3(0f, 0.085f, 0f);
        var titleR = titleSlot.AttachComponent<TextRenderer>();
        titleR.Text.Value = _title;
        titleR.Size.Value = 0.09f;
        titleR.Color.Value = color.White;
        titleR.Font.Target = font;
        TitleText.Target = titleR;

        // Status / percent line
        var statusSlot = Visual.Target.AddSlot("Status");
        statusSlot.LocalPosition.Value = new float3(0f, -0.01f, 0f);
        var statusR = statusSlot.AttachComponent<TextRenderer>();
        statusR.Text.Value = "Preparing...";
        statusR.Size.Value = 0.05f;
        statusR.Color.Value = new color(0.8f, 0.86f, 0.95f, 1f);
        statusR.Font.Target = font;
        StatusText.Target = statusR;

        // Progress bar background (opaque, so no transparency sorting to worry about)
        var barBg = Visual.Target.AddSlot("BarBg");
        barBg.LocalPosition.Value = new float3(0f, -0.105f, 0f);
        var bgMesh = barBg.AttachComponent<LumoraMeshes.QuadMesh>();
        bgMesh.Size.Value = new float2(BarHalfWidth * 2f + 0.02f, 0.055f);
        bgMesh.DualSided.Value = true;
        var bgRenderer = barBg.AttachComponent<MeshRenderer>();
        bgRenderer.Mesh.Target = bgMesh;
        var bgMat = barBg.AttachComponent<UnlitMaterial>();
        bgMat.Color = new colorHDR(0.10f, 0.10f, 0.13f, 1f);
        bgRenderer.Material.Target = bgMat;

        // Progress bar fill - left-anchored: a centered quad shifted + x-scaled by progress in OnUpdate.
        var fill = barBg.AddSlot("BarFill");
        fill.LocalPosition.Value = new float3(-BarHalfWidth, 0f, 0.002f);
        fill.LocalScale.Value = new float3(0.0001f, 1f, 1f);
        var fillMesh = fill.AttachComponent<LumoraMeshes.QuadMesh>();
        fillMesh.Size.Value = new float2(BarHalfWidth * 2f, 0.045f);
        fillMesh.DualSided.Value = true;
        var fillRenderer = fill.AttachComponent<MeshRenderer>();
        fillRenderer.Mesh.Target = fillMesh;
        var fillMat = fill.AttachComponent<UnlitMaterial>();
        fillMat.Color = new colorHDR(0.25f, 0.72f, 1f, 1f);
        fillRenderer.Material.Target = fillMat;
        ProgressFill.Target = fill;

        InitializeNewSyncMembers();

        UpdatePosition();

        LumoraLogger.Log("ModelImportIndicator: created in-world indicator");
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // Follow the user's view.
        UpdatePosition(delta);

        // Apply progress that Report() stashed (off-thread). Only touch the data model when it actually changed.
        float p = System.Math.Clamp(_progress, 0f, 1f);
        string s = _status ?? string.Empty;
        if (System.Math.Abs(p - _lastApplied) > 0.004f || s != _lastStatus)
        {
            _lastApplied = p;
            _lastStatus = s;

            if (StatusText.Target != null)
                StatusText.Target.Text.Value = $"{s}   {(int)(p * 100f)}%";

            if (ProgressFill.Target != null)
            {
                ProgressFill.Target.LocalScale.Value = new float3(System.Math.Max(0.0001f, p), 1f, 1f);
                ProgressFill.Target.LocalPosition.Value = new float3(BarHalfWidth * (p - 1f), 0f, 0.002f);
            }
        }

        if (_done)
        {
            _doneTimer += delta;
            if (_doneTimer >= 0.35f)
                Slot.Destroy();
        }
    }

    /// <summary>
    /// Float the indicator in front of the LOCAL USER's head and face them. Uses the in-world head slot
    /// (World.LocalUser.Root.HeadSlot) which is correct for BOTH desktop and VR - the raw VR HeadDevice is only a
    /// fallback (it reports untracked on desktop, which is why the indicator was snapping to the world origin). -xlinka
    /// </summary>
    private void UpdatePosition(float delta = 0f)
    {
        try
        {
            var head = World?.LocalUser?.Root?.HeadSlot;
            var anchor = Anchor.Target;

            float3 targetPosition;
            float3 facePos;

            if (anchor != null)
            {
                // PRIMARY: float above the imported model. The model is spawned in front of the user (in view), so
                // this is reliable even when the local-user head slot isn't resolving (which is why a head-only
                // approach left the indicator stranded at the world origin). -xlinka
                targetPosition = anchor.GlobalPosition + float3.Up * 1.9f;
                facePos = head?.GlobalPosition ?? (targetPosition - float3.Forward);
            }
            else if (head != null)
            {
                var hp = head.GlobalPosition;
                targetPosition = hp + (head.GlobalRotation * float3.Forward) * 1.1f + float3.Up * 0.18f;
                facePos = hp;
            }
            else
            {
                return; // nothing to anchor to yet - retry next frame, do NOT snap to the origin
            }

            // Face the user with a level yaw (floatQ.LookRotation is broken for facing - see FaceLocalUser). -xlinka
            var toUser = facePos - targetPosition;
            toUser.y = 0f;
            floatQ targetRotation = toUser.LengthSquared > 0.001f
                ? floatQ.AxisAngle(float3.Up, MathF.Atan2(toUser.x, toUser.z))
                : floatQ.Identity;

            // Snap into place the first valid frame, then smoothly follow so it never animates in from the origin.
            if (delta > 0f && _placed)
            {
                Slot.GlobalPosition = float3.Lerp(Slot.GlobalPosition, targetPosition, delta * 6f);
                Slot.GlobalRotation = floatQ.Slerp(Slot.GlobalRotation, targetRotation, delta * 8f);
            }
            else
            {
                Slot.GlobalPosition = targetPosition;
                Slot.GlobalRotation = targetRotation;
                if (!_placed)
                    LumoraLogger.Log($"ModelImportIndicator: placed at {targetPosition} (anchor={anchor != null}, head={head != null})");
                _placed = true;
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"ModelImportIndicator: position error: {ex.Message}");
        }
    }

    // --- Static API (call from anywhere / any thread) ---

    /// <summary>Spawn the in-world indicator for an import. <paramref name="anchor"/> is the slot the model imports
    /// into - the indicator floats above it (so it's reliably in view).</summary>
    public static void Show(World world, Slot anchor, string title)
    {
        if (world == null)
            return;

        world.RunSynchronously(() =>
        {
            try
            {
                var prev = _current;
                if (prev != null && !prev.IsDestroyed)
                    prev.Slot?.Destroy();

                var slot = world.AddSlot("ModelImportIndicator");
                var ind = slot.AttachComponent<ModelImportIndicator>();
                ind._title = title;
                if (ind.TitleText.Target != null)
                    ind.TitleText.Target.Text.Value = title;
                if (anchor != null)
                    ind.Anchor.Target = anchor;
                _current = ind;
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"ModelImportIndicator.Show: {ex.Message}");
            }
        });
    }

    /// <summary>Update progress (0..1) + status. Thread-safe; applied on the world thread by OnUpdate.</summary>
    public static void Report(float progress, string status)
    {
        var c = _current;
        if (c == null || c.IsDestroyed)
            return;
        c._progress = progress;
        if (status != null)
            c._status = status;
    }

    /// <summary>Finish: the indicator fades out + self-destructs shortly after.</summary>
    public static void Hide()
    {
        var c = _current;
        _current = null!;
        if (c != null && !c.IsDestroyed)
            c._done = true;
    }

    public override void OnDestroy()
    {
        if (_current == this)
            _current = null!;
        base.OnDestroy();
    }
}
