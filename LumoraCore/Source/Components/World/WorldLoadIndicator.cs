// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Localization;
using Lumora.Core.Math;
using Lumora.Nexus.Cloud.Cdn;
using Helio.UI;
using LumoraMeshes = Lumora.Core.Components.Meshes;

namespace Lumora.Core.Components;

// The plate that floats in front of you while a world is arriving: how many assets are still
// loading, how many transfers are in flight from the content service and from peers, and how many
// bytes are still to come. One per world, local only, and only for the world you are looking at.
//
// It is about WORLD LOADING and nothing else. The window opens when the world itself is coming up,
// which is a deferred start (opening a saved world) or the first seconds of a session this client
// joined, and it closes for good once that load has drained. Asset work on its own never opens it,
// so importing a model in a world that is already up shows the import readout and not this. The
// checkered placeholder on a surface says "this one is not here yet"; this says how much of the
// world's arrival is left. -xlinka
[ComponentCategory("Hidden")]
public sealed class WorldLoadIndicator : Component
{
    private const float PanelWidth = 0.52f;
    private const float PanelHeight = 0.20f;
    private const float PanelCornerRadius = 0.022f;
    private const float RimWidth = 0.006f;
    private const float PanelDepth = -0.002f;
    private const float BarWidth = 0.44f;
    private const float BarHeight = 0.022f;
    private const float BarHalfWidth = BarWidth * 0.5f;
    private const float ShowAfterSeconds = 0.35f;
    // How long after a world appears a join still counts as "arriving".
    private const float JoinWindowSeconds = 3f;
    private const float LingerSeconds = 0.8f;
    private const float RefreshSeconds = 0.2f;

    private static readonly color PanelColor = new color(0.07f, 0.08f, 0.11f, 0.88f);
    private static readonly color RimColor = new color(0.30f, 0.55f, 0.95f, 0.95f);
    private static readonly color BarTrackColor = new color(0.16f, 0.17f, 0.21f, 0.95f);
    private static readonly color BarFillColor = new color(0.32f, 0.68f, 1f, 1f);
    private static readonly color StatusColor = new color(0.78f, 0.84f, 0.94f, 1f);

    private Slot? _plate;
    private TextRenderer? _title;
    private TextRenderer? _status;
    private Slot? _fill;
    // Waiting: the world has not started loading yet (or never will). Open: it is loading. Closed:
    // that load finished, and nothing reopens it for this world.
    private enum Window { Waiting, Open, Closed }

    private Window _window = Window.Waiting;
    private float _age;
    private float _busyFor;
    private float _idleFor;
    private float _refreshIn;
    private bool _visible;
    private bool _placed;
    private long _mostBytes;
    private int _mostAssets;
    private string _lastStatus = string.Empty;

    // Attached by the world manager to every world it brings up; the component decides for itself
    // when there is anything to say.
    public static void Attach(World world)
    {
        if (world == null || world.IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (world.IsDestroyed || world.RootSlot == null)
                return;
            var slot = world.RootSlot.AddLocalSlot("World Load Indicator");
            slot.Persistent.Value = false;
            slot.AttachComponent<WorldLoadIndicator>();
        });
    }

    public override void OnInit()
    {
        base.OnInit();
        Compose();
        SetVisible(false);
    }

    private void Compose()
    {
        if (Slot == null || World == null)
            return;
        var theme = NameplateTheme.For(World);

        _plate = Slot.AddLocalSlot("Plate");
        _plate.Persistent.Value = false;
        _plate.AttachComponent<FaceLocalUser>();

        var rim = _plate.AddSlot("Rim");
        rim.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        var rimMesh = rim.AttachComponent<LumoraMeshes.RoundedQuadRingMesh>();
        rimMesh.Size.Value = new float2(PanelWidth, PanelHeight);
        rimMesh.CornerRadius.Value = PanelCornerRadius;
        rimMesh.RimWidth.Value = RimWidth;
        rimMesh.Color = RimColor;
        AttachRenderer(rim, rimMesh, theme);

        var panel = _plate.AddSlot("Panel");
        panel.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        var panelMesh = panel.AttachComponent<LumoraMeshes.RoundedQuadMesh>();
        panelMesh.Size.Value = new float2(PanelWidth, PanelHeight);
        panelMesh.CornerRadius.Value = PanelCornerRadius;
        panelMesh.Color = PanelColor;
        AttachRenderer(panel, panelMesh, theme);

        var title = _plate.AddSlot("Title");
        title.LocalPosition.Value = new float3(0f, 0.048f, 0f);
        _title = title.AttachComponent<TextRenderer>();
        if (theme?.Font != null)
            _title.Font.Target = theme.Font;
        _title.Size.Value = 0.042f;
        _title.Color.Value = color.White;
        _title.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        _title.VerticalAlign.Value = TextVerticalAlignment.Middle;
        _title.OutlineThickness.Value = 1.2f;
        _title.Text.Value = "WorldLoad.Title".AsLocale("Loading world").Resolve();

        var status = _plate.AddSlot("Status");
        status.LocalPosition.Value = new float3(0f, -0.004f, 0f);
        _status = status.AttachComponent<TextRenderer>();
        if (theme?.Font != null)
            _status.Font.Target = theme.Font;
        _status.Size.Value = 0.028f;
        _status.Color.Value = StatusColor;
        _status.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        _status.VerticalAlign.Value = TextVerticalAlignment.Middle;
        _status.Text.Value = string.Empty;

        var track = _plate.AddSlot("BarTrack");
        track.LocalPosition.Value = new float3(0f, -0.055f, 0f);
        var trackMesh = track.AttachComponent<LumoraMeshes.RoundedQuadMesh>();
        trackMesh.Size.Value = new float2(BarWidth, BarHeight);
        trackMesh.CornerRadius.Value = BarHeight * 0.5f;
        trackMesh.Color = BarTrackColor;
        AttachRenderer(track, trackMesh, theme);

        _fill = track.AddSlot("BarFill");
        _fill.LocalPosition.Value = new float3(-BarHalfWidth, 0f, 0.001f);
        _fill.LocalScale.Value = new float3(0.0001f, 1f, 1f);
        var fillMesh = _fill.AttachComponent<LumoraMeshes.RoundedQuadMesh>();
        fillMesh.Size.Value = new float2(BarWidth, BarHeight * 0.72f);
        fillMesh.CornerRadius.Value = BarHeight * 0.36f;
        fillMesh.Color = BarFillColor;
        AttachRenderer(_fill, fillMesh, theme);
    }

    private static void AttachRenderer(Slot slot, LumoraMeshes.ProceduralMesh mesh, NameplateTheme? theme)
    {
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        if (theme?.PlateMaterial != null)
            renderer.Material.Target = theme.PlateMaterial;
        // A floating readout has no business darkening the floor under it.
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        if (world == null || world.IsDestroyed)
            return;

        _age += delta;
        bool focused = Engine.Current?.WorldManager?.FocusedWorld == world;
        var (downloads, _, remainingBytes, totalBytes) = TransferRegistry.Totals();
        int assets = Asset.LoadingCount;
        var transferer = Engine.Current?.ActiveSessionTransferer;
        int peer = transferer != null && focused ? transferer.DownloadJobCount : 0;

        // Only a world coming up opens the window: a deferred start, or the opening seconds of a
        // session this client joined rather than hosts. Everything else, an import included, is
        // somebody else's readout.
        if (_window == Window.Waiting)
        {
            bool joining = !world.IsAuthority && _age <= JoinWindowSeconds;
            if (world.IsSessionStartPending || joining)
                _window = Window.Open;
            else if (_age > JoinWindowSeconds)
                _window = Window.Closed;
        }

        if (_window != Window.Open)
        {
            if (_visible)
                SetVisible(false);
            return;
        }

        bool busy = focused && (world.IsSessionStartPending || assets > 0 || downloads > 0 || peer > 0);

        if (busy)
        {
            _busyFor += delta;
            _idleFor = 0f;
            if (assets > _mostAssets) _mostAssets = assets;
            if (totalBytes > _mostBytes) _mostBytes = totalBytes;
        }
        else
        {
            _idleFor += delta;
            if (_idleFor > LingerSeconds)
            {
                // The load drained. This world only arrives once, so the window shuts for good.
                _busyFor = 0f;
                _mostAssets = 0;
                _mostBytes = 0;
                _window = Window.Closed;
            }
        }

        // A flicker of loading, the kind a single texture makes, never earns the plate.
        bool show = busy ? _busyFor >= ShowAfterSeconds : _visible && _idleFor < LingerSeconds;
        if (show != _visible)
            SetVisible(show);
        if (!show)
            return;

        UpdatePosition(delta);

        _refreshIn -= delta;
        if (_refreshIn > 0f)
            return;
        _refreshIn = RefreshSeconds;

        string status;
        if (!busy)
        {
            status = "WorldLoad.Done".AsLocale("Ready").Resolve();
        }
        else if (world.IsSessionStartPending)
        {
            status = "WorldLoad.Opening".AsLocale("Opening the world").Resolve();
        }
        else
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (assets > 0)
                parts.Add(assets == 1 ? "WorldLoad.OneAsset".AsLocale("1 asset").Resolve() : "WorldLoad.Assets".AsLocale("{0} assets", assets).Resolve());
            if (downloads + peer > 0)
                parts.Add((downloads + peer) == 1 ? "WorldLoad.OneTransfer".AsLocale("1 transfer").Resolve() : "WorldLoad.Transfers".AsLocale("{0} transfers", downloads + peer).Resolve());
            if (remainingBytes > 0)
                parts.Add("WorldLoad.Remaining".AsLocale("{0} left", FormatBytes(remainingBytes)).Resolve());
            status = parts.Count > 0 ? string.Join(" · ", parts) : "WorldLoad.Working".AsLocale("Working").Resolve();
        }
        if (status != _lastStatus && _status != null && !_status.IsDestroyed)
        {
            _status.Text.Value = status;
            _lastStatus = status;
        }

        float fraction;
        if (!busy)
            fraction = 1f;
        else if (_mostBytes > 0)
            fraction = 1f - (float)remainingBytes / _mostBytes;
        else if (_mostAssets > 0)
            fraction = 1f - (float)assets / _mostAssets;
        else
            fraction = 0.05f;
        if (_fill != null && !_fill.IsDestroyed)
        {
            float clamped = System.Math.Clamp(fraction, 0.02f, 1f);
            _fill.LocalScale.Value = new float3(clamped, 1f, 1f);
            _fill.LocalPosition.Value = new float3(-BarHalfWidth + BarHalfWidth * clamped, 0f, 0.001f);
        }
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        if (_plate != null && !_plate.IsDestroyed)
            _plate.ActiveSelf.Value = visible;
        if (!visible)
            _placed = false;
    }

    // Just ahead of the head and a little below the eye line, eased so it drifts with a turn rather
    // than snapping. The plate child does the facing.
    private void UpdatePosition(float delta)
    {
        var head = World?.LocalUser?.Root?.HeadSlot;
        if (head == null || head.IsDestroyed || Slot == null || Slot.IsDestroyed)
            return;
        var target = head.GlobalPosition + (head.GlobalRotation * float3.Backward) * 1.1f + float3.Up * -0.12f;
        if (_placed)
            Slot.GlobalPosition = float3.Lerp(Slot.GlobalPosition, target, System.Math.Min(1f, delta * 6f));
        else
            Slot.GlobalPosition = target;
        _placed = true;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:0} KB";
        return $"{bytes} B";
    }
}
